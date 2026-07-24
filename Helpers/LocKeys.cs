namespace MM4LB.Helpers;

/// <summary>
/// Constantes de las claves de localización (i18n). Fuente única para usarlas desde código sin strings sueltos; el
/// XAML usa las mismas cadenas literales vía <c>{loc:Str Key=...}</c> / <c>help:Help.Key</c>.
/// <see cref="MM4LB.Services.LocalizationValidator"/> comprueba en DEBUG que toda clave aquí exista en
/// <c>Strings/Resources.resx</c> (evita claves rotas).
///
/// Convención: <c>{Scope}_{Element}_{Role}</c>, donde <b>Scope = el control/vista/servicio dueño</b> del texto
/// (nombre reconocible, sin el sufijo <c>Control</c>), y <c>Common_</c> para lo compartido/deduplicado. Sin puntos
/// (romperían el binding por indexador <c>[clave]</c>). Roles: Label, Tooltip, Header, Placeholder, Title,
/// Description, Empty, Format, Progress, Error. Se amplía por áreas (ver docs/Plan-Localizacion-Ayuda.md, F1–F4).
/// </summary>
public static class LocKeys
{
    // Common (compartido / deduplicado)
    public const string Common_AppName = "Common_AppName";
    public const string Common_Cancel_Tooltip = "Common_Cancel_Tooltip";
    public const string Common_Undo_Tooltip = "Common_Undo_Tooltip";
    public const string Common_CacheUsage_Label = "Common_CacheUsage_Label";
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
    public const string GameList_Filter_Placeholder = "GameList_Filter_Placeholder";
    public const string GameList_Missing_Label = "GameList_Missing_Label";
    public const string GameList_OneMedia_Label = "GameList_OneMedia_Label";
    public const string GameList_MoreMedia_Label = "GameList_MoreMedia_Label";

    // ImageType (banda de tipos)
    public const string ImageType_WithImages_Label = "ImageType_WithImages_Label";
    public const string ImageType_WithoutImages_Label = "ImageType_WithoutImages_Label";
    public const string ImageType_Favourites_Label = "ImageType_Favourites_Label";

    // ImageCollectionImport
    public const string ImageCollectionImport_Folder_Label = "ImageCollectionImport_Folder_Label";
    public const string ImageCollectionImport_Import_Label = "ImageCollectionImport_Import_Label";
    public const string ImageCollectionImport_Media_Label = "ImageCollectionImport_Media_Label";
    public const string ImageCollectionImport_Games_Label = "ImageCollectionImport_Games_Label";

    // GameDetails
    public const string GameDetails_InLaunchBox_Label = "GameDetails_InLaunchBox_Label";
    public const string GameDetails_NotInLaunchBox_Label = "GameDetails_NotInLaunchBox_Label";
    public const string GameDetails_KnownImages_Label = "GameDetails_KnownImages_Label";
    public const string GameDetails_KnownImages_Description = "GameDetails_KnownImages_Description";
    public const string GameDetails_MediaTypes_Label = "GameDetails_MediaTypes_Label";
    public const string GameDetails_MediaTypes_Description = "GameDetails_MediaTypes_Description";
    public const string GameDetails_Empty_Text = "GameDetails_Empty_Text";

    // ImageGrid (GAME MEDIA GALLERY)
    public const string ImageGrid_AspectRatio_Header = "ImageGrid_AspectRatio_Header";
    public const string ImageGrid_Resolution_Header = "ImageGrid_Resolution_Header";
    public const string ImageGrid_Coverage_Label = "ImageGrid_Coverage_Label";

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
    public const string StatsGlobal_GameSetCollection_Title = "StatsGlobal_GameSetCollection_Title";
    public const string StatsGlobal_GameSetCollection_Help = "StatsGlobal_GameSetCollection_Help";
    public const string StatsGlobal_GameSet_Title = "StatsGlobal_GameSet_Title";
    public const string StatsGlobal_GameSet_Help = "StatsGlobal_GameSet_Help";
    public const string StatsGlobal_ImageSet_Title = "StatsGlobal_ImageSet_Title";
    public const string StatsGlobal_ImageSet_Help = "StatsGlobal_ImageSet_Help";
    public const string StatsGlobal_ImageSetSize_Title = "StatsGlobal_ImageSetSize_Title";
    public const string StatsGlobal_ImageSetSize_Help = "StatsGlobal_ImageSetSize_Help";

    // StatsPlatform (GAME STATISTICS)
    public const string StatsPlatform_Scope_Header = "StatsPlatform_Scope_Header";
    public const string StatsPlatform_Favourites_Label = "StatsPlatform_Favourites_Label";
    public const string StatsPlatform_InPlatform_Label = "StatsPlatform_InPlatform_Label";
    public const string StatsPlatform_Coverage_Label = "StatsPlatform_Coverage_Label";
    public const string StatsPlatform_Coverage_Tooltip = "StatsPlatform_Coverage_Tooltip";
    public const string StatsPlatform_CoverageNow_Label = "StatsPlatform_CoverageNow_Label";
    public const string StatsPlatform_AverageAll_Label = "StatsPlatform_AverageAll_Label";
    public const string StatsPlatform_CoverageByGame_Title = "StatsPlatform_CoverageByGame_Title";
    public const string StatsPlatform_CoverageByGame_Help = "StatsPlatform_CoverageByGame_Help";
    public const string StatsPlatform_CoverageDistribution_Title = "StatsPlatform_CoverageDistribution_Title";
    public const string StatsPlatform_CoverageDistribution_Help = "StatsPlatform_CoverageDistribution_Help";
    public const string StatsPlatform_CoverageByType_Title = "StatsPlatform_CoverageByType_Title";
    public const string StatsPlatform_CoverageByType_Help = "StatsPlatform_CoverageByType_Help";
    public const string StatsPlatform_MediaSetByType_Title = "StatsPlatform_MediaSetByType_Title";
    public const string StatsPlatform_MediaSetByType_Help = "StatsPlatform_MediaSetByType_Help";
    public const string StatsPlatform_CoveragePill_Label = "StatsPlatform_CoveragePill_Label";
    public const string StatsPlatform_CoveragePill_Description = "StatsPlatform_CoveragePill_Description";

    // PlatformDetails
    public const string PlatformDetails_DropHint_Text = "PlatformDetails_DropHint_Text";
    public const string PlatformDetails_Importing_Text = "PlatformDetails_Importing_Text";
    public const string PlatformDetails_CoverageByPlatform_Title = "PlatformDetails_CoverageByPlatform_Title";
    public const string PlatformDetails_CoverageByPlatform_Help = "PlatformDetails_CoverageByPlatform_Help";
    public const string PlatformDetails_CoverageDistribution_Title = "PlatformDetails_CoverageDistribution_Title";
    public const string PlatformDetails_CoverageDistribution_Help = "PlatformDetails_CoverageDistribution_Help";
    public const string PlatformDetails_CoverageByType_Title = "PlatformDetails_CoverageByType_Title";
    public const string PlatformDetails_CoverageByType_Help = "PlatformDetails_CoverageByType_Help";
    public const string PlatformDetails_MediaSetByType_Title = "PlatformDetails_MediaSetByType_Title";
    public const string PlatformDetails_MediaSetByType_Help = "PlatformDetails_MediaSetByType_Help";

    // Common (más): diálogos
    public const string Common_Discard_Label = "Common_Discard_Label";
    public const string Common_Keep_Label = "Common_Keep_Label";
    public const string Common_Close_Label = "Common_Close_Label";
    public const string Common_OK_Label = "Common_OK_Label";
    public const string Common_NoRegion_Label = "Common_NoRegion_Label";

    // Diálogos
    public const string DeleteConfirmDialog_AskBefore_Label = "DeleteConfirmDialog_AskBefore_Label";
    public const string ImportImagesDialog_ExistingImages_Label = "ImportImagesDialog_ExistingImages_Label";
    public const string ImportImagesDialog_Help_Description = "ImportImagesDialog_Help_Description";
    public const string PlatformImageDropDialog_TypeLabel = "PlatformImageDropDialog_TypeLabel";
    public const string PlatformImageDropDialog_SelectType_Placeholder = "PlatformImageDropDialog_SelectType_Placeholder";
    public const string PlatformImageDropDialog_ExistingImages_Label = "PlatformImageDropDialog_ExistingImages_Label";
    public const string SelectRegionDialog_Prompt_Text = "SelectRegionDialog_Prompt_Text";
    public const string DashboardSettingsDialog_ApplyCriterion_Tooltip = "DashboardSettingsDialog_ApplyCriterion_Tooltip";
    public const string DashboardSettingsDialog_Preselection_Title = "DashboardSettingsDialog_Preselection_Title";
    public const string DashboardSettingsDialog_Preselection_Description = "DashboardSettingsDialog_Preselection_Description";
    public const string DashboardSettingsDialog_Processing_Title = "DashboardSettingsDialog_Processing_Title";
    public const string DashboardSettingsDialog_Processing_Description = "DashboardSettingsDialog_Processing_Description";
    public const string DialogsSettingsControl_Placeholder_Text = "DialogsSettingsControl_Placeholder_Text";
    public const string TemplateNameDialog_ChooseSlot_Text = "TemplateNameDialog_ChooseSlot_Text";
    public const string TemplateNameDialog_Name_Label = "TemplateNameDialog_Name_Label";
    public const string TemplateNameDialog_Name_Placeholder = "TemplateNameDialog_Name_Placeholder";

    // SetLaunchBoxFoldersWindow
    public const string SetLaunchBoxFolders_Title = "SetLaunchBoxFolders_Title";
    public const string SetLaunchBoxFolders_Folder_Label = "SetLaunchBoxFolders_Folder_Label";
    public const string SetLaunchBoxFolders_DataFolder_Label = "SetLaunchBoxFolders_DataFolder_Label";
    public const string SetLaunchBoxFolders_PlatformsFolder_Label = "SetLaunchBoxFolders_PlatformsFolder_Label";
    public const string SetLaunchBoxFolders_PlatformsXml_Label = "SetLaunchBoxFolders_PlatformsXml_Label";
    public const string SetLaunchBoxFolders_SettingsXml_Label = "SetLaunchBoxFolders_SettingsXml_Label";
    public const string SetLaunchBoxFolders_SelectFolder_Label = "SetLaunchBoxFolders_SelectFolder_Label";

    // MainWindow : títulos de widgets
    public const string MainWindow_DashboardWidget_Title = "MainWindow_DashboardWidget_Title";
    public const string MainWindow_RegionsWidget_Title = "MainWindow_RegionsWidget_Title";
    public const string MainWindow_GameStatsWidget_Title = "MainWindow_GameStatsWidget_Title";
    public const string MainWindow_GlobalStatsWidget_Title = "MainWindow_GlobalStatsWidget_Title";
    public const string MainWindow_ActivityLogWidget_Title = "MainWindow_ActivityLogWidget_Title";
    public const string MainWindow_WebSearchWidget_Title = "MainWindow_WebSearchWidget_Title";
    public const string MainWindow_GalleryWidget_Title = "MainWindow_GalleryWidget_Title";
    public const string MainWindow_GamesAuditWidget_Title = "MainWindow_GamesAuditWidget_Title";
    public const string MainWindow_GameDetailsWidget_Title = "MainWindow_GameDetailsWidget_Title";
    public const string MainWindow_MediaAuditWidget_Title = "MainWindow_MediaAuditWidget_Title";
    public const string MainWindow_ImportWidget_Title = "MainWindow_ImportWidget_Title";
    public const string MainWindow_ToolsWidget_Title = "MainWindow_ToolsWidget_Title";

    // Common (más): botones de diálogo
    public const string Common_Cancel_Label = "Common_Cancel_Label";
    public const string Common_Import_Label = "Common_Import_Label";
    public const string Common_Add_Label = "Common_Add_Label";
    public const string Common_Save_Label = "Common_Save_Label";
    public const string Common_Apply_Label = "Common_Apply_Label";
    public const string Common_Empty_Label = "Common_Empty_Label";

    // DialogsService : títulos de diálogos
    public const string DialogsService_DeleteMedia_Title = "DialogsService_DeleteMedia_Title";
    public const string DialogsService_ImportMatchedImages_Title = "DialogsService_ImportMatchedImages_Title";
    public const string DialogsService_ImportRegion_Title = "DialogsService_ImportRegion_Title";
    public const string DialogsService_AddPlatformImage_Title = "DialogsService_AddPlatformImage_Title";
    public const string DialogsService_DashboardSettings_Title = "DialogsService_DashboardSettings_Title";
    public const string DialogsService_Settings_Title = "DialogsService_Settings_Title";
    public const string DialogsService_SaveTemplate_Title = "DialogsService_SaveTemplate_Title";

    // Criterios de los dashboards
    public const string DashboardCriteria_First_Label = "DashboardCriteria_First_Label";
    public const string DashboardCriteria_Second_Label = "DashboardCriteria_Second_Label";
    public const string DashboardCriteria_Region_Label = "DashboardCriteria_Region_Label";
    public const string DashboardCriteria_Suffix_Label = "DashboardCriteria_Suffix_Label";
    public const string DashboardCriteria_FileName_Label = "DashboardCriteria_FileName_Label";
    public const string Common_OtherRegions_Label = "Common_OtherRegions_Label";

    // ToolsViewModel
    public const string Tools_MediaCheck_Title = "Tools_MediaCheck_Title";
    public const string Tools_Orphan_Title = "Tools_Orphan_Title";
    public const string Tools_Shared_Title = "Tools_Shared_Title";

    // SearchStrings
    public const string SearchStrings_DefaultTitle = "SearchStrings_DefaultTitle";
    public const string SearchStrings_Empty = "SearchStrings_Empty";
    public const string SearchStrings_GameTitle = "SearchStrings_GameTitle";
    public const string SearchStrings_GameImageTitle = "SearchStrings_GameImageTitle";

    // AuditPanel (VM)
    public const string AuditPanel_StatusMissing_Label = "AuditPanel_StatusMissing_Label";
    public const string AuditPanel_StatusExtra_Label = "AuditPanel_StatusExtra_Label";
    public const string AuditPanel_StatusOk_Label = "AuditPanel_StatusOk_Label";
    public const string AuditPanel_Dialog_Title = "AuditPanel_Dialog_Title";
    public const string AuditPanel_SelectPlatform_Text = "AuditPanel_SelectPlatform_Text";
    public const string AuditPanel_ExcelError_Text = "AuditPanel_ExcelError_Text";

    // MediaAuditService
    public const string MediaAuditService_UnknownType_Warning = "MediaAuditService_UnknownType_Warning";

    // ConsoleViewModel
    public const string ConsoleViewModel_EmptyBackup_Title = "ConsoleViewModel_EmptyBackup_Title";
    public const string ConsoleViewModel_EmptyBackup_Content = "ConsoleViewModel_EmptyBackup_Content";
    public const string ConsoleViewModel_Emptying_Progress = "ConsoleViewModel_Emptying_Progress";
    public const string ConsoleViewModel_Emptied_Progress = "ConsoleViewModel_Emptied_Progress";

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

    // ViewModels (progreso/errores) + placeholder de región
    public const string Common_NoneRegion_Placeholder = "Common_NoneRegion_Placeholder";
    public const string ImageAudit_DeleteOrphanImages_Error = "ImageAudit_DeleteOrphanImages_Error";
    public const string ImageAudit_RetrieveDimensions_Error = "ImageAudit_RetrieveDimensions_Error";
    public const string ImageAudit_OrphanFilesDeleted_Progress = "ImageAudit_OrphanFilesDeleted_Progress";
    public const string ImageAudit_RetrievingDimensions_Progress = "ImageAudit_RetrievingDimensions_Progress";
    public const string ImageAudit_DimensionsRetrieved_Progress = "ImageAudit_DimensionsRetrieved_Progress";
    public const string ImageAudit_DimensionsRetrievedShort_Progress = "ImageAudit_DimensionsRetrievedShort_Progress";
    public const string ImageAudit_Quality_Label = "ImageAudit_Quality_Label";
    public const string ImageAudit_Duration_Label = "ImageAudit_Duration_Label";
    public const string ImageAudit_MostUsed_Label = "ImageAudit_MostUsed_Label";
    public const string ImageAudit_Others_Label = "ImageAudit_Others_Label";
    public const string OrphanTool_Scan_Error = "OrphanTool_Scan_Error";
    public const string OrphanTool_Delete_Error = "OrphanTool_Delete_Error";
    public const string OrphanTool_DeletingMedia_Progress = "OrphanTool_DeletingMedia_Progress";
    public const string OrphanTool_MediaDeleted_Progress = "OrphanTool_MediaDeleted_Progress";
    public const string SharedMediaTool_Scan_Error = "SharedMediaTool_Scan_Error";
    public const string ImageGridGame_RefreshGallery_Error = "ImageGridGame_RefreshGallery_Error";
    public const string ImageGridGame_SelectionChange_Error = "ImageGridGame_SelectionChange_Error";
    public const string ImageType_LoadImages_Error = "ImageType_LoadImages_Error";
    public const string ImageCollectionImport_Import_Error = "ImageCollectionImport_Import_Error";
    public const string ImageCollectionImport_SelectFolder_Error = "ImageCollectionImport_SelectFolder_Error";
    public const string PlatformDetails_RefreshView_Error = "PlatformDetails_RefreshView_Error";
    public const string PlatformDetails_RefreshImages_Error = "PlatformDetails_RefreshImages_Error";
    public const string PlatformDetails_AddDroppedMedia_Error = "PlatformDetails_AddDroppedMedia_Error";
    public const string GameImagesDashboard_AddDroppedMedia_Error = "GameImagesDashboard_AddDroppedMedia_Error";
    public const string GameImagesDashboard_RefreshGame_Error = "GameImagesDashboard_RefreshGame_Error";
    public const string GameImagesDashboard_RefreshImages_Error = "GameImagesDashboard_RefreshImages_Error";
    public const string GameImagesDashboard_LoadAddedImage_Error = "GameImagesDashboard_LoadAddedImage_Error";
    public const string GameImagesRegionDashboard_LoadAddedImage_Error = "GameImagesRegionDashboard_LoadAddedImage_Error";
    public const string GameImagesRegionDashboard_Refresh_Error = "GameImagesRegionDashboard_Refresh_Error";
    public const string GameImagesRegionDashboard_AddDroppedMedia_Error = "GameImagesRegionDashboard_AddDroppedMedia_Error";
    public const string MainWindow_FfmpegPreparing_Progress = "MainWindow_FfmpegPreparing_Progress";
    public const string MainWindow_FfmpegReady_Progress = "MainWindow_FfmpegReady_Progress";
    public const string MainWindow_FfmpegCancelled_Progress = "MainWindow_FfmpegCancelled_Progress";
    public const string MainWindow_FfmpegPrepare_Error = "MainWindow_FfmpegPrepare_Error";

    // Toolbar principal (tooltips)
    public const string Toolbar_Layout_Tooltip = "Toolbar_Layout_Tooltip";
    public const string Toolbar_Widgets_Tooltip = "Toolbar_Widgets_Tooltip";
    public const string Toolbar_Templates_Tooltip = "Toolbar_Templates_Tooltip";
    public const string Toolbar_QuickSettings_Tooltip = "Toolbar_QuickSettings_Tooltip";
    public const string Toolbar_Settings_Tooltip = "Toolbar_Settings_Tooltip";
    public const string Toolbar_Resize_Tooltip = "Toolbar_Resize_Tooltip";
    public const string Toolbar_SaveTemplate_Tooltip = "Toolbar_SaveTemplate_Tooltip";
    public const string Toolbar_TogglePlatformDetails_Tooltip = "Toolbar_TogglePlatformDetails_Tooltip";
    public const string Toolbar_TogglePlatformList_Tooltip = "Toolbar_TogglePlatformList_Tooltip";
    public const string Toolbar_ToggleGameList_Tooltip = "Toolbar_ToggleGameList_Tooltip";
    public const string Toolbar_ToggleImageTypeBand_Tooltip = "Toolbar_ToggleImageTypeBand_Tooltip";

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
    public const string ImageAudit_InUse_Label = "ImageAudit_InUse_Label";
    public const string ImageAudit_Shared_Label = "ImageAudit_Shared_Label";
    public const string ImageAudit_Orphan_Label = "ImageAudit_Orphan_Label";
    public const string ImageAudit_Dimensions_Label = "ImageAudit_Dimensions_Label";
    public const string ImageAudit_Dimensions_Tooltip = "ImageAudit_Dimensions_Tooltip";
    public const string ImageAudit_Characteristics_Title = "ImageAudit_Characteristics_Title";
    public const string ImageAudit_Characteristics_Description = "ImageAudit_Characteristics_Description";
    public const string ImageAudit_FileName_Header = "ImageAudit_FileName_Header";
    public const string ImageAudit_Size_Header = "ImageAudit_Size_Header";
    public const string ImageAudit_Quality_Header = "ImageAudit_Quality_Header";
    public const string ImageAudit_Dimensions_Header = "ImageAudit_Dimensions_Header";
    public const string ImageAudit_Duration_Header = "ImageAudit_Duration_Header";
    public const string ImageAudit_Extension_Header = "ImageAudit_Extension_Header";
    public const string ImageAudit_NumGames_Header = "ImageAudit_NumGames_Header";
    public const string ImageAudit_Games_Header = "ImageAudit_Games_Header";

    // Console (ACTIVITY LOG)
    public const string Console_CachedMedia_Label = "Console_CachedMedia_Label";
    public const string Console_BackupMedia_Label = "Console_BackupMedia_Label";
    public const string Console_MediaTotalSize_Description = "Console_MediaTotalSize_Description";
    public const string Console_Backup_Label = "Console_Backup_Label";
    public const string Console_Backup_Tooltip = "Console_Backup_Tooltip";

    // FooterEventViewer
    public const string FooterEventViewer_OlderEvent_Tooltip = "FooterEventViewer_OlderEvent_Tooltip";
    public const string FooterEventViewer_NewerEvent_Tooltip = "FooterEventViewer_NewerEvent_Tooltip";
    public const string FooterEventViewer_LatestEvent_Tooltip = "FooterEventViewer_LatestEvent_Tooltip";

    // FooterSound
    public const string FooterSound_Sound_Tooltip = "FooterSound_Sound_Tooltip";
    public const string FooterSound_Mute_Tooltip = "FooterSound_Mute_Tooltip";

    // MainWindow
    public const string MainWindow_Platform_Label = "MainWindow_Platform_Label";

    // Common auditoría/tools (toolbar de vista, columnas de tabla, pills)
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

    // GamesAudit
    public const string GamesAudit_InCollection_Label = "GamesAudit_InCollection_Label";
    public const string GamesAudit_InLbDb_Label = "GamesAudit_InLbDb_Label";
    public const string GamesAudit_NotInLbDb_Label = "GamesAudit_NotInLbDb_Label";
    public const string GamesAudit_NoMatches_Label = "GamesAudit_NoMatches_Label";
    public const string GamesAudit_OneMatch_Label = "GamesAudit_OneMatch_Label";
    public const string GamesAudit_MoreThanOneMatch_Label = "GamesAudit_MoreThanOneMatch_Label";
    public const string GamesAudit_LaunchboxId_Header = "GamesAudit_LaunchboxId_Header";
    public const string GamesAudit_Rom_Header = "GamesAudit_Rom_Header";
    public const string GamesAudit_Version_Header = "GamesAudit_Version_Header";
    public const string GamesAudit_MatchedImages_Header = "GamesAudit_MatchedImages_Header";

    // OrphanTool
    public const string OrphanTool_DeleteAll_Label = "OrphanTool_DeleteAll_Label";
    public const string OrphanTool_Empty_Text = "OrphanTool_Empty_Text";
    public const string OrphanTool_Orphans_Label = "OrphanTool_Orphans_Label";
    public const string OrphanTool_OrphansRatio_Description = "OrphanTool_OrphansRatio_Description";
    public const string OrphanTool_SizeRatio_Description = "OrphanTool_SizeRatio_Description";
    public const string OrphanTool_TypesRatio_Description = "OrphanTool_TypesRatio_Description";
    public const string OrphanTool_TopTypeRatio_Description = "OrphanTool_TopTypeRatio_Description";

    // SharedMediaTool
    public const string SharedMediaTool_Empty_Text = "SharedMediaTool_Empty_Text";
    public const string SharedMediaTool_Shared_Label = "SharedMediaTool_Shared_Label";
    public const string SharedMediaTool_SharedRatio_Description = "SharedMediaTool_SharedRatio_Description";
    public const string SharedMediaTool_SizeRatio_Description = "SharedMediaTool_SizeRatio_Description";
    public const string SharedMediaTool_TypesRatio_Description = "SharedMediaTool_TypesRatio_Description";
    public const string SharedMediaTool_Games_Label = "SharedMediaTool_Games_Label";
    public const string SharedMediaTool_GamesRatio_Description = "SharedMediaTool_GamesRatio_Description";

    // AuditPanel (LaunchBox media check)
    public const string AuditPanel_CheckMedia_Label = "AuditPanel_CheckMedia_Label";
    public const string AuditPanel_OnlyDiscrepancies_Label = "AuditPanel_OnlyDiscrepancies_Label";
    public const string AuditPanel_SelectedMediaType_Label = "AuditPanel_SelectedMediaType_Label";
    public const string AuditPanel_ImportedFile_Label = "AuditPanel_ImportedFile_Label";
    public const string AuditPanel_Warnings_Title = "AuditPanel_Warnings_Title";
    public const string AuditPanel_Category_Header = "AuditPanel_Category_Header";
    public const string AuditPanel_LaunchBox_Header = "AuditPanel_LaunchBox_Header";
    public const string AuditPanel_MM4LB_Header = "AuditPanel_MM4LB_Header";
    public const string AuditPanel_Status_Header = "AuditPanel_Status_Header";
    public const string AuditPanel_Empty_Text = "AuditPanel_Empty_Text";
    public const string AuditPanel_Compared_Label = "AuditPanel_Compared_Label";
    public const string AuditPanel_Compared_Description = "AuditPanel_Compared_Description";
    public const string AuditPanel_Discrepancies_Label = "AuditPanel_Discrepancies_Label";
    public const string AuditPanel_Discrepancies_Description = "AuditPanel_Discrepancies_Description";
    public const string AuditPanel_WrongFiles_Label = "AuditPanel_WrongFiles_Label";
    public const string AuditPanel_WrongFiles_Description = "AuditPanel_WrongFiles_Description";
    public const string AuditPanel_NotMatched_Label = "AuditPanel_NotMatched_Label";
    public const string AuditPanel_NotMatched_Description = "AuditPanel_NotMatched_Description";
    public const string AuditPanel_NotInExcel_Label = "AuditPanel_NotInExcel_Label";
    public const string AuditPanel_NotInExcel_Description = "AuditPanel_NotInExcel_Description";

    // SettingsDialog (títulos de sección y opciones generadas en el VM del diálogo)
    public const string SettingsDialog_General_Title = "SettingsDialog_General_Title";
    public const string SettingsDialog_Regions_Title = "SettingsDialog_Regions_Title";
    public const string SettingsDialog_MediaTypes_Title = "SettingsDialog_MediaTypes_Title";
    public const string SettingsDialog_Theme_Title = "SettingsDialog_Theme_Title";
    public const string SettingsDialog_About_Title = "SettingsDialog_About_Title";
    public const string SettingsDialog_ToolbarGroupsSeparate_Label = "SettingsDialog_ToolbarGroupsSeparate_Label";
    public const string SettingsDialog_ToolbarGroupsGrouped_Label = "SettingsDialog_ToolbarGroupsGrouped_Label";
    public const string SettingsDialog_ToolbarGroupsAuto_Label = "SettingsDialog_ToolbarGroupsAuto_Label";

    // GeneralSettings (pestaña General de Settings)
    public const string GeneralSettings_Language_Label = "GeneralSettings_Language_Label";
    public const string GeneralSettings_Language_Tooltip = "GeneralSettings_Language_Tooltip";
    public const string GeneralSettings_ToolbarGroups_Label = "GeneralSettings_ToolbarGroups_Label";
    public const string GeneralSettings_CacheSize_Label = "GeneralSettings_CacheSize_Label";
    public const string GeneralSettings_ShowWidgetHeader_Label = "GeneralSettings_ShowWidgetHeader_Label";
    public const string GeneralSettings_FooterEventViewer_Label = "GeneralSettings_FooterEventViewer_Label";
    public const string GeneralSettings_ConfirmDelete_Label = "GeneralSettings_ConfirmDelete_Label";
    public const string GeneralSettings_LogExceptions_Label = "GeneralSettings_LogExceptions_Label";

    // RegionsSettings / MediaTypesSettings
    public const string RegionsSettings_Intro_Text = "RegionsSettings_Intro_Text";
    public const string MediaTypesSettings_Intro_Text = "MediaTypesSettings_Intro_Text";

    // ThemeSettings
    public const string ThemeSettings_Theme_Label = "ThemeSettings_Theme_Label";
    public const string ThemeSettings_RandomTheme_Label = "ThemeSettings_RandomTheme_Label";
    public const string ThemeSettings_BackgroundOverlay_Header = "ThemeSettings_BackgroundOverlay_Header";
    public const string ThemeSettings_TintOpacity_Label = "ThemeSettings_TintOpacity_Label";
    public const string ThemeSettings_TintSaturation_Label = "ThemeSettings_TintSaturation_Label";
    public const string ThemeSettings_TintBrightness_Label = "ThemeSettings_TintBrightness_Label";
    public const string ThemeSettings_OverlayBlur_Label = "ThemeSettings_OverlayBlur_Label";
    public const string ThemeSettings_OverlayOpacity_Label = "ThemeSettings_OverlayOpacity_Label";
    public const string ThemeSettings_LoadingBackground_Header = "ThemeSettings_LoadingBackground_Header";
    public const string ThemeSettings_TintBackground_Label = "ThemeSettings_TintBackground_Label";
    public const string ThemeSettings_NeonFrame_Label = "ThemeSettings_NeonFrame_Label";

    // AboutSettings
    public const string AboutSettings_Tagline_Text = "AboutSettings_Tagline_Text";
    public const string AboutSettings_Description_Text = "AboutSettings_Description_Text";
    public const string AboutSettings_Details_Header = "AboutSettings_Details_Header";
    public const string AboutSettings_Build_Label = "AboutSettings_Build_Label";
    public const string AboutSettings_Runtime_Label = "AboutSettings_Runtime_Label";
    public const string AboutSettings_Architecture_Label = "AboutSettings_Architecture_Label";
    public const string AboutSettings_DataSource_Label = "AboutSettings_DataSource_Label";
    public const string AboutSettings_ThirdParty_Header = "AboutSettings_ThirdParty_Header";
    public const string AboutSettings_AccentNote_Text = "AboutSettings_AccentNote_Text";
    public const string AboutSettings_Copyright_Text = "AboutSettings_Copyright_Text";

    // FooterHelp (botón de ayuda del footer)
    public const string FooterHelp_Toggle_Tooltip = "FooterHelp_Toggle_Tooltip";

    // Dashboard_* : compartido por los dos dashboards de imágenes (GameImages + GameImagesRegion)
    public const string Dashboard_Layout_Header = "Dashboard_Layout_Header";
    public const string Dashboard_HorView_Label = "Dashboard_HorView_Label";
    public const string Dashboard_VerView_Label = "Dashboard_VerView_Label";
    public const string Dashboard_VideoQuality_Header = "Dashboard_VideoQuality_Header";
    public const string Dashboard_Quality240_Label = "Dashboard_Quality240_Label";
    public const string Dashboard_Quality360_Label = "Dashboard_Quality360_Label";
    public const string Dashboard_Quality480_Label = "Dashboard_Quality480_Label";
    public const string Dashboard_Quality720_Label = "Dashboard_Quality720_Label";
    public const string Dashboard_Quality1080_Label = "Dashboard_Quality1080_Label";
    public const string Dashboard_Strings_Label = "Dashboard_Strings_Label";
    public const string Dashboard_Strings_Tooltip = "Dashboard_Strings_Tooltip";
    public const string Dashboard_Delete_Label = "Dashboard_Delete_Label";
    public const string Dashboard_Delete_Tooltip = "Dashboard_Delete_Tooltip";
    public const string Dashboard_Settings_Label = "Dashboard_Settings_Label";
    public const string Dashboard_Settings_Tooltip = "Dashboard_Settings_Tooltip";
    public const string Dashboard_Importing_Text = "Dashboard_Importing_Text";
    public const string Dashboard_ProcessPrevious_Label = "Dashboard_ProcessPrevious_Label";
    public const string Dashboard_ProcessNext_Label = "Dashboard_ProcessNext_Label";

    // GameImagesDashboard_* : dashboard estándar
    public const string GameImagesDashboard_ProcessPrevious_Tooltip = "GameImagesDashboard_ProcessPrevious_Tooltip";
    public const string GameImagesDashboard_ProcessNext_Tooltip = "GameImagesDashboard_ProcessNext_Tooltip";

    // GameImagesRegionDashboard_* : dashboard por regiones
    public const string GameImagesRegionDashboard_ProcessRegion_Label = "GameImagesRegionDashboard_ProcessRegion_Label";
    public const string GameImagesRegionDashboard_ProcessRegion_Tooltip = "GameImagesRegionDashboard_ProcessRegion_Tooltip";
    public const string GameImagesRegionDashboard_ProcessPrevious_Tooltip = "GameImagesRegionDashboard_ProcessPrevious_Tooltip";
    public const string GameImagesRegionDashboard_ProcessNext_Tooltip = "GameImagesRegionDashboard_ProcessNext_Tooltip";

    // Piloto (F0): validar idioma en caliente + toggle de ayuda. Se retirará al terminar la migración.
    public const string GeneralSettings_LanguagePilot_Caption = "GeneralSettings_LanguagePilot_Caption";
    public const string GeneralSettings_HelpPilot_Tooltip = "GeneralSettings_HelpPilot_Tooltip";

    // *_WidgetHelp_Description : descripción del TeachingTip de ayuda de cada widget (icono de la cabecera).
    public const string GameImagesDashboard_WidgetHelp_Description = "GameImagesDashboard_WidgetHelp_Description";
    public const string GameImagesRegionDashboard_WidgetHelp_Description = "GameImagesRegionDashboard_WidgetHelp_Description";
    public const string StatsPlatform_WidgetHelp_Description = "StatsPlatform_WidgetHelp_Description";
    public const string StatsGlobal_WidgetHelp_Description = "StatsGlobal_WidgetHelp_Description";
    public const string Console_WidgetHelp_Description = "Console_WidgetHelp_Description";
    public const string WebView_WidgetHelp_Description = "WebView_WidgetHelp_Description";
    public const string ImageGrid_WidgetHelp_Description = "ImageGrid_WidgetHelp_Description";
    public const string GamesAudit_WidgetHelp_Description = "GamesAudit_WidgetHelp_Description";
    public const string GameDetails_WidgetHelp_Description = "GameDetails_WidgetHelp_Description";
    public const string ImageAudit_WidgetHelp_Description = "ImageAudit_WidgetHelp_Description";
    public const string ImageCollectionImport_WidgetHelp_Description = "ImageCollectionImport_WidgetHelp_Description";
    public const string Tools_WidgetHelp_Description = "Tools_WidgetHelp_Description";

    // F4 — tooltips para los botones con label visible (explicativos, van más allá del label).
    public const string Common_Close_Tooltip = "Common_Close_Tooltip";
    public const string SetLaunchBoxFolders_SelectFolder_Tooltip = "SetLaunchBoxFolders_SelectFolder_Tooltip";
    public const string SetLaunchBoxFolders_Save_Tooltip = "SetLaunchBoxFolders_Save_Tooltip";
    public const string AuditPanel_CheckMedia_Tooltip = "AuditPanel_CheckMedia_Tooltip";
    public const string AuditPanel_OnlyDiscrepancies_Tooltip = "AuditPanel_OnlyDiscrepancies_Tooltip";
    public const string AuditPanel_SelectedMediaType_Tooltip = "AuditPanel_SelectedMediaType_Tooltip";
    public const string ImageCollectionImport_Folder_Tooltip = "ImageCollectionImport_Folder_Tooltip";
    public const string ImageCollectionImport_Import_Tooltip = "ImageCollectionImport_Import_Tooltip";
    public const string ImageAudit_Orphan_Tooltip = "ImageAudit_Orphan_Tooltip";
    public const string OrphanTool_DeleteAll_Tooltip = "OrphanTool_DeleteAll_Tooltip";
    public const string WidgetPanel_SplittersConfirm_Tooltip = "WidgetPanel_SplittersConfirm_Tooltip";
    public const string WidgetPanel_SplittersCancel_Tooltip = "WidgetPanel_SplittersCancel_Tooltip";
    public const string WidgetPanel_SplittersDefault_Tooltip = "WidgetPanel_SplittersDefault_Tooltip";
    public const string ImageType_Toggle_Tooltip = "ImageType_Toggle_Tooltip";
    public const string TemplateSlots_BuiltIn_Tooltip = "TemplateSlots_BuiltIn_Tooltip";
    // Panel de ajustes rápidos (icono equalizer de la toolbar).
    public const string QuickSettings_Thumbnails_Header = "QuickSettings_Thumbnails_Header";
    public const string QuickSettings_GameGallery_Label = "QuickSettings_GameGallery_Label";
    public const string QuickSettings_MediaAudit_Label = "QuickSettings_MediaAudit_Label";
    public const string QuickSettings_ImportMedia_Label = "QuickSettings_ImportMedia_Label";
    public const string QuickSettings_OrphanMedia_Label = "QuickSettings_OrphanMedia_Label";
    public const string QuickSettings_Sound_Header = "QuickSettings_Sound_Header";
    public const string QuickSettings_VideoVolume_Label = "QuickSettings_VideoVolume_Label";
    public const string QuickSettings_Widgets_Header = "QuickSettings_Widgets_Header";
    public const string QuickSettings_CornerRadius_Label = "QuickSettings_CornerRadius_Label";
    public const string QuickSettings_Gap_Label = "QuickSettings_Gap_Label";
    public const string QuickSettings_PanelMargin_Label = "QuickSettings_PanelMargin_Label";
}
