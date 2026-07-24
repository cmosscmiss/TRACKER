using Microsoft.Extensions.Options;
using MM4LB.Enums;
using MM4LB.Models;
using MM4LB.Services;

namespace MM4LB.Controls.ViewModels;

/// <summary>
/// View model del widget "Tools": contenedor de herramientas del dashboard (auditoría de media y huérfanos). Además
/// del ciclo de vida del widget (SlotIndex), ORQUESTA la persistencia: guarda/carga la herramienta abierta (pestaña
/// del FlipView) y los ajustes configurables de cada tool que contiene, leyendo/escribiendo directamente en los VMs
/// (singletons) de esas tools. Es el <c>WidgetViewModelBase</c> que enlaza el <see cref="Views.ToolsControl"/>.
/// </summary>
public class ToolsViewModel : WidgetViewModelBase
{
    #region Constants
    /// <summary>Nombres de las herramientas (por índice de pestaña), mostrados en la cabecera del widget: "TOOLS | ...".</summary>
    private static readonly string[] ToolTitleKeys = { MM4LB.Helpers.LocKeys.Tools_MediaCheck_Title, MM4LB.Helpers.LocKeys.Tools_Orphan_Title, MM4LB.Helpers.LocKeys.Tools_Shared_Title };
    #endregion

    #region Attributes
    private readonly AuditPanelViewModel _auditPanelViewModel;
    private readonly OrphanToolViewModel _orphanToolViewModel;
    private readonly SharedMediaToolViewModel _sharedMediaToolViewModel;
    private int _selectedToolIndex;
    #endregion

    #region Constructor
    public ToolsViewModel(SharedDataService sharedDataService, AuditPanelViewModel auditPanelViewModel, OrphanToolViewModel orphanToolViewModel, SharedMediaToolViewModel sharedMediaToolViewModel, IOptions<AppSettings> appSettings)
        : base(sharedDataService, appSettings)
    {
        _auditPanelViewModel = auditPanelViewModel;
        _orphanToolViewModel = orphanToolViewModel;
        _sharedMediaToolViewModel = sharedMediaToolViewModel;
    }
    #endregion

    #region Properties
    /// <summary>VM de la tool de huérfanos. Expuesto para que SettingsControl enlace su slider de thumbnails.</summary>
    public OrphanToolViewModel OrphanToolViewModel => _orphanToolViewModel;

    /// <summary>VM de la tool de media compartida. Expuesto para que SettingsControl enlace su slider de thumbnails.</summary>
    public SharedMediaToolViewModel SharedMediaToolViewModel => _sharedMediaToolViewModel;

    /// <summary>Índice de la herramienta abierta (pestaña del FlipView). Enlazado TwoWay y persistido.</summary>
    public int SelectedToolIndex
    {
        get => _selectedToolIndex;
        set
        {
            if (SetProperty(ref _selectedToolIndex, value))
            {
                OnPropertyChanged(nameof(CurrentToolTitle));
            }
        }
    }

    /// <summary>Nombre de la herramienta actualmente visible; lo muestra la cabecera del widget tras "TOOLS  |  ".</summary>
    public string CurrentToolTitle => _selectedToolIndex >= 0 && _selectedToolIndex < ToolTitleKeys.Length ? (MM4LB.Services.LocalizationService.Instance?[ToolTitleKeys[_selectedToolIndex]] ?? string.Empty) : string.Empty;
    #endregion

    #region Config
    /// <summary>Restaura la herramienta abierta y los ajustes de cada tool desde AppSettings.</summary>
    public override void LoadConfig()
    {
        AppSettings.ToolsControlSettings config = _appSettings.ToolsControl;

        SelectedToolIndex = config.SelectedToolIndex;

        _auditPanelViewModel.ShowOnlyDiscrepancies = config.MediaAudit.ShowOnlyDiscrepancies;
        _auditPanelViewModel.FilterBySelectedType = config.MediaAudit.FilterBySelectedType;

        _orphanToolViewModel.IsTableView = config.OrphanTool.IsTableView;
        _orphanToolViewModel.FilterBySelectedType = config.OrphanTool.FilterBySelectedType;

        // Galería (vista en grid) del orphan tool: aspecto, resolución y tamaño de miniatura. Su ImageGridViewModel
        // es la instancia base (no persiste por sí misma), así que lo orquestamos aquí como el resto de su config.
        _orphanToolViewModel.IgViewModel.ApplyAspectRatio(config.OrphanTool.AspectRatio?.Value);
        _orphanToolViewModel.IgViewModel.ApplyResolution(config.OrphanTool.Resolution?.Value);
        if (config.OrphanTool.ItemSize > 0) { _orphanToolViewModel.IgViewModel.Width = config.OrphanTool.ItemSize; }

        _sharedMediaToolViewModel.IsTableView = config.SharedTool.IsTableView;
        _sharedMediaToolViewModel.FilterBySelectedType = config.SharedTool.FilterBySelectedType;
        _sharedMediaToolViewModel.IgViewModel.ApplyAspectRatio(config.SharedTool.AspectRatio?.Value);
        _sharedMediaToolViewModel.IgViewModel.ApplyResolution(config.SharedTool.Resolution?.Value);
        if (config.SharedTool.ItemSize > 0) { _sharedMediaToolViewModel.IgViewModel.Width = config.SharedTool.ItemSize; }
    }

    /// <summary>Vuelca la herramienta abierta y los ajustes actuales de cada tool en AppSettings.</summary>
    public override void SaveConfig()
    {
        AppSettings.ToolsControlSettings config = _appSettings.ToolsControl;

        config.SelectedToolIndex = SelectedToolIndex;

        config.MediaAudit.ShowOnlyDiscrepancies = _auditPanelViewModel.ShowOnlyDiscrepancies;
        config.MediaAudit.FilterBySelectedType = _auditPanelViewModel.FilterBySelectedType;

        config.OrphanTool.IsTableView = _orphanToolViewModel.IsTableView;
        config.OrphanTool.FilterBySelectedType = _orphanToolViewModel.FilterBySelectedType;

        config.OrphanTool.AspectRatio = Enumeration.FromValue<AspectRatioSettings>(_orphanToolViewModel.IgViewModel.SelectedAspectRatio.Name) ?? config.OrphanTool.AspectRatio;
        config.OrphanTool.Resolution = Enumeration.FromValue<ImageResolutionSettings>(_orphanToolViewModel.IgViewModel.SelectedImageResolution.Name) ?? config.OrphanTool.Resolution;
        config.OrphanTool.ItemSize = _orphanToolViewModel.IgViewModel.Width;

        config.SharedTool.IsTableView = _sharedMediaToolViewModel.IsTableView;
        config.SharedTool.FilterBySelectedType = _sharedMediaToolViewModel.FilterBySelectedType;
        config.SharedTool.AspectRatio = Enumeration.FromValue<AspectRatioSettings>(_sharedMediaToolViewModel.IgViewModel.SelectedAspectRatio.Name) ?? config.SharedTool.AspectRatio;
        config.SharedTool.Resolution = Enumeration.FromValue<ImageResolutionSettings>(_sharedMediaToolViewModel.IgViewModel.SelectedImageResolution.Name) ?? config.SharedTool.Resolution;
        config.SharedTool.ItemSize = _sharedMediaToolViewModel.IgViewModel.Width;
    }
    #endregion

    /// <summary>No se suscribe a ningún evento; nada que liberar.</summary>
    public override void Dispose() { }
}
