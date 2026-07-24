using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MM4LB.Controls.ViewModels;
using MM4LB.Helpers;
using MM4LB.Models;

namespace MM4LB.Controls.Views;

public sealed partial class GamesAuditControl : UserControl
{
    #region Dependency Properties
    /// <summary>
    /// Whether the header is hidden or not.
    /// </summary>
    public bool? IsHeaderVisible
    {
        get => GetValue(IsHeaderVisibleProperty) as bool?;
        set => SetValue(IsHeaderVisibleProperty, value);
    }

    public static readonly DependencyProperty IsHeaderVisibleProperty = DependencyProperty.Register("IsHeaderVisible", typeof(bool?), typeof(ImageGridControl), new PropertyMetadata(true));

    /// <summary>
    /// Property to hold the view model for the control.
    /// </summary>
    public GamesAuditViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty) as GamesAuditViewModel;
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register("ViewModel", typeof(GamesAuditViewModel), typeof(GamesAuditControl), new PropertyMetadata(null, OnViewModelChanged));
    #endregion


    #region Attributes
    /// <summary>
    /// The view model whose configuration has already been loaded, used so the persisted settings are
    /// restored only once per view model instance.
    /// </summary>
    private readonly ViewModelConfigGate<GamesAuditViewModel> _configGate = new();
    #endregion


    #region Constructors
    public GamesAuditControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;

        // Cabeceras de columna localizadas (las columnas del DataGrid no aceptan {loc:Str}); se re-aplican al cambiar idioma.
        DataGridLoc.Attach(dgGamesAudit,
            ("Title", LocKeys.Common_Title_Header),
            ("DatabaseId", LocKeys.GamesAudit_LaunchboxId_Header),
            ("RomFileName", LocKeys.GamesAudit_Rom_Header),
            ("Version", LocKeys.GamesAudit_Version_Header));
        DataGridLoc.Attach(dgInGallery,
            ("Title", LocKeys.Common_Title_Header),
            ("DatabaseId", LocKeys.GamesAudit_LaunchboxId_Header),
            ("MatchedImages", LocKeys.GamesAudit_MatchedImages_Header));
    }
    #endregion


    #region Configuration
    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        GamesAuditControl control = (GamesAuditControl)d;
        control.EnsureConfigurationLoaded();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        EnsureConfigurationLoaded();
        ScrollToSelectedGame();
    }

    /// <summary>
    /// Al cargar el control, posiciona el grid en el juego seleccionado (mapeado a la instancia de esta lista):
    /// lo selecciona y hace scroll hacia él. En runtime ya lo hace el SelectionChanged; esto cubre el arranque,
    /// cuando la selección ya estaba puesta antes de que el grid existiera.
    /// </summary>
    private void ScrollToSelectedGame()
    {
        if (ViewModel?.SharedDataService?.SelectedGame is not Game current)
        {
            return;
        }

        Game? match = ViewModel.GamesCollection.Find(g => g.Equals(current));
        if (match is null)
        {
            return;
        }

        ViewModel.SelectedGame = match;

        DataGrid grid = ViewModel.IsGamesAuditView ? dgGamesAudit : dgInGallery;
        if (grid is null)
        {
            return;
        }

        // Diferido (tras el layout): el contenedor de la fila puede no existir aún al cargar.
        grid.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            grid.UpdateLayout();
            grid.ScrollIntoView(match, null);
        });
    }

    /// <summary>
    /// Restores the persisted configuration of the view model once, after the control is loaded and the
    /// application settings have been restored. For the in-gallery instance this is a no-op (its
    /// <see cref="GamesAuditViewModel.LoadConfig"/> returns early when not in the games audit view).
    /// </summary>
    private void EnsureConfigurationLoaded() => _configGate.Ensure(ViewModel);
    #endregion


    #region Subscribed events
    /// <summary>
    /// Data grid column sorting event.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void GaDataGrid_Sorting(object sender, DataGridColumnEventArgs e)
    {
        DataGrid grid = (DataGrid)sender;
        DataGridSorting.CycleColumn(grid, e.Column);
        var selectedImage = grid.SelectedItem;
        ViewModel?.SortCollection(e.Column.Tag?.ToString() ?? string.Empty, e.Column.SortDirection);
        grid.SelectedItem = selectedImage;
    }
    #endregion
}
