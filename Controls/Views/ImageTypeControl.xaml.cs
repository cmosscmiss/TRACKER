using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MM4LB.Controls.Templates;
using MM4LB.Controls.ViewModels;
using MM4LB.Models;
using MM4LB.Services;
using static MM4LB.Services.SharedDataService;

namespace MM4LB.Controls.Views;

/// <summary>
/// Modos de visualización del <see cref="ImageTypeControl"/>.
/// </summary>
public enum ImageTypeDisplayMode
{
    /// <summary>Solo el selector (combo) y los filtros. Usado en la barra lateral, junto a la lista de juegos.</summary>
    Selector,

    /// <summary>El selector, los filtros y, además, los botones de tipos favoritos. Usado en la banda del panel.</summary>
    Full
}

/// <summary>
/// User control responsible for displaying and managing the image type selection area.
///
/// The control exposes a view model through a dependency property so it can be bound from XAML, reused in different
/// parent views, and participate correctly in the WinUI property system.
///
/// Soporta dos modos de visualización (<see cref="DisplayMode"/>): <see cref="ImageTypeDisplayMode.Selector"/>
/// (combo + filtros) y <see cref="ImageTypeDisplayMode.Full"/> (combo + filtros + favoritos, todo en una sola fila).
///
/// Los botones de tipos favoritos están integrados aquí (no en un control aparte): hablan directamente con
/// <see cref="SharedDataService"/> (pulsar un botón fija el set seleccionado y la selección se refleja de vuelta al
/// cambiar el set en otro sitio). Los botones cuyo tipo está filtrado fuera de la lista
/// (<see cref="SharedDataService.ImageTypesFiltered"/>) quedan deshabilitados. Solo se construyen en modo Full.
/// </summary>
public sealed partial class ImageTypeControl : UserControl, INotifyPropertyChanged
{
    #region Attributes
    private SharedDataService _sharedDataService = null!;
    private AppSettings _appSettings = null!;
    private bool _anyFavouriteTypeButtonSelected;
    #endregion

    #region Dependency Properties
    /// <summary>
    /// Gets or sets the view model used by this control.
    /// </summary>
    public ImageTypeViewModel? ViewModel
    {
        get => (ImageTypeViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    /// <summary>
    /// Identifies the <see cref="ViewModel"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register("ViewModel", typeof(ImageTypeViewModel), typeof(ImageTypeControl), new PropertyMetadata(null));

    /// <summary>
    /// Modo de visualización: <see cref="ImageTypeDisplayMode.Selector"/> (combo + filtros) o
    /// <see cref="ImageTypeDisplayMode.Full"/> (además, los favoritos). Por defecto <see cref="ImageTypeDisplayMode.Selector"/>.
    /// </summary>
    public ImageTypeDisplayMode DisplayMode
    {
        get => (ImageTypeDisplayMode)GetValue(DisplayModeProperty);
        set => SetValue(DisplayModeProperty, value);
    }

    /// <summary>
    /// Identifies the <see cref="DisplayMode"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty DisplayModeProperty = DependencyProperty.Register(nameof(DisplayMode), typeof(ImageTypeDisplayMode), typeof(ImageTypeControl), new PropertyMetadata(ImageTypeDisplayMode.Selector, OnDisplayModeChanged));

    /// <summary>Refleja el cambio de modo en la UI.</summary>
    private static void OnDisplayModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ImageTypeControl)d).ApplyDisplayMode();
    }
    #endregion

    #region Properties (favourites)
    /// <summary>Exclusive toggle group of the favourite image type buttons (no selection required).</summary>
    public ToggleGroup<PlatformImageSet> FavouriteImageTypeGroup { get; } = new(requireSelection: false, autoSelectFirstEnabledItem: false);

    /// <summary>Whether any favourite button is currently selected (drives the star icon).</summary>
    public bool AnyFavouriteTypeButtonSelected
    {
        get => _anyFavouriteTypeButtonSelected;
        private set
        {
            if (_anyFavouriteTypeButtonSelected != value)
            {
                _anyFavouriteTypeButtonSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AnyFavouriteTypeButtonSelected)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    #endregion

    #region Constructor
    /// <summary>
    /// Initializes a new instance of the <see cref="ImageTypeControl"/> class.
    /// </summary>
    public ImageTypeControl()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        ApplyDisplayMode();
    }
    #endregion

    #region Subscribed events
    /// <summary>
    /// Resolves the shared services, wires the favourite listeners and applies the display mode once in the tree.
    /// </summary>
    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _sharedDataService ??= App.GetService<SharedDataService>();
        _appSettings ??= App.GetService<IOptions<AppSettings>>().Value;

        FavouriteImageTypeGroup.SelectionChanged += OnFavouriteSelectionChanged;
        _sharedDataService.SelectedPlatformChanged += OnSelectedPlatformChanged;
        _sharedDataService.SelectedImageSetChanged += OnSelectedImageSetChanged;
        _sharedDataService.ImageTypesFiltered.CollectionChanged += OnImageTypesFilteredChanged;
        _sharedDataService.FavouriteMediaTypesChanged += OnFavouriteMediaTypesChanged;

        ApplyDisplayMode();
    }

    /// <summary>Detaches every favourite handler so the control does not keep receiving updates once removed.</summary>
    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        FavouriteImageTypeGroup.SelectionChanged -= OnFavouriteSelectionChanged;

        if (_sharedDataService is not null)
        {
            _sharedDataService.SelectedPlatformChanged -= OnSelectedPlatformChanged;
            _sharedDataService.SelectedImageSetChanged -= OnSelectedImageSetChanged;
            _sharedDataService.ImageTypesFiltered.CollectionChanged -= OnImageTypesFilteredChanged;
            _sharedDataService.FavouriteMediaTypesChanged -= OnFavouriteMediaTypesChanged;
        }
    }

    /// <summary>
    /// Los tipos de media favoritos cambiaron (al aceptar la ventana de configuración): reconstruye los botones de
    /// tipos favoritos y sincroniza su selección con el set activo. Aplicación en caliente.
    /// </summary>
    private void OnFavouriteMediaTypesChanged(object? sender, EventArgs e)
    {
        BuildFavouriteButtons();
        SyncSelectionWithSharedData();
    }

    /// <summary>
    /// A favourite button was selected: resolve its image set against the filtered list and push it as the shared
    /// selected image set.
    /// </summary>
    private void OnFavouriteSelectionChanged(object? sender, EventArgs e)
    {
        var selectedSet = FavouriteImageTypeGroup.SelectedValue;
        if (selectedSet is null)
        {
            AnyFavouriteTypeButtonSelected = false;
            return;
        }

        var match = _sharedDataService.ImageTypesFiltered.FirstOrDefault(s => s.Type.Key == selectedSet.Type.Key);
        if (match != null)
        {
            _sharedDataService.SelectedImageSet = match;
        }

        AnyFavouriteTypeButtonSelected = FavouriteImageTypeGroup.Items.Any(b => b.IsSelected);
    }

    /// <summary>On a platform change the available image sets change, so the favourite buttons are rebuilt.</summary>
    private void OnSelectedPlatformChanged(object? sender, PlatformChangedEventArgs e) => BuildFavouriteButtons();

    /// <summary>Mirrors the shared selected image set onto the favourite button selection.</summary>
    private void OnSelectedImageSetChanged(object? sender, ImageSetChangedEventArgs e) => SyncSelectionWithSharedData();

    /// <summary>The filtered image type list changed, so the enabled state of the buttons may have changed.</summary>
    private void OnImageTypesFilteredChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateFavouriteButtonsEnabled();
    #endregion

    #region Methods (private)
    /// <summary>
    /// Refleja <see cref="DisplayMode"/> en la UI: en modo Full muestra los favoritos (que ocupan el resto de la fila,
    /// a la derecha del combo de ancho fijo) y quita el padding; en Selector oculta los favoritos y aplica el padding
    /// compacto. El ancho del combo es fijo en ambos modos (la columna que lo aloja no cambia de anchura).
    /// </summary>
    private void ApplyDisplayMode()
    {
        if (RootGrid is null)
            return;

        bool full = DisplayMode == ImageTypeDisplayMode.Full;

        RootGrid.Padding = full ? new Thickness(0) : new Thickness(16, 0, 20, 8);
        FavouritesHost.Visibility = full ? Visibility.Visible : Visibility.Collapsed;

        // Los botones de favoritos solo se construyen en modo Full y una vez resueltos los servicios (en Loaded).
        if (full && _sharedDataService is not null)
            BuildFavouriteButtons();
    }

    /// <summary>
    /// Builds the favourite toggle buttons from the configured favourites that exist in the current platform's
    /// image sets, then refreshes their enabled/selected state.
    /// </summary>
    private void BuildFavouriteButtons()
    {
        FavouriteImageTypeGroup.Clear();

        var imageSets = _sharedDataService.SelectedPlatform?.Images?.ImageSets;
        var favouriteImageTypes = _appSettings.ImageTypeControl.FavouriteImageTypes;

        if (imageSets != null && favouriteImageTypes != null)
        {
            foreach (var favouriteImageType in favouriteImageTypes)
            {
                var set = imageSets.FirstOrDefault(s => s.Type.Key == favouriteImageType.Key);
                if (set is null)
                {
                    continue;
                }

                FavouriteImageTypeGroup.Add(set, set.Type.Value);
            }
        }

        UpdateFavouriteButtonsEnabled();
    }

    /// <summary>
    /// Synchronizes the favourite button selection with the shared selected image set. If the selected image set
    /// is not one of the favourites, the group is left with no active selection.
    /// </summary>
    private void SyncSelectionWithSharedData()
    {
        var currentImageSetKey = _sharedDataService.SelectedImageSet?.Type.Key;

        var matchingFavourite = currentImageSetKey is null
            ? null
            : FavouriteImageTypeGroup.Items.FirstOrDefault(item => item.Value.Type.Key == currentImageSetKey);

        if (matchingFavourite is null)
        {
            FavouriteImageTypeGroup.ClearSelection();
            AnyFavouriteTypeButtonSelected = false;
            return;
        }

        FavouriteImageTypeGroup.Select(matchingFavourite);
        AnyFavouriteTypeButtonSelected = FavouriteImageTypeGroup.Items.Any(b => b.IsSelected);
    }

    /// <summary>
    /// Enables only the buttons whose image type is present in the filtered image type list; the rest are disabled
    /// (their type is filtered out of the list and cannot be selected). Then re-syncs the selection.
    /// </summary>
    private void UpdateFavouriteButtonsEnabled()
    {
        var filtered = _sharedDataService.ImageTypesFiltered;

        foreach (var button in FavouriteImageTypeGroup.Items)
        {
            button.IsEnabled = filtered.Any(s => s.Type.Key == button.Value.Type.Key);
        }

        SyncSelectionWithSharedData();
        AnyFavouriteTypeButtonSelected = FavouriteImageTypeGroup.Items.Any(b => b.IsSelected);
    }
    #endregion
}
