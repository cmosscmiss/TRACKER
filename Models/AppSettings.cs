using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Tracker.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Tracker.Models;

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
    public LayoutSelectorControlSettings LayoutSelectorControl { get; set; } = new LayoutSelectorControlSettings();
    public ProductListControlSettings ProductListControl { get; set; } = new ProductListControlSettings();

    /// <summary>Config de la gráfica del widget de producto seleccionado (tipo/orden/Top N). Por defecto línea.</summary>
    public ChartConfig PriceChartControl { get; set; } = new ChartConfig { ChartType = ChartType.Line };

    /// <summary>Config de la gráfica del widget de resumen de precios (tipo/orden/Top N). Por defecto columnas.</summary>
    public ChartConfig ProductsOverviewControl { get; set; } = new ChartConfig { ChartType = ChartType.Column };

    /// <summary>Estado del widget de favoritos: página activa del FlipView y config de gráfica por producto.</summary>
    public FavoritesControlSettings FavoritesControl { get; set; } = new FavoritesControlSettings();

    public WebViewControlSettings WebViewControl { get; set; } = new WebViewControlSettings();
    public WindowSettings Window { get; set; } = new WindowSettings();
    public ThemeSettings Theme { get; set; } = new ThemeSettings();
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
        /// Activa el logging de excepciones a <c>Tracker.log</c> (herramienta de depuración). Por defecto activado.
        /// </summary>
        public bool ExceptionLoggingEnabled { get; set; } = true;

        /// <summary>
        /// Idioma de la interfaz (código ISO de dos letras, p. ej. "en", "es"). Por defecto inglés. Se aplica en
        /// caliente vía <see cref="Tracker.Services.LocalizationService"/>; ver docs/Plan-Localizacion-Ayuda.md.
        /// </summary>
        public string Language { get; set; } = "en";

        /// <summary>
        /// Última categoría abierta en la ventana de configuración (nombre del <c>SettingsSectionKind</c>, p. ej.
        /// "General", "Theme"). Se guarda al aceptar y se restaura al reabrir el diálogo. Se persiste como texto para no
        /// acoplar el modelo a un enum de la capa de ViewModels; si el valor no existe, se cae a la primera categoría.
        /// </summary>
        public string SettingsLastSection { get; set; } = "General";

        /// <summary>
        /// Indica si los widgets del panel (modo <see cref="Tracker.Controls.Views.WidgetDisplayMode.Default"/>) muestran
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
        /// Si es true (por defecto), se muestran los tooltips de los botones. Un toggle del footer lo alterna en
        /// caliente (vía <see cref="Tracker.Services.SharedDataService.HelpTooltipsEnabled"/> y la attached property
        /// <c>Help.Key</c>).
        /// </summary>
        public bool HelpTooltipsEnabled { get; set; } = true;

        /// <summary>
        /// Si es true (por defecto), el botón de cerrar de la ventana NO cierra la aplicación: la esconde en la
        /// bandeja del sistema (y minimizar también la esconde), de forma que el planificador sigue leyendo precios
        /// en segundo plano. Se sale de la aplicación desde el menú del icono de la bandeja (botón derecho -> Exit).
        /// Si es false, la app se comporta como una ventana normal: cerrar termina el proceso y minimizar la deja en
        /// la barra de tareas. Se lee EN VIVO (no hace falta reiniciar).
        /// </summary>
        public bool MinimizeToTray { get; set; } = true;

        /// <summary>
        /// Si es true (por defecto), la aplicación se registra para arrancar automáticamente al iniciar sesión en
        /// Windows (clave <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>, ver
        /// <see cref="Tracker.Services.StartupService"/>). El registro se sincroniza con este valor al arrancar y al
        /// aceptar la ventana de configuración.
        /// </summary>
        public bool StartWithWindows { get; set; } = true;

        /// <summary>
        /// Si es true, el precio de los productos incluye los gastos de envío (precio + envío) en toda la app. Lo
        /// alterna un toggle del footer (vía <see cref="Tracker.Services.SharedDataService.IncludeShippingInPrice"/>).
        /// Por defecto false.
        /// </summary>
        public bool IncludeShippingInPrice { get; set; } = false;

        /// <summary>Tasa de cambio por defecto: dólares por euro (1 € = 1,08 $).</summary>
        public const double DefaultDollarsPerEuro = 1.08;

        /// <summary>Tasa de cambio por defecto: yenes por euro (1 € = 170 ¥).</summary>
        public const double DefaultYensPerEuro = 170.0;

        /// <summary>
        /// Tasa de cambio FIJA para amazon.com: cuántos dólares vale un euro (1 € = X $). Los precios en dólares se
        /// dividen por ella para compararlos con los del resto de tiendas (ver <see cref="Tracker.Helpers.Money"/>).
        /// Configurable en la ventana de ajustes; no se consulta ningún servicio de cambio.
        /// </summary>
        public double DollarsPerEuro { get; set; } = DefaultDollarsPerEuro;

        /// <summary>
        /// Tasa de cambio FIJA para amazon.co.jp: cuántos yenes vale un euro (1 € = Y ¥). Ver
        /// <see cref="DollarsPerEuro"/>.
        /// </summary>
        public double YensPerEuro { get; set; } = DefaultYensPerEuro;

        /// <summary>
        /// Cada cuántas horas se refrescan automáticamente todos los precios (planificador). Por defecto 24. Se aplica
        /// un mínimo de 1 h. Configurable desde la ventana de ajustes.
        /// </summary>
        public int AutoRefreshHours { get; set; } = 24;

        /// <summary>Si es true, los productos comprados se muestran en la lista (con el título tachado). Lo alterna un toggle del footer. Por defecto false.</summary>
        public bool ShowPurchased { get; set; } = false;

        /// <summary>Si es true (por defecto), se aplican los colores personalizados (<see cref="CustomColors"/>) sobre el tema. Si es false, se usa el tema puro.</summary>
        public bool UseCustomColors { get; set; } = true;

        /// <summary>Overrides de color del tema (nombre base -> hex #RRGGBB) editados en el diálogo de colores. Se aplican al arrancar si <see cref="UseCustomColors"/>. Vacío = tema puro.</summary>
        public Dictionary<string, string> CustomColors { get; set; } = new();

        /// <summary>
        /// Si es true, las gráficas del widget de productos (producto seleccionado y favoritos) muestran las etiquetas
        /// del eje X (las fechas de actualización). Por defecto false (leyenda oculta). Lo controla un toggle del pie y
        /// se aplica en caliente a todas esas gráficas vía <see cref="Tracker.Services.SharedDataService.ShowChartAxisLabels"/>.
        /// </summary>
        public bool ShowChartAxisLabels { get; set; } = false;

        /// <summary>
        /// Si es true, la vista de producto (seleccionado y favoritos) muestra la gráfica de evolución del precio MÍNIMO
        /// (área) en vez de la de precios por tienda. Ajuste global; lo controla un toggle del pie y se aplica en caliente
        /// a todas las vistas vía <see cref="Tracker.Services.SharedDataService.ShowMinPriceChart"/>. Por defecto false.
        /// </summary>
        public bool ShowMinPriceChart { get; set; } = false;

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
        /// texto (ver <see cref="Tracker.Controls.Views.WidgetStatCardControl"/>.IconCollapsePillCount). Hay un valor
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


    public class ProductListControlSettings
    {
        /// <summary>Id (clave en BD) del producto seleccionado al cerrar, para re-seleccionarlo al arrancar (0 = ninguno).</summary>
        public long SelectedProductId { get; set; }
    }

    public class WebViewControlSettings
    {
        /// <summary>Marketplace de Amazon seleccionado en el navegador (código: es/de/fr/be/nl).</summary>
        public string Country { get; set; } = "es";
    }

    /// <summary>
    /// Ajustes persistentes de una gráfica de <c>ChartTypeSelectorControl</c>: tipo de gráfica, orden de los
    /// elementos y Top N (0 = todos).
    /// </summary>
    public class ChartConfig
    {
        private ChartType _chartType = ChartType.Column;

        /// <summary>
        /// Tipo de gráfica. Tarta y anillo ya no se ofrecen en el selector (no dicen nada sobre precios), así que un
        /// valor de esos guardado por una versión anterior se sanea a columnas AL LEER el .ini: el chart nunca llega a
        /// pedir un tipo que ya no se dibuja, y al guardar tampoco se reescribe.
        /// </summary>
        public ChartType ChartType
        {
            get => _chartType;
            set => _chartType = value is ChartType.Pie or ChartType.Doughnut ? ChartType.Column : value;
        }
        public SortMode SortOrder { get; set; } = SortMode.None;
        public int TopN { get; set; }
    }

    /// <summary>
    /// Estado persistente del widget de favoritos: la página activa del FlipView y la configuración de la gráfica
    /// (tipo/orden/Top N) de cada producto favorito, indexada por su Id (clave en BD) para que sobreviva a reordenar
    /// o cambiar el conjunto de favoritos.
    /// </summary>
    public class FavoritesControlSettings
    {
        /// <summary>Índice de la página (favorito) visible en el FlipView.</summary>
        public int SelectedIndex { get; set; }

        /// <summary>Config de gráfica por producto favorito, indexada por <see cref="Product.Id"/>.</summary>
        public Dictionary<long, ChartConfig> Charts { get; set; } = new();
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
                SuccessColor = "#2E7D32",
                WarningColor = "#fff000",
                ExtraColor1 = "#546E7A",
                ExtraColor2 = "#2F6FED",
                ExtraColor3 = "#CC5500",
                ExtraColor4 = "#00A8E1",
                BackgroundImage = "Backgrounds/TRACKER-SC-CYBER-CITY-NO-FRAME.png",
                LogoImage = "TRACKER-LOGO-CYBER-CITY.png",
                OverlayImage = "Backgrounds/TRACKER-SC-CYBER-CITY-OVERLAY.jpg",
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
                SuccessColor = "#2E7D32",
                WarningColor = "#fff000",
                ExtraColor1 = "#546E7A",
                ExtraColor2 = "#2F6FED",
                ExtraColor3 = "#CC5500",
                ExtraColor4 = "#00A8E1",
                BackgroundImage = "Backgrounds/TRACKER-SC-DEAD-SPACE-NO-FRAME.png",
                LogoImage = "TRACKER-LOGO-DEAD-SPACE.png",
                OverlayImage = "Backgrounds/TRACKER-SC-DEAD-SPACE-OVERLAY.jpg",
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
                SuccessColor = "#2E7D32",
                WarningColor = "#fff000",
                ExtraColor1 = "#546E7A",
                ExtraColor2 = "#2F6FED",
                ExtraColor3 = "#CC5500",
                ExtraColor4 = "#00A8E1",
                BackgroundImage = "Backgrounds/TRACKER-SC-DOOM-NO-FRAME.png",
                LogoImage = "TRACKER-LOGO-DOOM.png",
                OverlayImage = "Backgrounds/TRACKER-SC-DOOM-OVERLAY.jpg",
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
                SuccessColor = "#2E7D32",
                WarningColor = "#fff000",
                ExtraColor1 = "#546E7A",
                ExtraColor2 = "#2F6FED",
                ExtraColor3 = "#CC5500",
                ExtraColor4 = "#00A8E1",
                BackgroundImage = "Backgrounds/TRACKER-SC-LOL-NO-FRAME.png",
                LogoImage = "TRACKER-LOGO-LOL.png",
                OverlayImage = "Backgrounds/TRACKER-SC-LOL-OVERLAY.jpg",
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

        // Colores GENÉRICOS extra (sin semántica): se usan para pills y elementos que antes tenían colores fijos.
        public string ExtraColor1 { get; set; } = string.Empty;
        public string ExtraColor2 { get; set; } = string.Empty;
        public string ExtraColor3 { get; set; } = string.Empty;
        public string ExtraColor4 { get; set; } = string.Empty;

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
                Tracker.Services.ExceptionService.LogToFile(ex, $"Error binding settings for section '{entry.Key}'");
                continue;
            }
        }
    }
    #endregion
}