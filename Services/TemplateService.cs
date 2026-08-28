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
/// Gestiona los "templates" de configuración: ficheros <c>*.json</c> (+ <c>*.jpg</c> opcional) en
/// <c>Assets/Templates</c>, que se distribuyen con la aplicación y solo se pueden CARGAR (no crear, editar ni borrar).
/// Cargar un template aplica su configuración EN CALIENTE (sin reiniciar). El tema se excluye del template.
/// </summary>
public class TemplateService
{
    #region Nested types
    /// <summary>Un template disponible para cargar: nombre visible y rutas de su JSON y de su miniatura.</summary>
    public sealed record TemplateEntry(string Name, string JsonPath, string ImagePath);
    #endregion

    #region Attributes
    private readonly AppSettings _appSettings;
    private readonly PersistAndRestoreService _persistAndRestoreService;
    private readonly SharedDataService _sharedDataService;
    #endregion

    #region Properties
    /// <summary>Carpeta de los templates, dentro de los assets de la app.</summary>
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
    /// <summary>
    /// Todos los templates disponibles para CARGAR (<c>Assets/Templates/*.json</c>, en orden alfabético). El nombre
    /// visible es el del fichero; la miniatura es el <c>.jpg</c> con el mismo nombre base, si existe.
    /// </summary>
    public IReadOnlyList<TemplateEntry> GetAllTemplates()
    {
        var list = new List<TemplateEntry>();

        if (!Directory.Exists(AppTemplatesFolderPath))
            return list;

        foreach (string jsonPath in Directory.EnumerateFiles(AppTemplatesFolderPath, "*.json").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            string name = Path.GetFileNameWithoutExtension(jsonPath);
            string image = Path.Combine(AppTemplatesFolderPath, name + ".jpg");
            list.Add(new TemplateEntry(name, jsonPath, File.Exists(image) ? image : string.Empty));
        }

        return list;
    }

    /// <summary>
    /// Carga un template por la ruta de su JSON: lo vuelca sobre <see cref="AppSettings"/>, lo
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
