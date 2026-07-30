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
    public const string MainWindow_ProductsOverviewWidget_Title = "MainWindow_ProductsOverviewWidget_Title";
    public const string MainWindow_FavoritesWidget_Title = "MainWindow_FavoritesWidget_Title";
    public const string PriceChart_Favorite_Tooltip = "PriceChart_Favorite_Tooltip";

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
    public const string Common_Save_Label = "Common_Save_Label";
    public const string Common_OK_Label = "Common_OK_Label";
    public const string Common_Apply_Label = "Common_Apply_Label";
    public const string Common_AppName = "Common_AppName";

    // Settings (ventana de configuración: General / Theme / About)
    public const string Toolbar_Settings_Tooltip = "Toolbar_Settings_Tooltip";
    public const string DialogsService_Settings_Title = "DialogsService_Settings_Title";
    public const string DialogsSettingsControl_Placeholder_Text = "DialogsSettingsControl_Placeholder_Text";
    public const string SettingsDialog_General_Title = "SettingsDialog_General_Title";
    public const string SettingsDialog_Theme_Title = "SettingsDialog_Theme_Title";
    public const string SettingsDialog_About_Title = "SettingsDialog_About_Title";
    public const string SettingsDialog_ToolbarGroupsSeparate_Label = "SettingsDialog_ToolbarGroupsSeparate_Label";
    public const string SettingsDialog_ToolbarGroupsGrouped_Label = "SettingsDialog_ToolbarGroupsGrouped_Label";
    public const string SettingsDialog_ToolbarGroupsAuto_Label = "SettingsDialog_ToolbarGroupsAuto_Label";
    public const string GeneralSettings_Language_Label = "GeneralSettings_Language_Label";
    public const string GeneralSettings_ToolbarGroups_Label = "GeneralSettings_ToolbarGroups_Label";
    public const string GeneralSettings_ShowWidgetHeader_Label = "GeneralSettings_ShowWidgetHeader_Label";
    public const string GeneralSettings_FooterEventViewer_Label = "GeneralSettings_FooterEventViewer_Label";
    public const string GeneralSettings_LogExceptions_Label = "GeneralSettings_LogExceptions_Label";
    public const string ThemeSettings_Theme_Label = "ThemeSettings_Theme_Label";
    public const string ThemeSettings_RandomTheme_Label = "ThemeSettings_RandomTheme_Label";
    public const string ThemeSettings_BackgroundOverlay_Header = "ThemeSettings_BackgroundOverlay_Header";
    public const string ThemeSettings_TintOpacity_Label = "ThemeSettings_TintOpacity_Label";
    public const string ThemeSettings_TintSaturation_Label = "ThemeSettings_TintSaturation_Label";
    public const string ThemeSettings_TintBrightness_Label = "ThemeSettings_TintBrightness_Label";
    public const string ThemeSettings_OverlayBlur_Label = "ThemeSettings_OverlayBlur_Label";
    public const string ThemeSettings_OverlayOpacity_Label = "ThemeSettings_OverlayOpacity_Label";
    public const string AboutSettings_Tagline_Text = "AboutSettings_Tagline_Text";
    public const string AboutSettings_Description_Text = "AboutSettings_Description_Text";
    public const string AboutSettings_Details_Header = "AboutSettings_Details_Header";
    public const string AboutSettings_Build_Label = "AboutSettings_Build_Label";
    public const string AboutSettings_Runtime_Label = "AboutSettings_Runtime_Label";
    public const string AboutSettings_Architecture_Label = "AboutSettings_Architecture_Label";
    public const string AboutSettings_ThirdParty_Header = "AboutSettings_ThirdParty_Header";
    public const string AboutSettings_AccentNote_Text = "AboutSettings_AccentNote_Text";
    public const string AboutSettings_Copyright_Text = "AboutSettings_Copyright_Text";

    // Amazon login/logout
    public const string AmazonLogin_Tooltip = "AmazonLogin_Tooltip";
    public const string AmazonLogout_Tooltip = "AmazonLogout_Tooltip";
    public const string AmazonLogin_Title = "AmazonLogin_Title";
    public const string AmazonLogin_Message = "AmazonLogin_Message";
    public const string AmazonLogin_Email_Label = "AmazonLogin_Email_Label";
    public const string AmazonLogin_Email_Placeholder = "AmazonLogin_Email_Placeholder";
    public const string AmazonLogin_Password_Label = "AmazonLogin_Password_Label";
    public const string AmazonLogin_Note = "AmazonLogin_Note";
    public const string AmazonLogin_SignIn_Label = "AmazonLogin_SignIn_Label";
    public const string AmazonLogin_NoBrowser_Title = "AmazonLogin_NoBrowser_Title";
    public const string AmazonLogin_NoBrowser_Message = "AmazonLogin_NoBrowser_Message";
    public const string AmazonLogout_ConfirmTitle = "AmazonLogout_ConfirmTitle";
    public const string AmazonLogout_ConfirmMessage = "AmazonLogout_ConfirmMessage";
    public const string AmazonLogout_Confirm_Label = "AmazonLogout_Confirm_Label";
    public const string AmazonLogin_Progress_SigningIn = "AmazonLogin_Progress_SigningIn";
    public const string AmazonLogin_Progress_Done = "AmazonLogin_Progress_Done";
    public const string AmazonLogout_Progress_SigningOut = "AmazonLogout_Progress_SigningOut";
    public const string AmazonLogout_Progress_Done = "AmazonLogout_Progress_Done";

    // Templates (guardar/seleccionar)
    public const string Toolbar_Templates_Tooltip = "Toolbar_Templates_Tooltip";
    public const string Toolbar_SaveTemplate_Tooltip = "Toolbar_SaveTemplate_Tooltip";
    public const string DialogsService_SaveTemplate_Title = "DialogsService_SaveTemplate_Title";
    public const string TemplateNameDialog_ChooseSlot_Text = "TemplateNameDialog_ChooseSlot_Text";
    public const string TemplateNameDialog_Name_Label = "TemplateNameDialog_Name_Label";
    public const string TemplateNameDialog_Name_Placeholder = "TemplateNameDialog_Name_Placeholder";
    public const string TemplateSlots_BuiltIn_Tooltip = "TemplateSlots_BuiltIn_Tooltip";
    public const string WebView_Country_Tooltip = "WebView_Country_Tooltip";
    public const string WebView_AddProduct_Tooltip = "WebView_AddProduct_Tooltip";
    public const string WebView_PickPrice_Tooltip = "WebView_PickPrice_Tooltip";
    public const string WebView_AddAltLink_Tooltip = "WebView_AddAltLink_Tooltip";
    public const string PriceChart_CurrentPrice_Label = "PriceChart_CurrentPrice_Label";
    public const string PriceChart_LowestPrice_Label = "PriceChart_LowestPrice_Label";

    // Product activity log
    public const string ProductLog_Added_Progress = "ProductLog_Added_Progress";
    public const string ProductLog_AddedAndRead_Progress = "ProductLog_AddedAndRead_Progress";
    public const string ProductLog_AltLinkAdded_Progress = "ProductLog_AltLinkAdded_Progress";
    public const string ProductLog_Refreshing_Progress = "ProductLog_Refreshing_Progress";
    public const string ProductLog_ReadingStore_Progress = "ProductLog_ReadingStore_Progress";
    public const string ProductLog_Refreshed_Progress = "ProductLog_Refreshed_Progress";
    public const string ProductLog_RefreshingAll_Progress = "ProductLog_RefreshingAll_Progress";
    public const string ProductLog_RefreshedAll_Progress = "ProductLog_RefreshedAll_Progress";
    public const string AppLog_Loaded_Progress = "AppLog_Loaded_Progress";
    public const string PriceChart_Prime_Label = "PriceChart_Prime_Label";
    public const string PriceChart_NoPrime_Label = "PriceChart_NoPrime_Label";
    public const string PriceChart_Promo_Label = "PriceChart_Promo_Label";
    public const string Footer_RefreshAll_Tooltip = "Footer_RefreshAll_Tooltip";
    public const string Footer_AxisLabels_Tooltip = "Footer_AxisLabels_Tooltip";

    // Acciones del widget de producto
    public const string PriceChart_Delete_Tooltip = "PriceChart_Delete_Tooltip";
    public const string PriceChart_Purchased_Tooltip = "PriceChart_Purchased_Tooltip";
    public const string PriceChart_Refresh_Tooltip = "PriceChart_Refresh_Tooltip";
    public const string PriceChart_Edit_Tooltip = "PriceChart_Edit_Tooltip";
    public const string PriceChart_Search_Tooltip = "PriceChart_Search_Tooltip";
    public const string PriceChart_EditDialog_Title = "PriceChart_EditDialog_Title";
    public const string PriceChart_EditDialog_Message = "PriceChart_EditDialog_Message";
    public const string PriceChart_EditName_Placeholder = "PriceChart_EditName_Placeholder";
    public const string PriceChart_Issues_Label = "PriceChart_Issues_Label";
    public const string Product_Issues_Tooltip = "Product_Issues_Tooltip";
    public const string PriceChart_Preorder_Label = "PriceChart_Preorder_Label";
    public const string Product_Preorder_Tooltip = "Product_Preorder_Tooltip";
    public const string ProductList_Filter_Placeholder = "ProductList_Filter_Placeholder";
    public const string ProductList_Filters_Tooltip = "ProductList_Filters_Tooltip";
    public const string ProductList_FilterFavorites_Label = "ProductList_FilterFavorites_Label";
    public const string ProductList_FilterIssues_Label = "ProductList_FilterIssues_Label";
    public const string ProductList_FilterPriceChange_Label = "ProductList_FilterPriceChange_Label";
    public const string ProductList_FilterAlert_Label = "ProductList_FilterAlert_Label";
    public const string ProductList_Count_Format = "ProductList_Count_Format";
    public const string PriceChart_Alert_Tooltip = "PriceChart_Alert_Tooltip";
    public const string PriceChart_BelowAlert_Tooltip = "PriceChart_BelowAlert_Tooltip";
    public const string PriceChart_AlertDialog_Title = "PriceChart_AlertDialog_Title";
    public const string PriceChart_AlertDialog_Message = "PriceChart_AlertDialog_Message";
    public const string PriceChart_AlertPrice_Placeholder = "PriceChart_AlertPrice_Placeholder";
    public const string PriceChart_DeleteDialog_Title = "PriceChart_DeleteDialog_Title";
    public const string PriceChart_DeleteDialog_Message = "PriceChart_DeleteDialog_Message";
    public const string PriceChart_PurchasedDialog_Title = "PriceChart_PurchasedDialog_Title";
    public const string PriceChart_PurchasedDialog_Message = "PriceChart_PurchasedDialog_Message";
    public const string PriceChart_PurchasePrice_Placeholder = "PriceChart_PurchasePrice_Placeholder";
    public const string PriceChart_Purchased_Confirm_Label = "PriceChart_Purchased_Confirm_Label";
    public const string Common_Delete_Label = "Common_Delete_Label";
    public const string ProductLog_Purchased_Progress = "ProductLog_Purchased_Progress";
    public const string ProductLog_Removed_Progress = "ProductLog_Removed_Progress";
}
