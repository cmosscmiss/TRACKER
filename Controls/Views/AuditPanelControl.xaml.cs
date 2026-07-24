using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MM4LB.Controls.ViewModels;
using MM4LB.Helpers;

namespace MM4LB.Controls.Views;

/// <summary>
/// Control autónomo y reutilizable de la auditoría de media: barra de herramientas (lanza el chequeo pidiendo
/// un Excel de LaunchBox), tabla de resultados y resumen en pills. Resuelve su propio
/// <see cref="AuditPanelViewModel"/> de DI (patrón de SearchStringsControl), de modo que cualquier contenedor
/// solo tiene que instanciarlo.
/// </summary>
public sealed partial class AuditPanelControl : UserControl
{
    public AuditPanelViewModel ViewModel { get; } = App.GetService<AuditPanelViewModel>();

    public AuditPanelControl()
    {
        InitializeComponent();

        // Attach/Detach acotan la escucha del tipo de media al tiempo en que el control está en el árbol visual
        // (el VM es transitorio; sin esto, con el filtro por tipo activo quedaría suscrito al descargarse).
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        // Cabeceras de columna localizadas (la Δ se deja como símbolo). Las columnas del DataGrid no aceptan {loc:Str}.
        DataGridLoc.Attach(dgAudit,
            ("GameTitle", LocKeys.Common_Game_Header),
            ("CategoryName", LocKeys.AuditPanel_Category_Header),
            ("ExcelCount", LocKeys.AuditPanel_LaunchBox_Header),
            ("Mm4lbCount", LocKeys.AuditPanel_MM4LB_Header),
            ("Status", LocKeys.AuditPanel_Status_Header));
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        ViewModel.SelectRowRequested += OnSelectRowRequested;
        ViewModel.Attach();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        ViewModel.SelectRowRequested -= OnSelectRowRequested;
        ViewModel.Detach();
    }

    /// <summary>
    /// El juego seleccionado en la app cambió: refleja la selección en la tabla (y hace scroll a la fila), o la
    /// limpia si el juego no tiene fila visible. Si la fila ya está seleccionada (p. ej. el propio clic que
    /// disparó el cambio), no hace nada, para evitar el SelectionChanged redundante.
    /// </summary>
    private void OnSelectRowRequested(object? sender, AuditRow? row)
    {
        if (Equals(dgAudit.SelectedItem, row)) { return; }

        dgAudit.SelectedItem = row;
        if (row != null) { dgAudit.ScrollIntoView(row, null); }
    }

    /// <summary>
    /// Ordena la tabla por la columna pulsada, alternando ascendente → descendente → sin orden, y limpiando el
    /// indicador de las demás columnas. Mismo patrón que GamesAuditControl.
    /// </summary>
    private void AuditDataGrid_Sorting(object? sender, DataGridColumnEventArgs e)
    {
        DataGridSortDirection? direction = DataGridSorting.CycleColumn((DataGrid)sender!, e.Column);

        bool? ascending = direction switch
        {
            DataGridSortDirection.Ascending => true,
            DataGridSortDirection.Descending => false,
            _ => (bool?)null
        };

        ViewModel.Sort(e.Column.Tag?.ToString(), ascending);
    }

    /// <summary>Al seleccionar una fila de la tabla, delega en el VM para fijar el juego seleccionado de la app.</summary>
    private void AuditDataGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is AuditRow row)
        {
            ViewModel.OnRowSelected(row);
        }
    }
}
