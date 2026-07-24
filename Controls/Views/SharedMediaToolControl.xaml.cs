using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MM4LB.Controls.ViewModels;
using MM4LB.Helpers;

namespace MM4LB.Controls.Views;

/// <summary>
/// Herramienta "Shared media" del widget Tools: escanea los medios (imagen y vídeo de juego) de la plataforma
/// seleccionada que emparejan con al menos 2 juegos y los muestra en tabla (una fila por pareja juego↔media) o grid
/// de miniaturas. Resuelve su propio <see cref="SharedMediaToolViewModel"/> (singleton) de DI, patrón de
/// <see cref="OrphanToolControl"/>; Attach/Detach acotan la escucha de cambios al tiempo en el árbol visual.
/// </summary>
public sealed partial class SharedMediaToolControl : UserControl
{
    public SharedMediaToolViewModel ViewModel { get; } = App.GetService<SharedMediaToolViewModel>();

    public SharedMediaToolControl()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        // Cabeceras de columna localizadas (las columnas del DataGrid no aceptan {loc:Str}).
        DataGridLoc.Attach(dgShared,
            ("Game", LocKeys.Common_Game_Header),
            ("Type", LocKeys.Common_Type_Header),
            ("File", LocKeys.Common_File_Header),
            ("Region", LocKeys.Common_Region_Header));
    }

    private void OnLoaded(object? sender, RoutedEventArgs e) => ViewModel.Attach();

    private void OnUnloaded(object? sender, RoutedEventArgs e) => ViewModel.Detach();

    /// <summary>
    /// Ordena la tabla por la columna pulsada, alternando ascendente → descendente → sin orden, y limpiando el
    /// indicador de las demás columnas. Mismo patrón que OrphanToolControl.
    /// </summary>
    private void SharedDataGrid_Sorting(object? sender, DataGridColumnEventArgs e)
    {
        DataGridSortDirection? direction = DataGridSorting.CycleColumn((DataGrid)sender!, e.Column);

        bool? ascending = direction switch
        {
            DataGridSortDirection.Ascending => true,
            DataGridSortDirection.Descending => false,
            _ => (bool?)null
        };

        ViewModel.SortRows(e.Column.Tag?.ToString(), ascending);
    }
}
