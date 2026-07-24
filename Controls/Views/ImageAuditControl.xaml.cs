using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MM4LB.Controls.ViewModels;
using MM4LB.Helpers;
using System.ComponentModel;

namespace MM4LB.Controls.Views;

public sealed partial class ImageAuditControl : UserControl
{
    #region Dependency Properties
    /// <summary>
    /// Property to hold the view model for the control.
    /// </summary>
    public ImageAuditViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty) as ImageAuditViewModel;
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register("ViewModel", typeof(ImageAuditViewModel), typeof(ImageAuditControl), new PropertyMetadata(null, OnViewModelChanged));
    #endregion


    #region Attributes
    /// <summary>
    /// The view model whose configuration has already been loaded, used so the persisted settings are
    /// restored only once per view model instance.
    /// </summary>
    private readonly ViewModelConfigGate<ImageAuditViewModel> _configGate = new();
    #endregion


    #region Constructors
    public ImageAuditControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        // Cabeceras de columna localizadas (las columnas del DataGrid no aceptan {loc:Str}); se re-aplican al cambiar idioma.
        DataGridLoc.Attach(iaDataGrid,
            ("FileName", LocKeys.ImageAudit_FileName_Header),
            ("FileSize", LocKeys.ImageAudit_Size_Header),
            ("Quality", LocKeys.ImageAudit_Quality_Header),
            ("Dimensions", LocKeys.ImageAudit_Dimensions_Header),
            ("Duration", LocKeys.ImageAudit_Duration_Header),
            ("FileExtension", LocKeys.ImageAudit_Extension_Header),
            ("Region", LocKeys.Common_Region_Header),
            ("LinkedGames", LocKeys.ImageAudit_NumGames_Header),
            ("LinkedGamesToString", LocKeys.ImageAudit_Games_Header));
    }
    #endregion


    #region Configuration
    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ImageAuditControl control = (ImageAuditControl)d;

        // El tipo de set (imagen/vídeo) gobierna la visibilidad de las columnas Quality/Duration/Region del
        // DataGrid, que no es x:Bindable (las columnas no están en el árbol visual); se escucha IsVideoSet del
        // ViewModel para ajustarlas en código.
        if (e.OldValue is ImageAuditViewModel oldViewModel)
        {
            oldViewModel.PropertyChanged -= control.ViewModel_PropertyChanged;
        }
        if (e.NewValue is ImageAuditViewModel newViewModel)
        {
            newViewModel.PropertyChanged += control.ViewModel_PropertyChanged;
        }

        // Re-evaluar los x:Bind ahora que el ViewModel está asignado. Sin esto, los x:Bind OneTime que se
        // evaluaron antes de asignarse el ViewModel (su orden frente a la asignación del DP es una carrera de
        // arranque, sensible al coste del layout: a 2 columnas con la GameList lateral se evaluaban antes y
        // quedaban con null para siempre) no se vuelven a leer. En particular el ViewModel del ImageGridControl
        // interno (galería) quedaba sin asignar → galería y pastillas vacías. Mismo patrón que ImageGridControl.
        control.Bindings.Update();

        control.EnsureConfigurationLoaded();
        control.ApplyMediaTypeColumns();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        EnsureConfigurationLoaded();
        ApplyMediaTypeColumns();
    }

    /// <summary>
    /// El ViewModel es Singleton y el control se descarta al reconfigurar el panel; sin desuscribir aquí, el VM
    /// mantendría vivo cada control anterior (fuga). Patrón simétrico al de ImageGridControl.
    /// </summary>
    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is ImageAuditViewModel viewModel)
            viewModel.PropertyChanged -= ViewModel_PropertyChanged;

        Unloaded -= OnUnloaded;
    }

    /// <summary>
    /// Reacciona a los cambios del ViewModel relevantes para la vista. Por ahora, al cambiar el tipo de set
    /// (imagen/vídeo) reajusta la visibilidad de las columnas específicas de vídeo y de la columna de región.
    /// </summary>
    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImageAuditViewModel.IsVideoSet))
        {
            ApplyMediaTypeColumns();
        }
    }

    /// <summary>
    /// Muestra u oculta las columnas del DataGrid según el tipo de set: para vídeos se ven Quality y Duration y se
    /// oculta Region (los vídeos no tienen región); para imágenes, al revés.
    /// </summary>
    private void ApplyMediaTypeColumns()
    {
        if (ViewModel is null)
        {
            return;
        }

        bool isVideo = ViewModel.IsVideoSet;

        if (QualityColumn is not null)
        {
            QualityColumn.Visibility = isVideo ? Visibility.Visible : Visibility.Collapsed;
        }
        if (DurationColumn is not null)
        {
            DurationColumn.Visibility = isVideo ? Visibility.Visible : Visibility.Collapsed;
        }
        if (RegionColumn is not null)
        {
            RegionColumn.Visibility = isVideo ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>
    /// Restores the persisted configuration of the view model once, after the control is loaded and the
    /// view model has been assigned.
    /// </summary>
    private void EnsureConfigurationLoaded() => _configGate.Ensure(ViewModel);
    #endregion


    #region Subscribed events
    /// <summary>
    /// Data grid column sorting event.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    /// <summary>
    /// When the FlipView moves to the "Image set characteristics" page, lazily reads (in the background) the
    /// dimensions of the selected image set so that column gets filled in.
    /// </summary>
    private void AuditFlipView_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ViewModel != null)
            ViewModel.CharacteristicsPageVisible = ReferenceEquals(AuditFlipView.SelectedItem, ImageCharacteristicsItem);
    }

    /// <summary>Opens the help TeachingTip of the image-set characteristics chart.</summary>
    private void ImageCharacteristicsHelp_Click(object? sender, RoutedEventArgs e) => ImageCharacteristicsHelpTip.IsOpen = true;

    private void IaDataGrid_Sorting(object? sender, DataGridColumnEventArgs e)
    {
        DataGrid grid = (DataGrid)sender!;
        DataGridSorting.CycleColumn(grid, e.Column);
        var selectedImage = grid.SelectedItem;
        ViewModel?.SortCollection(e.Column.Tag?.ToString() ?? string.Empty, e.Column.SortDirection);
        grid.SelectedItem = selectedImage;
    }
    #endregion
}
