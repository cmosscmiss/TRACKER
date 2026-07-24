using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MM4LB.Controls.ViewModels;
using MM4LB.Helpers;

namespace MM4LB.Controls.Views;

/// <summary>
/// Herramienta "Orphan media" del widget Tools: escanea los medios (imagen y vídeo de juego) de la plataforma
/// seleccionada que no emparejan con ningún juego y los muestra en tabla o grid de miniaturas. Resuelve su propio
/// <see cref="OrphanToolViewModel"/> (singleton) de DI, patrón de AuditPanelControl; Attach/Detach acotan la escucha
/// de cambios de plataforma al tiempo en que el control está en el árbol visual.
/// </summary>
public sealed partial class OrphanToolControl : UserControl
{
    public OrphanToolViewModel ViewModel { get; } = App.GetService<OrphanToolViewModel>();

    public OrphanToolControl()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        // Cabeceras de columna localizadas (las columnas del DataGrid no aceptan {loc:Str}).
        DataGridLoc.Attach(dgOrphans,
            ("Type", LocKeys.Common_Type_Header),
            ("Name", LocKeys.Common_File_Header),
            ("Region", LocKeys.Common_Region_Header));
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        ViewModel.Attach();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e) => ViewModel.Detach();

    /// <summary>
    /// Ordena la tabla por la columna pulsada, alternando ascendente → descendente → sin orden, y limpiando el
    /// indicador de las demás columnas. Mismo patrón que AuditPanelControl/GamesAuditControl.
    /// </summary>
    private void OrphanDataGrid_Sorting(object? sender, DataGridColumnEventArgs e)
    {
        DataGridSortDirection? direction = DataGridSorting.CycleColumn((DataGrid)sender!, e.Column);

        bool? ascending = direction switch
        {
            DataGridSortDirection.Ascending => true,
            DataGridSortDirection.Descending => false,
            _ => (bool?)null
        };

        ViewModel.SortShown(e.Column.Tag?.ToString(), ascending);
    }
}
