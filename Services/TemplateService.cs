using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using MM4LB.Models;

namespace MM4LB.Services;

/// <summary>
/// Gestiona los "templates" de configuración. Hay DOS tipos:
/// - De USUARIO: <see cref="SlotCount"/> slots fijos (<c>slot{n}.json/.jpg/.name</c>) en
///   <c>%LocalAppData%\Tracker\Templates</c>. Se graban/sobrescriben y se pueden cargar.
/// - De APLICACIÓN (built-in): ficheros <c>*.json</c> (+ <c>*.jpg</c> opcional) en <c>Assets/Templates</c>. Solo se
///   pueden CARGAR (no editar ni borrar).
/// Cargar un template aplica su configuración EN CALIENTE (sin reiniciar). El tema se excluye del template.
/// </summary>
public class TemplateService
{
    #region Nested types
    /// <summary>Estado de un slot de USUARIO: número (1..N), si está ocupado, su nombre visible y la ruta del JPG.</summary>
    public sealed record SlotInfo(int Slot, bool Occupied, string Name, string ImagePath);

    /// <summary>Un template disponible para cargar (de app o de usuario): nombre, rutas y origen.</summary>
    public sealed record TemplateEntry(string Name, string JsonPath, string ImagePath, bool IsBuiltIn, int UserSlot);
    #endregion

    #region Constants
    /// <summary>Número de slots de template de USUARIO disponibles.</summary>
    public const int SlotCount = 3;
    #endregion

    #region Attributes
    private readonly AppSettings _appSettings;
    private readonly PersistAndRestoreService _persistAndRestoreService;
    private readonly SharedDataService _sharedDataService;
    #endregion

    #region Properties
    /// <summary>Carpeta donde se guardan los templates de USUARIO (cuelga de la carpeta del .ini).</summary>
    public static string TemplatesFolderPath => Path.Combine(PersistAndRestoreService.SettingsFolderPath, "Templates");

    /// <summary>Carpeta de los templates de APLICACIÓN (built-in), dentro de los assets de la app.</summary>
    public static string AppTemplatesFolderPath => Path.Combine(AppContext.BaseDirectory, "Assets", "Templates");
    #endregion

    #region Constructor
    public TemplateService(IOptions<AppSettings> appSettings, PersistAndRestoreService persistAndRestoreService, SharedDataService sharedDataService)
    {
        _appSettings = appSettings?.Value ?? throw new ArgumentNullException(nameof(appSettings));
        _persistAndRestoreService = persistAndRestoreService;
        _sharedDataService = sharedDataService;
    }
    #endregion

    #region Methods (public)
    /// <summary>Estado de los <see cref="SlotCount"/> slots de USUARIO (1..N), ocupados o vacíos, en orden. Para el diálogo de grabar.</summary>
    public IReadOnlyList<SlotInfo> GetUserSlots()
    {
        var slots = new List<SlotInfo>(SlotCount);
        for (int slot = 1; slot <= SlotCount; slot++)
        {
            bool occupied = File.Exists(JsonPath(slot));
            string name = occupied ? ReadName(slot) : string.Empty;
            string image = File.Exists(ImagePath(slot)) ? ImagePath(slot) : string.Empty;
            slots.Add(new SlotInfo(slot, occupied, name, image));
        }
        return slots;
    }

    /// <summary>
    /// Todos los templates disponibles para CARGAR: primero los de aplicación (<c>Assets/Templates/*.json</c>, orden
    /// alfabético), luego los de usuario ocupados (slots 1..N). Para el selector de la toolbar.
    /// </summary>
    public IReadOnlyList<TemplateEntry> GetAllTemplates()
    {
        var list = new List<TemplateEntry>();

        // Built-in (Assets/Templates): nombre = nombre de fichero; jpg opcional con el mismo nombre base.
        if (Directory.Exists(AppTemplatesFolderPath))
        {
            foreach (string jsonPath in Directory.EnumerateFiles(AppTemplatesFolderPath, "*.json").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileNameWithoutExtension(jsonPath);
                string image = Path.Combine(AppTemplatesFolderPath, name + ".jpg");
                list.Add(new TemplateEntry(name, jsonPath, File.Exists(image) ? image : string.Empty, IsBuiltIn: true, UserSlot: 0));
            }
        }

        // De usuario (slots ocupados).
        foreach (SlotInfo slot in GetUserSlots())
        {
            if (slot.Occupied)
                list.Add(new TemplateEntry(slot.Name, JsonPath(slot.Slot), slot.ImagePath, IsBuiltIn: false, UserSlot: slot.Slot));
        }

        return list;
    }

    /// <summary>
    /// Graba (o SOBREESCRIBE) el slot indicado con la configuración actual, el nombre dado y el pantallazo. Si no se
    /// aporta pantallazo, se elimina el JPG previo del slot (para que no quede una imagen obsoleta).
    /// </summary>
    public async Task SaveToSlotAsync(int slot, string name, byte[]? screenshotJpeg)
    {
        if (slot < 1 || slot > SlotCount)
            return;

        // Vuelca el estado en vivo de los VMs a AppSettings ANTES de serializar (normalmente solo se hace al cerrar la
        // app), para que el template capture el estado ACTUAL (layout, slots de widgets, config de gráficas) y no el de arranque.
        _sharedDataService.NotifySaveConfigRequested();

        Directory.CreateDirectory(TemplatesFolderPath);

        await File.WriteAllTextAsync(JsonPath(slot), _persistAndRestoreService.SerializeCurrentSettings(), Encoding.UTF8);
        await File.WriteAllTextAsync(NamePath(slot), CleanName(name), Encoding.UTF8);

        if (screenshotJpeg is { Length: > 0 })
            await File.WriteAllBytesAsync(ImagePath(slot), screenshotJpeg);
        else if (File.Exists(ImagePath(slot)))
            File.Delete(ImagePath(slot));
    }

    /// <summary>
    /// Carga un template (de app o de usuario) por la ruta de su JSON: lo vuelca sobre <see cref="AppSettings"/>, lo
    /// persiste al .ini y lo aplica EN CALIENTE (config de gráficas, layout de widgets; el tema se excluye). No hace
    /// nada si el fichero no existe.
    /// </summary>
    public void LoadTemplate(string jsonPath)
    {
        if (string.IsNullOrEmpty(jsonPath) || !File.Exists(jsonPath))
            return;

        // El TEMA no forma parte del template: se excluye para no tocarlo al cargar.
        _persistAndRestoreService.RestoreFromJson(File.ReadAllText(jsonPath, Encoding.UTF8), "Theme");
        _persistAndRestoreService.PersistData();

        ApplyLoadedSettingsLive();
    }
    #endregion

    #region Methods (private)
    private static string JsonPath(int slot) => Path.Combine(TemplatesFolderPath, $"slot{slot}.json");
    private static string ImagePath(int slot) => Path.Combine(TemplatesFolderPath, $"slot{slot}.jpg");
    private static string NamePath(int slot) => Path.Combine(TemplatesFolderPath, $"slot{slot}.name");

    private static string ReadName(int slot)
    {
        try
        {
            string namePath = NamePath(slot);
            if (File.Exists(namePath))
            {
                string name = File.ReadAllText(namePath, Encoding.UTF8).Trim();
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
        }
        catch { /* nombre ilegible: se usa el genérico */ }
        return $"Slot {slot}";
    }

    /// <summary>Recorta el nombre visible (se guarda tal cual en el .name; no es un nombre de fichero).</summary>
    private static string CleanName(string name)
    {
        string trimmed = (name ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? "Template" : trimmed;
    }

    /// <summary>
    /// Re-aplica en caliente la configuración recién cargada (un template cambia todo MENOS el tema, que se excluye):
    /// logging, cabecera de widgets, modo de grupos de toolbar y un evento global de "recarga" que reorganiza el
    /// layout de widgets y recarga la config de cada ViewModel.
    /// </summary>
    private void ApplyLoadedSettingsLive()
    {
        ExceptionService.LoggingEnabled = _appSettings.General.ExceptionLoggingEnabled;
        // El tema NO se toca al cargar un template (se excluye del restore).

        _sharedDataService.NotifyWidgetHeaderVisibilityChanged();
        _sharedDataService.NotifySettingsReloaded();
    }
    #endregion
}
