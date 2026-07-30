using System;
using System.Collections;
using System.IO;
using System.Text;
using Microsoft.Extensions.Options;
using MM4LB.Enums;
using MM4LB.Models;
using Newtonsoft.Json;

namespace MM4LB.Services;

/// <summary>
/// Servicio encargado de persistir y restaurar la configuración de la aplicación.
/// 
/// - Guarda AppSettings en un archivo JSON local (Tracker.ini)
/// - Restaura la configuración al iniciar la aplicación
/// - Utiliza un conversor personalizado para serializar/deserializar tipos basados en Enumeration
/// 
/// Este servicio garantiza que tipos como <see cref="AspectRatioSettings"/> se almacenen como texto
/// y se reconstruyan correctamente al cargar.
/// </summary>
public class PersistAndRestoreService
{
    #region Attributes
    private readonly ExceptionService _exceptionService;
    private readonly AppSettings _appSettings;
    private static readonly string _settingsFileFolder = "Tracker";
    private static readonly string _settingsFileName = "Tracker.ini";
    private readonly string _folderPath;
    private readonly string _filePath;

    /// <summary>
    /// Carpeta donde se almacena el fichero de configuración (<c>%LocalAppData%\MM4LB</c>). Es la fuente
    /// única de esta ubicación; otros servicios (p. ej. el backup de imágenes) cuelgan sus carpetas de aquí.
    /// </summary>
    public static string SettingsFolderPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        _settingsFileFolder);
    private static readonly JsonSerializer Serializer = new JsonSerializer
    {
        Converters =
        {
            new EnumerationJsonConverter<AspectRatioSettings>(),
            new EnumerationJsonConverter<ImageResolutionSettings>(),
            new EnumerationJsonConverter<VideoDownloadQualitySettings>(),
            new EnumerationJsonConverter<SettingsType>()
        }
    };
    #endregion

    #region Subclasses
    /// <summary>
    /// Conversor JSON genérico para cualquier tipo basado en <see cref="Enumeration"/>.
    /// 
    /// Serializa usando la propiedad <see cref="Enumeration.Value"/> (string legible).
    /// Deserializa buscando primero por Value y luego por Key.
    /// </summary>
    public class EnumerationJsonConverter<T> : JsonConverter<T> where T : Enumeration, new()
    {
        /// <summary>
        /// Serializa un tipo Enumeration escribiendo su valor textual.
        /// </summary>
        public override void WriteJson(JsonWriter writer, T? value, JsonSerializer serializer)
        {
            writer.WriteValue(value?.Value);
        }

        /// <summary>
        /// Deserializa un tipo Enumeration a partir de su representación textual o numérica.
        /// </summary>
        public override T ReadJson(JsonReader reader, Type objectType, T? existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null!;

            var raw = reader.Value?.ToString();
            if (string.IsNullOrWhiteSpace(raw)) return null!;

            // Intento 1: buscar por Value (nombre legible)
            var byValue = Enumeration.FromValue<T>(raw);
            if (byValue != null) return byValue;

            // Intento 2: buscar por Key (entero)
            if (int.TryParse(raw, out int key))
            {
                var byKey = Enumeration.FromKey<T>(key);
                if (byKey != null) return byKey;
            }

            throw new JsonSerializationException($"Unknown {typeof(T).Name} '{raw}'");
        }
    }
    #endregion

    #region Constructors
    /// <summary>
    /// Inicializa el servicio configurando rutas y obteniendo AppSettings desde DI.
    /// </summary>
    public PersistAndRestoreService(ExceptionService exceptionService, IOptions<AppSettings> appSettings)
    {
        _exceptionService = exceptionService ?? throw new ArgumentNullException(nameof(exceptionService));
        _appSettings = appSettings?.Value ?? throw new ArgumentNullException(nameof(appSettings));

        _folderPath = SettingsFolderPath;
        _filePath = Path.Combine(_folderPath, _settingsFileName);
    }
        #endregion

    #region Methods (public)
    /// <summary>
    /// Persiste la configuración actual de la aplicación en un archivo JSON local.
    /// 
    /// - Crea la carpeta si no existe
    /// - Serializa AppSettings usando el conversor para Enumeration
    /// - Guarda el archivo en formato legible (Indented)
    /// </summary>
    /// <summary>
    /// Construye los <see cref="JsonSerializerSettings"/> con los converters de <see cref="Enumeration"/> usados tanto
    /// para el .ini como para los templates. Fuente única para no desincronizar las listas de converters.
    /// </summary>
    private static JsonSerializerSettings BuildJsonSettings(bool indented) => new()
    {
        Formatting = indented ? Formatting.Indented : Formatting.None,
        Converters =
        {
            new EnumerationJsonConverter<AspectRatioSettings>(),
            new EnumerationJsonConverter<ImageResolutionSettings>(),
            new EnumerationJsonConverter<VideoDownloadQualitySettings>(),
            new EnumerationJsonConverter<SettingsType>()
        }
    };

    /// <summary>Serializa la configuración actual (<see cref="AppSettings"/>) a JSON indentado. Lo usa el guardado de templates.</summary>
    public string SerializeCurrentSettings() => JsonConvert.SerializeObject(_appSettings, BuildJsonSettings(indented: true));

    /// <summary>
    /// Vuelca sobre el <see cref="AppSettings"/> vivo la configuración contenida en <paramref name="json"/> (misma
    /// mecánica que <see cref="RestoreData"/> pero desde una cadena arbitraria). Lo usa la carga de templates. Las
    /// secciones de <paramref name="skipSections"/> NO se aplican (p. ej. "Theme": el tema no forma parte del template).
    /// </summary>
    public void RestoreFromJson(string json, params string[] skipSections)
    {
        IDictionary? properties;
        try
        {
            properties = JsonConvert.DeserializeObject<IDictionary>(json, BuildJsonSettings(indented: false));
        }
        catch (Exception ex)
        {
            ExceptionService.LogToFile(ex, "Could not parse the template settings; it was ignored.");
            return;
        }

        if (properties is null)
            return;

        foreach (string section in skipSections)
            properties.Remove(section);

        _appSettings.BindProperties(properties, Serializer);
    }

    public void PersistData()
    {
        try
        {
            if (!Directory.Exists(_folderPath))
                Directory.CreateDirectory(_folderPath);

            var settings = BuildJsonSettings(indented: true);

            // Serialización completa de AppSettings
            var json = JsonConvert.SerializeObject(_appSettings, settings);

            // Escritura ATÓMICA: se escribe a un temporal y se reemplaza, para no dejar el .ini truncado si el
            // proceso muere a mitad de escritura (PersistData corre en cada cierre y en cada cambio de ajuste).
            string tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, json, Encoding.UTF8);
            if (File.Exists(_filePath))
                File.Replace(tempPath, _filePath, null);
            else
                File.Move(tempPath, _filePath);
        }
        catch (Exception ex)
        {
            // LogToFile (no Handle): PersistData corre en el cierre (un diálogo no se renderiza) y en cada
            // cambio de ajuste (un diálogo cada vez molestaría). Al menos deja rastro del fallo de guardado.
            ExceptionService.LogToFile(ex, "Could not save the application settings.");
        }
    }

    /// <summary>
    /// Restaura la configuración de la aplicación leyendo el archivo JSON local (<c>Tracker.ini</c>).
    ///
    /// Resiliente a problemas de configuración al arrancar: NUNCA bloquea el arranque ni muestra un diálogo.
    /// - Si el archivo no existe, no hace nada (se arranca con los valores por defecto).
    /// - Si el archivo no se puede leer (bloqueado, permisos…), se arranca con defaults y se deja rastro en el log,
    ///   SIN tocar el fichero (el fallo puede ser transitorio y el próximo arranque podría leerlo bien).
    /// - Si el archivo está corrupto (JSON inválido), se arranca con defaults, se aparta el fichero dañado a
    ///   <c>Tracker.ini.corrupt</c> (para no volver a fallar en cada arranque y poder inspeccionarlo) y se registra.
    /// - Si el JSON es válido, <see cref="AppSettings.BindProperties"/> aplica cada sección de forma resiliente: si
    ///   una sección falla, la registra y sigue con el resto, de modo que un valor inválido solo descarta ESA sección.
    /// </summary>
    public void RestoreData()
    {
        if (!File.Exists(_filePath))
            return;

        string json;
        try
        {
            json = File.ReadAllText(_filePath, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            ExceptionService.LogToFile(ex, "Could not read the settings file; starting with default values.");
            return;
        }

        IDictionary? properties;
        try
        {
            properties = JsonConvert.DeserializeObject<IDictionary>(json, BuildJsonSettings(indented: false));
        }
        catch (Exception ex)
        {
            ExceptionService.LogToFile(ex, "The settings file is corrupt; starting with default values.");
            QuarantineCorruptSettingsFile();
            return;
        }

        if (properties is not null)
            _appSettings.BindProperties(properties, Serializer);
    }

    /// <summary>
    /// Aparta el <c>Tracker.ini</c> corrupto renombrándolo a <c>Tracker.ini.corrupt</c> (sobreescribiendo uno anterior),
    /// para que el siguiente arranque parta limpio de valores por defecto y el fichero dañado quede para inspección.
    /// Nunca lanza: si no se puede mover, solo se registra.
    /// </summary>
    private void QuarantineCorruptSettingsFile()
    {
        try
        {
            string corruptPath = _filePath + ".corrupt";
            if (File.Exists(corruptPath))
                File.Delete(corruptPath);
            File.Move(_filePath, corruptPath);
        }
        catch (Exception ex)
        {
            ExceptionService.LogToFile(ex, "Could not move aside the corrupt settings file.");
        }
    }
    #endregion
}
