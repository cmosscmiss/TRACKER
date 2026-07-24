using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using MM4LB.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MM4LB.Models;

/// <summary>
/// Contenedor principal de toda la configuración de la aplicación.
/// 
/// Responsabilidades:
/// - Agrupar todas las secciones de configuración (General, LaunchBox, Theme, etc.)
/// - Proveer modelos fuertemente tipados para cada sección
/// - Permitir la carga dinámica desde diccionarios JSON
/// - Facilitar la validación de rutas y parámetros
/// 
/// Esta clase actúa como el núcleo de configuración de la aplicación.
/// </summary>
public class AppSettings
{
    #region Properties
    public GeneralSettings General { get; set; } = new GeneralSettings();
    public LaunchBoxSettings LaunchBox { get; set; } = new LaunchBoxSettings();
    public GameImagesDashboardControlSettings GameImagesDashboardControl { get; set; } = new GameImagesDashboardControlSettings();
    public GameImagesRegionDashboardControlSettings GameImagesRegionDashboardControl { get; set; } = new GameImagesRegionDashboardControlSettings();
    public GameListControlSettings GameListControl { get; set; } = new GameListControlSettings();
    public GamesAuditControlSettings GamesAuditControl { get; set; } = new GamesAuditControlSettings();
    public ImageAuditControlSettings ImageAuditControl { get; set; } = new ImageAuditControlSettings();
    public ImageCollectionImportControlSettings ImageCollectionImportControl { get; set; } = new ImageCollectionImportControlSettings();
    public ImageGridControlSettings ImageGridControl { get; set; } = new ImageGridControlSettings();
    public ImageTypeControlSettings ImageTypeControl { get; set; } = new ImageTypeControlSettings();
    public LayoutSelectorControlSettings LayoutSelectorControl { get; set; } = new LayoutSelectorControlSettings();
    public PlatformListControlSettings PlatformListControl { get; set; } = new PlatformListControlSettings();
    public StatsGlobalControlSettings StatsGlobalControl { get; set; } = new StatsGlobalControlSettings();
    public StatsPlatformControlSettings StatsPlatformControl { get; set; } = new StatsPlatformControlSettings();
    public WebViewControlSettings WebViewControl { get; set; } = new WebViewControlSettings();
    public ToolsControlSettings ToolsControl { get; set; } = new ToolsControlSettings();
    public WindowSettings Window { get; set; } = new WindowSettings();
    public ThemeSettings Theme { get; set; } = new ThemeSettings();

    /// <summary>
    /// Resultado de validación de rutas de LaunchBox.
    /// Se utiliza para comprobar si la estructura de carpetas es válida.
    /// </summary>
    public record LaunchBoxValidationResult(bool IsLaunchBoxFolderPathValid, bool IsLaunchBoxDataFolderPathValid, bool IsLaunchBoxPlatformsFolderValid, bool IsLaunchBoxPlatformsXmlFileValid, bool IsLaunchBoxSettingsXmlFileValid)
    {
        public bool LaunchBoxFoldersValid => IsLaunchBoxFolderPathValid && IsLaunchBoxDataFolderPathValid && IsLaunchBoxPlatformsFolderValid && IsLaunchBoxPlatformsXmlFileValid && IsLaunchBoxSettingsXmlFileValid;
    }
    #endregion

    #region Nested Settings Classes
    /// <summary>
    /// Configuración general de la aplicación.
    /// Contiene parámetros globales no relacionados con LaunchBox ni UI.
    /// </summary>
    public class GeneralSettings
    {
        /// <summary>
        /// Memoria máxima de bitmaps decodificados a mantener en caché, en megabytes.
        /// </summary>
        public double CacheSize { get; set; } = 4096;

        /// <summary>
        /// Activa el logging de excepciones a <c>MM4LB.log</c> (herramienta de depuración). Por defecto activado.
        /// </summary>
        public bool ExceptionLoggingEnabled { get; set; } = true;

        /// <summary>
        /// Idioma de la interfaz (código ISO de dos letras, p. ej. "en", "es"). Por defecto inglés. Se aplica en
        /// caliente vía <see cref="MM4LB.Services.LocalizationService"/>; ver docs/Plan-Localizacion-Ayuda.md.
        /// </summary>
        public string Language { get; set; } = "en";

        /// <summary>
        /// Si es true (por defecto), se muestran los tooltips y los paneles de ayuda (iconos "Help"/TeachingTips). El
        /// toggle de ayuda del footer lo alterna en caliente (HelpService, fase F0 del plan de localización/ayuda).
        /// </summary>
        public bool HelpTooltipsEnabled { get; set; } = true;

        /// <summary>
        /// Cómo se muestran los grupos de botones excluyentes de las toolbars de los widgets: como botones sueltos
        /// (<see cref="ToolbarGroupsDisplayMode.Expanded"/>), colapsados en un ToggleSplitButton por grupo
        /// (<see cref="ToolbarGroupsDisplayMode.Collapsed"/>), o automático por tamaño de la toolbar
        /// (<see cref="ToolbarGroupsDisplayMode.Auto"/>, por defecto).
        /// </summary>
        public ToolbarGroupsDisplayMode ToolbarGroupsDisplayMode { get; set; } = ToolbarGroupsDisplayMode.Auto;

        /// <summary>
        /// Última categoría abierta en la ventana de configuración (nombre del <c>SettingsSectionKind</c>, p. ej.
        /// "General", "Theme"). Se guarda al aceptar y se restaura al reabrir el diálogo. Se persiste como texto para no
        /// acoplar el modelo a un enum de la capa de ViewModels; si el valor no existe, se cae a la primera categoría.
        /// </summary>
        public string SettingsLastSection { get; set; } = "General";

        /// <summary>
        /// Indica si los widgets del panel (modo <see cref="MM4LB.Controls.Views.WidgetDisplayMode.Default"/>) muestran
        /// su barra de cabecera completa (con título). Por defecto true. Si es false, la cabecera se reduce a una barra
        /// fina que conserva solo el asa de arrastre, para que se solape menos con el contenido SIN bloquear el
        /// drag&amp;drop. NO afecta a los widgets fijos (banda del panel), que nunca tienen cabecera. Se lee al arrancar
        /// (no se aplica en caliente).
        /// </summary>
        public bool ShowWidgetHeader { get; set; } = true;

        /// <summary>
        /// Si es true (por defecto), el visor de eventos del pie está siempre visible; si es false, solo se muestra
        /// cuando la consola NO está colocada como widget visible en el WidgetPanel.
        /// </summary>
        public bool FooterEventViewerAlwaysVisible { get; set; } = true;

        /// <summary>
        /// Volumen (0–100) de la reproducción de vídeo en toda la aplicación (preview del dashboard y vídeo de la
        /// ficha de plataforma). 0 = silencio; cualquier valor mayor reproduce con sonido a ese nivel. Por defecto 0
        /// para mantener el comportamiento silencioso histórico.
        /// </summary>
        public double VideoVolume { get; set; } = 0;

        /// <summary>
        /// Silencio global de la reproducción de vídeo. Cuando es true, el volumen efectivo es 0 conservando el nivel
        /// de <see cref="VideoVolume"/> (al desactivarlo se recupera). Lo controla el botón de mute del footer.
        /// </summary>
        public bool IsMuted { get; set; } = false;

        /// <summary>
        /// Si es true (por defecto), borrar un medio del grid (comando de borrar) pide confirmación mediante un
        /// diálogo. El propio diálogo incluye un check para apagar esta confirmación, así que el usuario puede
        /// desactivarla desde ahí. Cuando es false, el borrado se aplica directamente (sigue siendo deshacible
        /// desde el activity log).
        /// </summary>
        public bool PromptBeforeDeleteImage { get; set; } = true;

        /// <summary>
        /// Umbral de ancho (DIPs) por debajo del cual un grupo de pastillas oculta su icono para dejar sitio al
        /// texto (ver <see cref="MM4LB.Controls.Views.WidgetStatCardControl"/>.IconCollapsePillCount). Hay un valor
        /// por nº de pastillas del grupo; se mide sobre el ancho del PROPIO grupo, así que sirve igual en fila
        /// completa o en fracción. Constantes internas (no se persisten): ajuste centralizado de "todas las
        /// pastillas" desde aquí. Sube el valor para que el icono desaparezca antes (a más ancho).
        /// </summary>
        [JsonIgnore]
        public double PillIconCollapseWidth2 { get; set; } = 360;

        [JsonIgnore]
        public double PillIconCollapseWidth3 { get; set; } = 480;

        [JsonIgnore]
        public double PillIconCollapseWidth4 { get; set; } = 580;

        /// <summary>
        /// Ancho (DIPs) del GRUPO de pastillas por debajo del cual su valor principal (la estadística) reduce su
        /// fuente a <see cref="PillValueFontSizeCompact"/>. Es un paso de degradación PREVIO a ocultar el icono, así
        /// que cada valor debe ser MAYOR que su PillIconCollapseWidthN. Se mide sobre el ancho del grupo (no de cada
        /// pastilla) para que TODAS las pastillas del grupo cambien de fuente a la vez. Un valor por nº de pastillas.
        /// Interno (no se persiste).
        /// </summary>
        [JsonIgnore]
        public double PillValueShrinkWidth2 { get; set; } = 460;

        [JsonIgnore]
        public double PillValueShrinkWidth3 { get; set; } = 620;

        [JsonIgnore]
        public double PillValueShrinkWidth4 { get; set; } = 760;

        /// <summary>Tamaño de fuente del valor principal de una pastilla en modo compacto (ver <see cref="PillValueShrinkWidth3"/>). El tamaño normal es 22.</summary>
        [JsonIgnore]
        public double PillValueFontSizeCompact { get; set; } = 16;
    }

    /// <summary>
    /// Configuración relacionada con LaunchBox.
    /// 
    /// Responsabilidades:
    /// - Almacenar rutas configuradas por el usuario
    /// - Calcular rutas internas derivadas automáticamente
    /// - Validar la estructura de carpetas de LaunchBox
    /// </summary>
    public class LaunchBoxSettings
    {
        private string _launchBoxFolder = string.Empty;
        private string _platformIconPackFolder = string.Empty;
        private string _platformLogoPackFolder = string.Empty;

        public string LaunchBoxFolder
        {
            get => _launchBoxFolder;
            set
            {
                _launchBoxFolder = value;
                SetInternalSettings();
            }
        }

        [JsonIgnore]
        public string PlatformIconPackFolder
        {
            get => _platformIconPackFolder;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    _platformIconPackFolder = "";
                    return;
                }

                _platformIconPackFolder = Path.Combine(LaunchBoxFolder, "Images", "Media Packs", "Platform Icons", value, "Platforms");
            }
        }

        [JsonIgnore]
        public string PlatformLogoPackFolder
        {
            get => _platformLogoPackFolder;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    _platformLogoPackFolder = "";
                    return;
                }

                _platformLogoPackFolder = Path.Combine(LaunchBoxFolder, "Images", "Media Packs", "Platform Clear Logos", value, "Platforms");
            }
        }

        [JsonIgnore]
        public bool LaunchBoxFoldersValid = false;
        [JsonIgnore]
        public string LaunchBoxDataFolder { get; private set; } = "";
        [JsonIgnore]
        public string LaunchBoxPlatformsFolder { get; private set; } = "";
        [JsonIgnore]
        public string LaunchboxPlatformsXmlFile { get; private set; } = "";
        [JsonIgnore]
        public string LaunchboxSettingsXmlFile { get; private set; } = "";
        [JsonIgnore]
        public string LaunchboxGamesDbFile { get; private set; } = "";
        [JsonIgnore]
        public readonly string[] AllowedImageExtensions = { ".bmp", ".gif", ".jpeg", ".jpg", ".png", ".wmf" };
        [JsonIgnore]
        public readonly string[] AllowedVideoExtensions = { ".mp4", ".mkv", ".avi", ".wmv", ".m4v", ".webm" };
        [JsonIgnore]
        public readonly int ImageLowResDecodePixelWidth = 200;
        [JsonIgnore]
        public readonly int ImageHighResDecodePixelWidth = 350;

        /// <summary>
        /// Calcula rutas internas derivadas de la carpeta base
        /// y valida la estructura de LaunchBox.
        /// </summary>
        private void SetInternalSettings()
        {
            LaunchBoxDataFolder = Path.Combine(LaunchBoxFolder, "Data");
            LaunchBoxPlatformsFolder = Path.Combine(LaunchBoxFolder, "Data", "Platforms");
            LaunchboxPlatformsXmlFile = Path.Combine(LaunchBoxFolder, "Data", "Platforms.xml");
            LaunchboxSettingsXmlFile = Path.Combine(LaunchBoxFolder, "Data", "Settings.xml");
            LaunchboxGamesDbFile = Path.Combine(LaunchBoxFolder, "Metadata", "LaunchBox.Metadata.db");
            LaunchBoxFoldersValid = LaunchBoxPathValidator.Validate(LaunchBoxFolder).LaunchBoxFoldersValid;
        }
    }

    public class GameImagesDashboardControlSettings
    {
        public bool IsHorizontalView { get; set; } = true;
        public bool IsSearchStringsPanelVisible { get; set; } = true;
        public double Width { get; set; } = 200;
        public double Height { get; set; } = 400;

        /// <summary>
        /// Resolución objetivo con la que se descargan los vídeos de YouTube desde el WebView (240p…1080p,
        /// siempre con audio). Si la exacta no existe, el descargador cae a la más cercana disponible.
        /// </summary>
        public VideoDownloadQualitySettings VideoDownloadQuality { get; set; } = VideoDownloadQualitySettings.P1080;

        /// <summary>
        /// Si el preview de vídeo del dashboard muestra los controles de reproducción (transport controls).
        /// False por defecto (sin controles). El sonido lo gobierna ahora <see cref="GeneralSettings.VideoVolume"/>.
        /// </summary>
        public bool ShowVideoControls { get; set; } = false;

        /// <summary>
        /// Criterios ordenados de preselección del medio (imagen/vídeo) al seleccionar un juego o al procesarlo:
        /// se aplican en cascada sobre los medios del juego del image type activo. Por defecto: mayor resolución
        /// ("Dimensions") y, a igualdad, mayor tamaño ("Size"). Array (no List) para que Newtonsoft lo REEMPLACE
        /// al restaurar en vez de acumularlo. Cada Type (SettingsType) se persiste como texto vía su converter.
        /// </summary>
        public GameImageCriterion[] ImageSelectionCriteria { get; set; } =
        {
            new GameImageCriterion { Type = SettingsType.Image, CriteriaName = "1st:", IsActive = true, ID = 1 },
            new GameImageCriterion { Type = SettingsType.Image, CriteriaName = "2nd:", IsActive = true, ID = 2 },
        };

        /// <summary>
        /// Criterios ordenados de procesado del medio conservado al procesar un juego: construyen su nuevo
        /// nombre/ruta. Por defecto: región "Discard" (sacar de la subcarpeta de región), sin sufijo, y nombre =
        /// título del juego. Mismo formato/persistencia que <see cref="ImageSelectionCriteria"/>.
        /// </summary>
        public GameImageCriterion[] ImageProcessingCriteria { get; set; } =
        {
            new GameImageCriterion { Type = SettingsType.Region, CriteriaName = "Region:", IsActive = true, ID = 2 },
            new GameImageCriterion { Type = SettingsType.FileNameSuffix, CriteriaName = "Suffix:", IsActive = true, ID = 2 },
            new GameImageCriterion { Type = SettingsType.FileName, CriteriaName = "File Name:", IsActive = true, ID = 4 },
        };
    }

    /// <summary>
    /// Configuración del widget GameImagesRegionDashboard: gestiona las imágenes del juego seleccionado POR
    /// REGIÓN (selector de regiones favoritas + buckets "otras regiones"/"sin región"). Comparte la mayoría de
    /// ajustes visuales con <see cref="GameImagesDashboardControlSettings"/>, pero mantiene criterios de
    /// selección/proceso PROPIOS (independientes del dashboard normal) y sus regiones favoritas.
    /// </summary>
    public class GameImagesRegionDashboardControlSettings
    {
        public bool IsHorizontalView { get; set; } = true;
        public bool IsSearchStringsPanelVisible { get; set; } = true;
        public double Width { get; set; } = 200;
        public double Height { get; set; } = 400;

        /// <summary>Resolución de descarga de vídeos (espejo del dashboard normal).</summary>
        public VideoDownloadQualitySettings VideoDownloadQuality { get; set; } = VideoDownloadQualitySettings.P1080;

        /// <summary>Si el preview de vídeo muestra los transport controls.</summary>
        public bool ShowVideoControls { get; set; } = false;

        /// <summary>
        /// Regiones favoritas del selector (máximo 3). Cada una es un elemento fijo del selector; a ellas se
        /// suman siempre los buckets "otras regiones" y "sin región". Por defecto: Europe, World, Spain. Array
        /// (no List) para que Newtonsoft lo REEMPLACE al restaurar en vez de acumularlo. Se persiste como texto
        /// vía <see cref="EnumerationJsonConverter{ImageRegion}"/> (registrado en PersistAndRestoreService).
        /// </summary>
        public ImageRegion[] FavouriteRegions { get; set; } =
        {
            ImageRegion.Europe, ImageRegion.World, ImageRegion.Spain,
        };

        /// <summary>
        /// Al procesar (Process region / Process &amp; next|previous), si las imágenes del bucket "otras regiones"
        /// (con región pero no favorita) se BORRAN todas (true) o se dejan intactas (false). True por defecto.
        /// </summary>
        public bool PurgeNonFavouriteRegions { get; set; } = true;

        /// <summary>
        /// Criterios de preselección del medio principal, aplicados POR REGIÓN. Mismos defaults que el dashboard
        /// normal (mayor resolución y, a igualdad, mayor tamaño). Independientes del dashboard normal.
        /// </summary>
        public GameImageCriterion[] ImageSelectionCriteria { get; set; } =
        {
            new GameImageCriterion { Type = SettingsType.Image, CriteriaName = "1st:", IsActive = true, ID = 1 },
            new GameImageCriterion { Type = SettingsType.Image, CriteriaName = "2nd:", IsActive = true, ID = 2 },
        };

        /// <summary>
        /// Criterios de procesado del medio conservado por región. La región se CONSERVA siempre (keep-region:
        /// ID = 1 = "Keep"), de modo que las imágenes conservadas se quedan en su subcarpeta de región. Sufijo y
        /// nombre = título del juego, como el dashboard normal.
        /// </summary>
        public GameImageCriterion[] ImageProcessingCriteria { get; set; } =
        {
            new GameImageCriterion { Type = SettingsType.Region, CriteriaName = "Region:", IsActive = true, ID = 1 },
            new GameImageCriterion { Type = SettingsType.FileNameSuffix, CriteriaName = "Suffix:", IsActive = true, ID = 2 },
            new GameImageCriterion { Type = SettingsType.FileName, CriteriaName = "File Name:", IsActive = true, ID = 4 },
        };
    }

    public class GameListControlSettings
    {
        public string SelectedGame { get; set; } = "";
    }


    /// <summary>
    /// Configuración específica del control de auditoría de juegos.
    /// Almacena el estado de los filtros de estado del juego activos.
    /// </summary>
    public class GamesAuditControlSettings
    {
        public bool InCollection { get; set; }
        public bool InLaunchboxDb { get; set; }
        public bool InCollectionNotInLaunchboxDb { get; set; }
    }

    /// <summary>
    /// Configuración específica del control de auditoría de imágenes.
    /// Se utiliza para almacenar parámetros visuales o funcionales del control.
    /// </summary>
    public class ImageAuditControlSettings
    {
        public AspectRatioSettings AspectRatio = AspectRatioSettings.AR11;
        public ImageResolutionSettings Resolution = ImageResolutionSettings.High;
        public bool ListView = false;
        public bool GridView = true;
        public int ItemSize = 250;
    }

    /// <summary>
    /// Configuración del widget "Tools": la herramienta abierta (pestaña del FlipView) y los ajustes de cada
    /// herramienta que contiene. La orquesta <see cref="MM4LB.Controls.ViewModels.ToolsViewModel"/>.
    /// </summary>
    public class ToolsControlSettings
    {
        public int SelectedToolIndex = 0;
        public ToolsMediaAuditSettings MediaAudit = new();
        public ToolsOrphanSettings OrphanTool = new();
        public ToolsSharedSettings SharedTool = new();
    }

    /// <summary>Ajustes persistidos de la herramienta de auditoría de media (primera tool).</summary>
    public class ToolsMediaAuditSettings
    {
        public bool ShowOnlyDiscrepancies = false;
        public bool FilterBySelectedType = false;
    }

    /// <summary>Ajustes persistidos de la herramienta de huérfanos: vista y filtro, más el aspecto,
    /// la resolución y el tamaño de miniatura de su galería (vista en grid).</summary>
    public class ToolsOrphanSettings
    {
        public bool IsTableView = true;
        public bool FilterBySelectedType = false;
        public AspectRatioSettings AspectRatio = AspectRatioSettings.AR11;
        public ImageResolutionSettings Resolution = ImageResolutionSettings.High;
        public int ItemSize = 250;
    }

    /// <summary>Ajustes persistidos de la herramienta de media compartida: vista y filtro, más el aspecto,
    /// la resolución y el tamaño de miniatura de su galería (vista en grid).</summary>
    public class ToolsSharedSettings
    {
        public bool IsTableView = true;
        public bool FilterBySelectedType = false;
        public AspectRatioSettings AspectRatio = AspectRatioSettings.AR11;
        public ImageResolutionSettings Resolution = ImageResolutionSettings.High;
        public int ItemSize = 250;
    }

    /// <summary>
    /// Configuración específica del control de importación de colecciones de imágenes.
    /// Almacena el aspecto, la resolución y el tamaño de la galería de la carpeta, qué vistas están
    /// activas (imágenes y/o juegos) y los filtros de coincidencia de imágenes.
    /// </summary>
    public class ImageCollectionImportControlSettings
    {
        public AspectRatioSettings AspectRatio = AspectRatioSettings.AR11;
        public ImageResolutionSettings Resolution = ImageResolutionSettings.High;
        public int ItemSize = 250;
        public bool ImagesView = true;
        public bool GamesView = false;
        public bool MissingImages = false;
        public bool OneImage = false;
        public bool MoreThanOneImage = false;
    }

    /// <summary>
    /// Configuración específica del control de galería de imágenes (ImageGrid).
    /// Almacena el aspecto, la resolución y el tamaño de los elementos de la galería.
    /// </summary>
    public class ImageGridControlSettings
    {
        public AspectRatioSettings AspectRatio = AspectRatioSettings.AR11;
        public ImageResolutionSettings Resolution = ImageResolutionSettings.High;
        public int ItemSize = 250;
    }

    /// <summary>
    /// Configuración específica del control de lista de juegos.
    /// Se utiliza para almacenar parámetros visuales o funcionales del control.
    /// </summary>
    public class ImageTypeControlSettings
    {
        public MediaType[] FavouriteImageTypes { get; set; } = [MediaType.Box3D, MediaType.BoxFront, MediaType.BoxBack, MediaType.BoxSpine, MediaType.ClearLogo, MediaType.FanartBackground];
        public MediaType? SelectedImageSet
        {
            get; set;
        }
    }

    public class LayoutSelectorControlSettings
    {
        public double Gap { get; set; } = 16;
        public double CornerRadius { get; set; } = 18;

        /// <summary>
        /// Margen exterior (DIPs) del panel de widgets en izquierda, derecha y abajo. El margen superior lo gestiona
        /// la barra de herramientas flotante y no se ve afectado. Por defecto 8 (equivalente al look histórico de
        /// gap/2 con el gap por defecto).
        /// </summary>
        public double PanelMargin { get; set; } = 8;
        public int SelectedLayout { get; set; } = LayoutType.TwoColumns50.Key;
        public Dictionary<string, int> WidgetSlots { get; set; } = new();

        /// <summary>
        /// Tamaños (factores estrella) de filas y columnas ajustados con los splitters, por índice de
        /// layout. Cada layout conserva su propia distribución.
        /// </summary>
        public Dictionary<int, LayoutSizes> LayoutSizes { get; set; } = new();
    }

    /// <summary>
    /// Distribución de tamaños de un layout ajustada por el usuario mediante los grid splitters.
    /// Los valores son factores estrella (proporcionales); un 0 indica columna/fila no usada.
    /// </summary>
    public class LayoutSizes
    {
        public double[] Columns { get; set; } = System.Array.Empty<double>();

        /// <summary>
        /// Ratio de partición (fracción de altura de la fila superior, 0–1) de cada columna dividida en dos
        /// filas, indexado por índice de columna. Cada columna conserva su reparto propio e independiente.
        /// Las columnas no partidas no aparecen.
        /// </summary>
        public Dictionary<int, double> RowRatiosByColumn { get; set; } = new();
    }


    public class PlatformListControlSettings
    {
        public bool BehavesAsList { get; set; } = true;
        public string SelectedPlatform { get; set; } = "";
    }

    public class WebViewControlSettings
    {
        public bool SearchViaGoogle { get; set; } = true;
        public bool SearchViaSteamGridDB { get; set; } = false;
    }

    /// <summary>
    /// Configuración del widget de estadísticas globales (StatsGlobal): la gráfica activa del FlipView y, por cada
    /// una de las cuatro gráficas comparativas, su tipo de gráfica, orden y Top X. Cada gráfica tiene un ajuste
    /// nombrado (no posicional) para no romperse en silencio si se reordenan las páginas del FlipView.
    /// </summary>
    public class StatsGlobalControlSettings
    {
        /// <summary>Índice de la gráfica visible en el FlipView (0..3).</summary>
        public int SelectedChartIndex { get; set; }

        /// <summary>Gráfica 1: juegos por plataforma, apilada (en colección / en LaunchBox / sólo colección).</summary>
        public ChartViewSettings GameSetChart { get; set; } = new ChartViewSettings();

        /// <summary>Gráfica 2: nº de juegos por plataforma.</summary>
        public ChartViewSettings GameCountChart { get; set; } = new ChartViewSettings();

        /// <summary>Gráfica 3: nº de imágenes por plataforma.</summary>
        public ChartViewSettings ImageCountChart { get; set; } = new ChartViewSettings();

        /// <summary>Gráfica 4: tamaño en disco (GB) por plataforma.</summary>
        public ChartViewSettings ImageSizeChart { get; set; } = new ChartViewSettings();
    }

    /// <summary>
    /// Configuración del widget de estadísticas de plataforma (StatsPlatform): la gráfica activa del FlipView y, por
    /// cada gráfica con toolbar de tipo (las tres últimas; la primera —cobertura por juego— es un chart sin toolbar),
    /// su tipo de gráfica, orden y Top X. Ajustes nombrados (no posicionales).
    /// </summary>
    public class StatsPlatformControlSettings
    {
        /// <summary>Índice de la gráfica visible en el FlipView (0..3).</summary>
        public int SelectedChartIndex { get; set; }

        /// <summary>Si el panel de resumen de cobertura está visible (toggle de la barra superior).</summary>
        public bool IsCoverageVisible { get; set; } = true;

        /// <summary>Ámbito de tipos del eje X de la gráfica de cobertura (favoritos / presentes en la plataforma).</summary>
        public CoverageTypeScope CoverageScope { get; set; } = CoverageTypeScope.Favourites;

        /// <summary>Gráfica "Coverage distribution" (nº de juegos por tramo de cobertura de favoritos).</summary>
        public ChartViewSettings CoverageDistributionChart { get; set; } = new ChartViewSettings();

        /// <summary>Gráfica "Coverage - Image type" (% de juegos con ≥1 imagen de cada tipo).</summary>
        public ChartViewSettings CoverageByTypeChart { get; set; } = new ChartViewSettings();

        /// <summary>Gráfica "Image set - Image type" (nº de imágenes por tipo).</summary>
        public ChartViewSettings ImagesByTypeChart { get; set; } = new ChartViewSettings();
    }

    /// <summary>
    /// Ajustes persistentes de una gráfica de <c>ChartTypeSelectorControl</c>: tipo de gráfica, orden de los
    /// elementos y Top X (0 = todos).
    /// </summary>
    public class ChartViewSettings
    {
        public ChartType ChartType { get; set; } = ChartType.Column;
        public SortMode SortOrder { get; set; } = SortMode.None;
        public int TopN { get; set; }
    }

    /// <summary>
    /// Configuración persistente de la ventana principal.
    /// </summary>
    public class WindowSettings
    {
        /// <summary>
        /// Coordenada X de la ventana en pantalla.
        /// </summary>
        public int X { get; set; } = 100;

        /// <summary>
        /// Coordenada Y de la ventana en pantalla.
        /// </summary>
        public int Y { get; set; } = 100;

        /// <summary>
        /// Anchura de la ventana.
        /// </summary>
        public int Width { get; set; } = 2800;

        /// <summary>
        /// Altura de la ventana.
        /// </summary>
        public int Height { get; set; } = 1400;

        /// <summary>
        /// Indica si la ventana estaba maximizada al cerrar la aplicación.
        /// </summary>
        public bool IsMaximized
        {
            get; set;
        }

        /// <summary>
        /// Indica si existe una posición guardada válida.
        /// </summary>
        public bool HasSavedPlacement
        {
            get; set;
        }
    }

    /// <summary>
    /// Utilidad estática para validar la estructura de carpetas de LaunchBox.
    /// </summary>
    public static class LaunchBoxPathValidator
    {
        /// <summary>
        /// Comprueba si las carpetas y archivos esenciales de LaunchBox existen.
        /// Devuelve un resultado detallado indicando qué partes son válidas.
        /// </summary>
        public static LaunchBoxValidationResult Validate(string? launchBoxFolderPath)
        {
            if (string.IsNullOrWhiteSpace(launchBoxFolderPath))
            {
                return new LaunchBoxValidationResult(false, false, false, false, false);
            }

            string launchBoxDataFolder = Path.Combine(launchBoxFolderPath, "Data");
            string platformsFolder = Path.Combine(launchBoxDataFolder, "Platforms");
            string platformsXml = Path.Combine(launchBoxDataFolder, "Platforms.xml");
            string settingsXml = Path.Combine(launchBoxDataFolder, "Settings.xml");

            bool isFolderValid = Directory.Exists(launchBoxFolderPath) && File.Exists(Path.Combine(launchBoxFolderPath, "Launchbox.exe"));
            bool isDataValid = Directory.Exists(launchBoxDataFolder);
            bool isPlatformsFolderValid = Directory.Exists(platformsFolder);
            bool isPlatformsXmlValid = File.Exists(platformsXml);
            bool isSettingsXmlValid = File.Exists(settingsXml);

            return new LaunchBoxValidationResult(isFolderValid, isDataValid, isPlatformsFolderValid, isPlatformsXmlValid, isSettingsXmlValid
            );
        }
    }

    /// <summary>
    /// Configuración del sistema de temas.
    /// 
    /// Responsabilidades:
    /// - Almacenar el nombre del tema activo
    /// - Contener el catálogo completo de temas disponibles
    /// - Proveer los valores de colores para cada tema
    /// </summary>
    public class ThemeSettings
    {
        public string Name { get; set; } = "Cyber City";
        public bool BackgroundImageFramed { get; set; } = false;
        public bool BackgroundImageTinted { get; set; } = true;
        public int OverlayImageBlur { get; set; } = 10;
        public double OverlayImageOpacity { get; set; } = 0.35;
        public bool RandomTheme { get; set; } = false;
        public double TintBrightness { get; set; } = 1.0;
        public double TintOpacity { get; set; } = 0.5;
        public double TintSaturation { get; set; } = 1.0;

        [JsonIgnore]
        public Dictionary<string, ThemeDefinition> Themes
        {
            get; set;
        } = new()
        {
            ["Cyber City"] = new ThemeDefinition
            {
                AccentColor = "#ff00e5",
                AccentLightColor = "#ef77ec",
                AccentDarkColor = "#670865",
                BackgroundColor = "#101010",
                BackgroundLightColor = "#181818",
                CardBackgroundLightColor = "#2f2f2f",
                CardBackgroundColor = "#20212f",
                TextColor = "#ffffff",
                TextSecondaryColor = "#b2b2b2",
                DangerColor = "#FF0000",
                SuccessColor = "#00FF00",
                WarningColor = "#fff000",
                BadgeNoImageColor = "#FF0000",
                BadgeOneImageColor = "#33AA33",
                BadgeMoreThanOneImageColor = "#3870c4",
                BackgroundImage = "Backgrounds/MM4LB-SC-CYBER-CITY-NO-FRAME.png",
                LogoImage = "MM4LB-LOGO-CYBER-CITY.png",
                OverlayImage = "Backgrounds/MM4LB-SC-CYBER-CITY-OVERLAY.jpg",
                AssetsPath = "Assets/Theme/CyberCity/"
            },
            ["Dead Space"] = new ThemeDefinition
            {
                AccentColor = "#ff8040",
                AccentLightColor = "#F4A627",
                AccentDarkColor = "#d85513",
                BackgroundColor = "#101010",
                BackgroundLightColor = "#181818",
                CardBackgroundLightColor = "#2f2f2f",
                CardBackgroundColor = "#20212f",
                TextColor = "#ffffff",
                TextSecondaryColor = "#b2b2b2",
                DangerColor = "#FF0000",
                SuccessColor = "#00FF00",
                WarningColor = "#fff000",
                BadgeNoImageColor = "#FF0000",
                BadgeOneImageColor = "#33AA33",
                BadgeMoreThanOneImageColor = "#3870c4",
                BackgroundImage = "Backgrounds/MM4LB-SC-DEAD-SPACE-NO-FRAME.png",
                LogoImage = "MM4LB-LOGO-DEAD-SPACE.png",
                OverlayImage = "Backgrounds/MM4LB-SC-DEAD-SPACE-OVERLAY.jpg",
                AssetsPath = "Assets/Theme/DeadSpace/"
            },
            ["Doom"] = new ThemeDefinition
            {
                AccentColor = "#ff0000",
                AccentLightColor = "#fc4545",
                AccentDarkColor = "#941515",
                BackgroundColor = "#101010",
                BackgroundLightColor = "#181818",
                CardBackgroundLightColor = "#2f2f2f",
                CardBackgroundColor = "#20212f",
                TextColor = "#ffffff",
                TextSecondaryColor = "#b2b2b2",
                DangerColor = "#CC3300",
                SuccessColor = "#33AA33",
                WarningColor = "#fff000",
                BadgeNoImageColor = "#FF0000",
                BadgeOneImageColor = "#33AA33",
                BadgeMoreThanOneImageColor = "#3870c4",
                BackgroundImage = "Backgrounds/MM4LB-SC-DOOM-NO-FRAME.png",
                LogoImage = "MM4LB-LOGO-DOOM.png",
                OverlayImage = "Backgrounds/MM4LB-SC-DOOM-OVERLAY.jpg",
                AssetsPath = "Assets/Theme/Doom/"
            },
            ["LoL"] = new ThemeDefinition
            {
                AccentColor = "#5cd65a",
                AccentLightColor = "#a3d439",
                AccentDarkColor = "#44a143",
                BackgroundColor = "#101010",
                BackgroundLightColor = "#181818",
                CardBackgroundLightColor = "#2f2f2f",
                CardBackgroundColor = "#20212f",
                TextColor = "#ffffff",
                TextSecondaryColor = "#b2b2b2",
                DangerColor = "#CC3300",
                SuccessColor = "#33AA33",
                WarningColor = "#fff000",
                BadgeNoImageColor = "#FF0000",
                BadgeOneImageColor = "#33AA33",
                BadgeMoreThanOneImageColor = "#3870c4",
                BackgroundImage = "Backgrounds/MM4LB-SC-LOL-NO-FRAME.png",
                LogoImage = "MM4LB-LOGO-LOL.png",
                OverlayImage = "Backgrounds/MM4LB-SC-LOL-OVERLAY.jpg",
                AssetsPath = "Assets/Theme/LoL/"
            }
        };
    }

    /// <summary>
    /// Representa un tema visual completo.
    /// Contiene los colores base utilizados por ThemeService.
    /// </summary>
    public class ThemeDefinition
    {
        public string AccentColor
        {
            get; set;
        } = string.Empty;
        public string AccentLightColor
        {
            get; set;
        } = string.Empty;
        public string AccentDarkColor
        {
            get; set;
        } = string.Empty;
        public string BackgroundColor
        {
            get; set;
        } = string.Empty;
        public string BackgroundLightColor
        {
            get; set;
        } = string.Empty;
        public string CardBackgroundLightColor
        {
            get; set;
        } = string.Empty;
        public string CardBackgroundColor
        {
            get; set;
        } = string.Empty;
        public string TextColor
        {
            get; set;
        } = string.Empty;
        public string TextSecondaryColor
        {
            get; set;
        } = string.Empty;
        public string DangerColor
        {
            get; set;
        } = string.Empty;
        public string SuccessColor
        {
            get; set;
        } = string.Empty;
        public string WarningColor
        {
            get; set;
        } = string.Empty;
        public string BadgeNoImageColor
        {
            get; set;
        } = string.Empty;
        public string BadgeOneImageColor
        {
            get; set;
        } = string.Empty;
        public string BadgeMoreThanOneImageColor
        {
            get; set;
        } = string.Empty;
        public string BackgroundImage
        {
            get; set;
        } = string.Empty;
        public string LogoImage
        {
            get; set;
        } = string.Empty;
        public string OverlayImage
        {
            get; set;
        } = string.Empty;
        public string AssetsPath
        {
            get; set;
        } = string.Empty;
    }
    #endregion

    #region Methods (public)
    /// <summary>
    /// Enlaza dinámicamente un diccionario de propiedades JSON
    /// con las secciones correspondientes de AppSettings.
    /// 
    /// Permite cargar configuraciones desde archivos externos
    /// sin necesidad de mapear manualmente cada propiedad.
    /// </summary>
    public void BindProperties(IDictionary properties, JsonSerializer Serializer)
    {
        foreach (DictionaryEntry entry in properties)
        {
            if (entry.Value is not JObject section)
            {
                continue;
            }

            // La sección se resuelve por reflexión sobre las propiedades de AppSettings (que SON las secciones), no
            // con un mapa manual. Así la carga usa la misma lista que el guardado (JsonConvert.SerializeObject, ya
            // por reflexión): añadir una sección se restaura sola, sin tener que acordarse de un segundo sitio
            // (antes, olvidarlo dejaba esa sección sin restaurar en silencio).
            PropertyInfo? prop = typeof(AppSettings).GetProperty(entry.Key?.ToString() ?? string.Empty, BindingFlags.Public | BindingFlags.Instance);
            object? target = prop?.GetValue(this);
            if (target == null)
            {
                continue;
            }

            try
            {
                using var reader = section.CreateReader();
                Serializer.Populate(reader, target);
            }
            catch (Exception ex)
            {
                MM4LB.Services.ExceptionService.LogToFile(ex, $"Error binding settings for section '{entry.Key}'");
                continue;
            }
        }
    }
    #endregion
}