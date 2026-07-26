namespace MM4LB.Helpers;

/// <summary>
/// Constantes de las claves de localizaciÃ³n (i18n). Fuente Ãºnica para usarlas desde cÃ³digo sin strings sueltos; el
/// XAML usa las mismas cadenas literales vÃ­a <c>{loc:Str Key=...}</c> / <c>help:Help.Key</c>.
/// <see cref="MM4LB.Services.LocalizationValidator"/> comprueba en DEBUG que toda clave aquÃ­ exista en
/// <c>Strings/Resources.resx</c> (evita claves rotas).
///
/// ConvenciÃ³n: <c>{Scope}_{Element}_{Role}</c>, donde <b>Scope = el control/vista/servicio dueÃ±o</b> del texto
/// (nombre reconocible, sin el sufijo <c>Control</c>), y <c>Common_</c> para lo compartido/deduplicado. Sin puntos
/// (romperÃ­an el binding por indexador <c>[clave]</c>). Roles: Label, Tooltip, Header, Placeholder, Title,
/// Description, Empty, Format, Progress, Error. Se amplÃ­a por Ã¡reas (ver docs/Plan-Localizacion-Ayuda.md, F1â€“F4).
/// </summary>
public static class LocKeys
{
    // Common (compartido / deduplicado)

    // GameList



    // GameDetails

    // ImageGrid (GAME MEDIA GALLERY)

    // WebView (WEB SEARCH)
    public const string WebView_Init_Error = "WebView_Init_Error";

    // StatsGlobal (GLOBAL STATISTICS)

    // StatsPlatform (GAME STATISTICS)


    // Common (mÃ¡s): diÃ¡logos

    // DiÃ¡logos

    // MainWindow : tÃ­tulos de widgets
    public const string MainWindow_ActivityLogWidget_Title = "MainWindow_ActivityLogWidget_Title";
    public const string MainWindow_WebSearchWidget_Title = "MainWindow_WebSearchWidget_Title";
    public const string MainWindow_PriceChartWidget_Title = "MainWindow_PriceChartWidget_Title";

    // Common (mÃ¡s): botones de diÃ¡logo

    // DialogsService : tÃ­tulos de diÃ¡logos

    // Criterios de los dashboards

    // SearchStrings

    // MediaAuditService

    // ConsoleViewModel



    // ImageLoadingService (progreso/errores)

    // PlatformLoading / ImageMatching / ImageBinaryLoading / Progress / GameMetadata / LaunchBox
    public const string Progress_LazyLoading_Progress = "Progress_LazyLoading_Progress";
    public const string Progress_LazyLoaded_Progress = "Progress_LazyLoaded_Progress";

    // ViewModels (progreso/errores) + placeholder de regiÃ³n

    // Toolbar principal (tooltips)

    // ChartTypeSelector (toolbar de graficas)
    public const string ChartType_Bars_Label = "ChartType_Bars_Label";
    public const string ChartType_HBars_Label = "ChartType_HBars_Label";
    public const string ChartType_Line_Label = "ChartType_Line_Label";
    public const string ChartType_Area_Label = "ChartType_Area_Label";
    public const string ChartType_Pie_Label = "ChartType_Pie_Label";
    public const string ChartType_Ring_Label = "ChartType_Ring_Label";
    public const string ChartType_All_Label = "ChartType_All_Label";
    public const string ChartType_SortNone_Label = "ChartType_SortNone_Label";
    public const string ChartType_SortAsc_Label = "ChartType_SortAsc_Label";
    public const string ChartType_SortDesc_Label = "ChartType_SortDesc_Label";

    // ImageAudit (MEDIA AUDIT)

    // Console (ACTIVITY LOG)

    // FooterEventViewer

    // MainWindow

    // Common auditorÃ­a/tools (toolbar de vista, columnas de tabla, pills)

    // SettingsDialog (tÃ­tulos de secciÃ³n y opciones generadas en el VM del diÃ¡logo)

    // GeneralSettings (pestaÃ±a General de Settings)


    // ThemeSettings

    // AboutSettings

    // FooterHelp (botÃ³n de ayuda del footer)

    // Piloto (F0): validar idioma en caliente + toggle de ayuda. Se retirarÃ¡ al terminar la migraciÃ³n.

    // *_WidgetHelp_Description : descripciÃ³n del TeachingTip de ayuda de cada widget (icono de la cabecera).

    // F4 â€” tooltips para los botones con label visible (explicativos, van mÃ¡s allÃ¡ del label).
    // Panel de ajustes rÃ¡pidos (icono equalizer de la toolbar).
    public const string QuickSettings_Widgets_Header = "QuickSettings_Widgets_Header";
    public const string QuickSettings_CornerRadius_Label = "QuickSettings_CornerRadius_Label";
    public const string QuickSettings_Gap_Label = "QuickSettings_Gap_Label";
    public const string QuickSettings_PanelMargin_Label = "QuickSettings_PanelMargin_Label";

    // Product tracker
    public const string Footer_AddProduct_Tooltip = "Footer_AddProduct_Tooltip";
    public const string AddProduct_Dialog_Title = "AddProduct_Dialog_Title";
    public const string AddProduct_Dialog_Message = "AddProduct_Dialog_Message";
    public const string AddProduct_Url_Placeholder = "AddProduct_Url_Placeholder";
    public const string Common_Add_Label = "Common_Add_Label";
    public const string Common_Cancel_Label = "Common_Cancel_Label";
}
