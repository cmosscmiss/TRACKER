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
    public const string Common_AppName = "Common_AppName";
    public const string Common_Cancel_Tooltip = "Common_Cancel_Tooltip";
    public const string Common_Undo_Tooltip = "Common_Undo_Tooltip";
    public const string Common_ShowingXofY_Format = "Common_ShowingXofY_Format";
    public const string Common_Help_Tooltip = "Common_Help_Tooltip";
    public const string Common_ListView_Label = "Common_ListView_Label";
    public const string Common_Delete_Label = "Common_Delete_Label";
    public const string Common_Delete_Tooltip = "Common_Delete_Tooltip";
    public const string Common_Dimensions_Label = "Common_Dimensions_Label";
    public const string Common_FileSize_Label = "Common_FileSize_Label";
    public const string Common_Extension_Label = "Common_Extension_Label";
    public const string Common_Quality_Label = "Common_Quality_Label";
    public const string Common_Duration_Label = "Common_Duration_Label";
    public const string Common_GamePlatform_Description = "Common_GamePlatform_Description";

    // GameList



    // GameDetails

    // ImageGrid (GAME MEDIA GALLERY)

    // WebView (WEB SEARCH)
    public const string WebView_UsingGoogle_Tooltip = "WebView_UsingGoogle_Tooltip";
    public const string WebView_UsingSteamDb_Tooltip = "WebView_UsingSteamDb_Tooltip";
    public const string WebView_YouTube_Tooltip = "WebView_YouTube_Tooltip";
    public const string WebView_Help_Title = "WebView_Help_Title";
    public const string WebView_Help_Description = "WebView_Help_Description";
    public const string WebView_Back_Tooltip = "WebView_Back_Tooltip";
    public const string WebView_Forward_Tooltip = "WebView_Forward_Tooltip";
    public const string WebView_AddToGameImages_MenuItem = "WebView_AddToGameImages_MenuItem";
    public const string WebView_AddToGameVideos_MenuItem = "WebView_AddToGameVideos_MenuItem";
    public const string WebView_Init_Error = "WebView_Init_Error";

    // StatsGlobal (GLOBAL STATISTICS)

    // StatsPlatform (GAME STATISTICS)


    // Common (mÃ¡s): diÃ¡logos
    public const string Common_Discard_Label = "Common_Discard_Label";
    public const string Common_Keep_Label = "Common_Keep_Label";
    public const string Common_Close_Label = "Common_Close_Label";
    public const string Common_OK_Label = "Common_OK_Label";
    public const string Common_NoRegion_Label = "Common_NoRegion_Label";

    // DiÃ¡logos
    public const string DeleteConfirmDialog_AskBefore_Label = "DeleteConfirmDialog_AskBefore_Label";
    public const string PlatformImageDropDialog_TypeLabel = "PlatformImageDropDialog_TypeLabel";
    public const string PlatformImageDropDialog_SelectType_Placeholder = "PlatformImageDropDialog_SelectType_Placeholder";
    public const string PlatformImageDropDialog_ExistingImages_Label = "PlatformImageDropDialog_ExistingImages_Label";

    // MainWindow : tÃ­tulos de widgets
    public const string MainWindow_ActivityLogWidget_Title = "MainWindow_ActivityLogWidget_Title";
    public const string MainWindow_WebSearchWidget_Title = "MainWindow_WebSearchWidget_Title";

    // Common (mÃ¡s): botones de diÃ¡logo
    public const string Common_Cancel_Label = "Common_Cancel_Label";
    public const string Common_Import_Label = "Common_Import_Label";
    public const string Common_Add_Label = "Common_Add_Label";
    public const string Common_Save_Label = "Common_Save_Label";
    public const string Common_Apply_Label = "Common_Apply_Label";
    public const string Common_Empty_Label = "Common_Empty_Label";

    // DialogsService : tÃ­tulos de diÃ¡logos
    public const string DialogsService_DeleteMedia_Title = "DialogsService_DeleteMedia_Title";
    public const string DialogsService_AddPlatformImage_Title = "DialogsService_AddPlatformImage_Title";

    // Criterios de los dashboards
    public const string DashboardCriteria_First_Label = "DashboardCriteria_First_Label";
    public const string DashboardCriteria_Second_Label = "DashboardCriteria_Second_Label";
    public const string DashboardCriteria_Region_Label = "DashboardCriteria_Region_Label";
    public const string DashboardCriteria_Suffix_Label = "DashboardCriteria_Suffix_Label";
    public const string DashboardCriteria_FileName_Label = "DashboardCriteria_FileName_Label";
    public const string Common_OtherRegions_Label = "Common_OtherRegions_Label";

    // SearchStrings
    public const string SearchStrings_DefaultTitle = "SearchStrings_DefaultTitle";
    public const string SearchStrings_Empty = "SearchStrings_Empty";
    public const string SearchStrings_GameTitle = "SearchStrings_GameTitle";
    public const string SearchStrings_GameImageTitle = "SearchStrings_GameImageTitle";

    // MediaAuditService

    // ConsoleViewModel

    // YoutubeDownloadService
    public const string Youtube_NoVideoStream_Error = "Youtube_NoVideoStream_Error";
    public const string Youtube_NoAudioTrack_Error = "Youtube_NoAudioTrack_Error";
    public const string Youtube_FfmpegZipMissing_Error = "Youtube_FfmpegZipMissing_Error";
    public const string Youtube_FfmpegNotExecutable_Error = "Youtube_FfmpegNotExecutable_Error";
    public const string Youtube_FfmpegInvalid_Error = "Youtube_FfmpegInvalid_Error";
    public const string Youtube_FfmpegNotFound_Error = "Youtube_FfmpegNotFound_Error";
    public const string Youtube_FfmpegProcessFailed_Error = "Youtube_FfmpegProcessFailed_Error";

    // StatisticsService : pills
    public const string Common_PlatformAll_Description = "Common_PlatformAll_Description";
    public const string Stats_MatchingTotal_Description = "Stats_MatchingTotal_Description";
    public const string Stats_MatchedTotal_Description = "Stats_MatchedTotal_Description";
    public const string Stats_InMyCollection_Title = "Stats_InMyCollection_Title";
    public const string Stats_InLaunchBox_Title = "Stats_InLaunchBox_Title";
    public const string Stats_NotInLaunchBox_Title = "Stats_NotInLaunchBox_Title";
    public const string Stats_WithRegion_Title = "Stats_WithRegion_Title";
    public const string Stats_MatchedImages_Title = "Stats_MatchedImages_Title";
    public const string Stats_GamesWithMatch_Title = "Stats_GamesWithMatch_Title";
    public const string Stats_Images_Title = "Stats_Images_Title";
    public const string Stats_ImageTypes_Title = "Stats_ImageTypes_Title";
    public const string Stats_Size_Title = "Stats_Size_Title";
    public const string Stats_Games_Title = "Stats_Games_Title";
    public const string Stats_MediaSet_Title = "Stats_MediaSet_Title";
    public const string Stats_MediaTypes_Title = "Stats_MediaTypes_Title";
    public const string Stats_MediaSetSize_Title = "Stats_MediaSetSize_Title";
    public const string Stats_EmptyCollection_Title = "Stats_EmptyCollection_Title";
    public const string Stats_AsRegion_Format = "Stats_AsRegion_Format";
    public const string Stats_AsExtension_Format = "Stats_AsExtension_Format";
    public const string Stats_AsQuality_Format = "Stats_AsQuality_Format";
    public const string Stats_AsDuration_Format = "Stats_AsDuration_Format";
    public const string Stats_AsDimensions_Format = "Stats_AsDimensions_Format";

    // ImageLoadingService (progreso/errores)
    public const string ImageLoading_ReplacingVideo_Progress = "ImageLoading_ReplacingVideo_Progress";
    public const string ImageLoading_VideoReplaced_Progress = "ImageLoading_VideoReplaced_Progress";
    public const string ImageLoading_ReplaceVideoFailed_Error = "ImageLoading_ReplaceVideoFailed_Error";
    public const string ImageLoading_ImportingMediaFile_Progress = "ImageLoading_ImportingMediaFile_Progress";
    public const string ImageLoading_MediaFileImported_Progress = "ImageLoading_MediaFileImported_Progress";
    public const string ImageLoading_ImportMediaFileFailed_Error = "ImageLoading_ImportMediaFileFailed_Error";
    public const string ImageLoading_UrlAndFolderRequired_Error = "ImageLoading_UrlAndFolderRequired_Error";
    public const string ImageLoading_DownloadingMedia_Progress = "ImageLoading_DownloadingMedia_Progress";
    public const string ImageLoading_DownloadingMediaSized_Progress = "ImageLoading_DownloadingMediaSized_Progress";
    public const string ImageLoading_MediaDownloaded_Progress = "ImageLoading_MediaDownloaded_Progress";
    public const string ImageLoading_MediaDownloadCancelled_Progress = "ImageLoading_MediaDownloadCancelled_Progress";
    public const string ImageLoading_MediaDownloadFailed_Error = "ImageLoading_MediaDownloadFailed_Error";
    public const string ImageLoading_SourceAndFolderRequired_Error = "ImageLoading_SourceAndFolderRequired_Error";
    public const string ImageLoading_SourceFileNotFound_Error = "ImageLoading_SourceFileNotFound_Error";
    public const string ImageLoading_ImportingMedia_Progress = "ImageLoading_ImportingMedia_Progress";
    public const string ImageLoading_MediaImported_Progress = "ImageLoading_MediaImported_Progress";
    public const string ImageLoading_MediaImportFailed_Error = "ImageLoading_MediaImportFailed_Error";
    public const string ImageLoading_DownloadingYoutube_Progress = "ImageLoading_DownloadingYoutube_Progress";
    public const string ImageLoading_DownloadingYoutubeSized_Progress = "ImageLoading_DownloadingYoutubeSized_Progress";
    public const string ImageLoading_YoutubeDownloaded_Progress = "ImageLoading_YoutubeDownloaded_Progress";
    public const string ImageLoading_YoutubeDownloadCancelled_Progress = "ImageLoading_YoutubeDownloadCancelled_Progress";
    public const string ImageLoading_MediaDeleted_Progress = "ImageLoading_MediaDeleted_Progress";
    public const string ImageLoading_ProcessingGame_Progress = "ImageLoading_ProcessingGame_Progress";
    public const string ImageLoading_GameProcessed_Progress = "ImageLoading_GameProcessed_Progress";
    public const string ImageLoading_ProcessGameFailed_Error = "ImageLoading_ProcessGameFailed_Error";
    public const string ImageLoading_ProcessingRegions_Progress = "ImageLoading_ProcessingRegions_Progress";
    public const string ImageLoading_RegionsProcessed_Progress = "ImageLoading_RegionsProcessed_Progress";
    public const string ImageLoading_ProcessRegionsFailed_Error = "ImageLoading_ProcessRegionsFailed_Error";
    public const string ImageLoading_ImportingMediaFiles_Progress = "ImageLoading_ImportingMediaFiles_Progress";
    public const string ImageLoading_ImagesImported_Progress = "ImageLoading_ImagesImported_Progress";
    public const string ImageLoading_ImagesImportedPartial_Error = "ImageLoading_ImagesImportedPartial_Error";
    public const string ImageLoading_FolderNotFound_Error = "ImageLoading_FolderNotFound_Error";
    public const string ImageLoading_ScanningFolder_Progress = "ImageLoading_ScanningFolder_Progress";
    public const string ImageLoading_LoadingMedia_Progress = "ImageLoading_LoadingMedia_Progress";
    public const string ImageLoading_MediaLoaded_Progress = "ImageLoading_MediaLoaded_Progress";
    public const string ImageLoading_DataUriNoPayload_Error = "ImageLoading_DataUriNoPayload_Error";

    // PlatformLoading / ImageMatching / ImageBinaryLoading / Progress / GameMetadata / LaunchBox
    public const string PlatformLoading_LoadingPlatform_Progress = "PlatformLoading_LoadingPlatform_Progress";
    public const string PlatformLoading_ProcessingPlatformsXml_Progress = "PlatformLoading_ProcessingPlatformsXml_Progress";
    public const string PlatformLoading_LoadingPlatforms_Progress = "PlatformLoading_LoadingPlatforms_Progress";
    public const string PlatformLoading_PreparingUi_Progress = "PlatformLoading_PreparingUi_Progress";
    public const string PlatformLoading_LoadingGamesDb_Progress = "PlatformLoading_LoadingGamesDb_Progress";
    public const string PlatformLoading_LoadGamesDbError_Error = "PlatformLoading_LoadGamesDbError_Error";
    public const string PlatformLoading_GamesDbLoaded_Progress = "PlatformLoading_GamesDbLoaded_Progress";
    public const string ImageMatching_LoadingMedia_Progress = "ImageMatching_LoadingMedia_Progress";
    public const string ImageMatching_MediaLoaded_Progress = "ImageMatching_MediaLoaded_Progress";
    public const string ImageBinaryLoading_HighResProgress_Progress = "ImageBinaryLoading_HighResProgress_Progress";
    public const string ImageBinaryLoading_HighResCompleted_Progress = "ImageBinaryLoading_HighResCompleted_Progress";
    public const string Progress_LazyLoading_Progress = "Progress_LazyLoading_Progress";
    public const string Progress_LazyLoaded_Progress = "Progress_LazyLoaded_Progress";
    public const string GameMetadata_ReadError_Error = "GameMetadata_ReadError_Error";
    public const string LaunchBox_NoPlatforms_Error = "LaunchBox_NoPlatforms_Error";

    // ViewModels (progreso/errores) + placeholder de regiÃ³n
    public const string Common_NoneRegion_Placeholder = "Common_NoneRegion_Placeholder";
    public const string ImageGridGame_RefreshGallery_Error = "ImageGridGame_RefreshGallery_Error";
    public const string ImageGridGame_SelectionChange_Error = "ImageGridGame_SelectionChange_Error";
    public const string MainWindow_FfmpegPreparing_Progress = "MainWindow_FfmpegPreparing_Progress";
    public const string MainWindow_FfmpegReady_Progress = "MainWindow_FfmpegReady_Progress";
    public const string MainWindow_FfmpegCancelled_Progress = "MainWindow_FfmpegCancelled_Progress";
    public const string MainWindow_FfmpegPrepare_Error = "MainWindow_FfmpegPrepare_Error";

    // Toolbar principal (tooltips)
    public const string Toolbar_Layout_Tooltip = "Toolbar_Layout_Tooltip";
    public const string Toolbar_Widgets_Tooltip = "Toolbar_Widgets_Tooltip";
    public const string Toolbar_QuickSettings_Tooltip = "Toolbar_QuickSettings_Tooltip";
    public const string Toolbar_Resize_Tooltip = "Toolbar_Resize_Tooltip";

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
    public const string ChartType_TypeButton_Tooltip = "ChartType_TypeButton_Tooltip";
    public const string ChartType_TopNButton_Tooltip = "ChartType_TopNButton_Tooltip";
    public const string ChartType_SortButton_Tooltip = "ChartType_SortButton_Tooltip";

    // ImageAudit (MEDIA AUDIT)

    // Console (ACTIVITY LOG)

    // FooterEventViewer
    public const string FooterEventViewer_OlderEvent_Tooltip = "FooterEventViewer_OlderEvent_Tooltip";
    public const string FooterEventViewer_NewerEvent_Tooltip = "FooterEventViewer_NewerEvent_Tooltip";
    public const string FooterEventViewer_LatestEvent_Tooltip = "FooterEventViewer_LatestEvent_Tooltip";

    // MainWindow
    public const string MainWindow_Platform_Label = "MainWindow_Platform_Label";

    // Common auditorÃ­a/tools (toolbar de vista, columnas de tabla, pills)
    public const string Common_View_Header = "Common_View_Header";
    public const string Common_TableView_Label = "Common_TableView_Label";
    public const string Common_GridView_Label = "Common_GridView_Label";
    public const string Common_SelectedType_Label = "Common_SelectedType_Label";
    public const string Common_Type_Header = "Common_Type_Header";
    public const string Common_File_Header = "Common_File_Header";
    public const string Common_Region_Header = "Common_Region_Header";
    public const string Common_Game_Header = "Common_Game_Header";
    public const string Common_Title_Header = "Common_Title_Header";
    public const string Common_Size_Label = "Common_Size_Label";
    public const string Common_Types_Label = "Common_Types_Label";

    // SettingsDialog (tÃ­tulos de secciÃ³n y opciones generadas en el VM del diÃ¡logo)

    // GeneralSettings (pestaÃ±a General de Settings)

    // RegionsSettings / MediaTypesSettings

    // ThemeSettings

    // AboutSettings

    // FooterHelp (botÃ³n de ayuda del footer)
    public const string FooterHelp_Toggle_Tooltip = "FooterHelp_Toggle_Tooltip";

    // Piloto (F0): validar idioma en caliente + toggle de ayuda. Se retirarÃ¡ al terminar la migraciÃ³n.

    // *_WidgetHelp_Description : descripciÃ³n del TeachingTip de ayuda de cada widget (icono de la cabecera).
    public const string Console_WidgetHelp_Description = "Console_WidgetHelp_Description";
    public const string WebView_WidgetHelp_Description = "WebView_WidgetHelp_Description";

    // F4 â€” tooltips para los botones con label visible (explicativos, van mÃ¡s allÃ¡ del label).
    public const string Common_Close_Tooltip = "Common_Close_Tooltip";
    public const string WidgetPanel_SplittersConfirm_Tooltip = "WidgetPanel_SplittersConfirm_Tooltip";
    public const string WidgetPanel_SplittersCancel_Tooltip = "WidgetPanel_SplittersCancel_Tooltip";
    public const string WidgetPanel_SplittersDefault_Tooltip = "WidgetPanel_SplittersDefault_Tooltip";
    // Panel de ajustes rÃ¡pidos (icono equalizer de la toolbar).
    public const string QuickSettings_Widgets_Header = "QuickSettings_Widgets_Header";
    public const string QuickSettings_CornerRadius_Label = "QuickSettings_CornerRadius_Label";
    public const string QuickSettings_Gap_Label = "QuickSettings_Gap_Label";
    public const string QuickSettings_PanelMargin_Label = "QuickSettings_PanelMargin_Label";
}
