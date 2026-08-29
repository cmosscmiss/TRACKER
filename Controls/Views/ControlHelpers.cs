using System;
using System.Collections.Generic;
using CommunityToolkit.WinUI.UI.Controls;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.WinUI;
using Tracker.Contracts.Services;

namespace Tracker.Controls.Views;

/// <summary>
/// Restaura la configuración persistida de un view model de widget UNA SOLA VEZ por instancia. Los controles de
/// widget resuelven su VM por una DP y cargan su config al cargarse (<c>Loaded</c>) y al asignarse la DP; como el
/// VM es Singleton, sin esta guarda se re-cargaría en cada montaje. Extrae el patrón <c>EnsureConfigurationLoaded</c>
/// (<c>_configurationLoadedViewModel</c> + <c>ReferenceEquals</c> + <c>LoadConfig</c>) que estaba copiado en 8 controles.
/// </summary>
/// <typeparam name="T">Tipo concreto del view model del control (implementa <see cref="IWidgetViewModelBase"/>).</typeparam>
public sealed class ViewModelConfigGate<T> where T : class, IWidgetViewModelBase
{
    private T? _loaded;

    /// <summary>
    /// Carga la config de <paramref name="viewModel"/> si aún no se cargó para ESA instancia. No hace nada si el VM
    /// es null o si ya se cargó (misma referencia). Reasignar la DP a un VM distinto lo recarga automáticamente.
    /// </summary>
    public void Ensure(T? viewModel)
    {
        if (viewModel is null || ReferenceEquals(_loaded, viewModel))
        {
            return;
        }

        viewModel.LoadConfig();
        _loaded = viewModel;
    }
}

/// <summary>
/// Helpers compartidos para la ordenación de un <see cref="DataGrid"/> del CommunityToolkit desde el evento
/// <c>Sorting</c>. Extrae el ciclado de columnas (asc → desc → sin orden + limpieza de las demás) que estaba
/// duplicado en los handlers de GamesAudit/ImageAudit/AuditPanel; cada control conserva su llamada al VM.
/// </summary>
public static class DataGridSorting
{
    /// <summary>
    /// Limpia el indicador de orden de todas las columnas salvo la pulsada y cicla la de <paramref name="clicked"/>
    /// entre Ascending → Descending → sin orden. Devuelve la dirección resultante de la columna pulsada (o null si
    /// no tiene <c>Tag</c>, en cuyo caso no es ordenable y no se toca).
    /// </summary>
    public static DataGridSortDirection? CycleColumn(DataGrid grid, DataGridColumn clicked)
    {
        foreach (DataGridColumn column in grid.Columns)
        {
            if (column.Tag?.ToString() != clicked.Tag?.ToString())
            {
                column.SortDirection = null;
            }
        }

        if (clicked.Tag == null)
        {
            return null;
        }

        clicked.SortDirection = clicked.SortDirection == null
            ? DataGridSortDirection.Ascending
            : clicked.SortDirection == DataGridSortDirection.Ascending ? DataGridSortDirection.Descending : null;

        return clicked.SortDirection;
    }
}

/// <summary>
/// Glue compartido del <see cref="CartesianChart"/> de cobertura, duplicado entre PlatformDetailsControl y
/// StatsPlatformControl. Cubre las dos partes que WinUI no puede hacer por binding:
/// <list type="bullet">
/// <item>La propiedad <c>Sections</c> (línea de objetivo punteada) no admite x:Bind: su tipo genérico rompe la
/// binding compilada, así que se vuelca a mano desde el view model.</item>
/// <item>La velocidad de animación se fuerza a cero cuando solo se reposiciona el resaltado (cambio de
/// plataforma/juego con cache hit → instantáneo) y se restaura a la de por defecto cuando hay recálculo real.</item>
/// </list>
/// Guarda la velocidad por defecto la primera vez que se consulta.
/// </summary>
public sealed class CoverageChartGlue
{
    private TimeSpan? _defaultAnimationsSpeed;

    /// <summary>Vuelca las secciones (línea de objetivo) del view model en el chart. No-op si alguno es null.</summary>
    public void UpdateSections(CartesianChart? chart, IEnumerable<RectangularSection>? sections)
    {
        if (chart != null && sections != null)
        {
            chart.Sections = sections;
        }
    }

    /// <summary>
    /// Fija la velocidad de animación del chart: la de por defecto si hay recálculo real (<paramref name="animate"/>
    /// true), o cero para que el reposicionado del resaltado sea instantáneo. No-op si el chart es null.
    /// </summary>
    public void ApplyAnimation(CartesianChart? chart, bool animate)
    {
        if (chart is null)
        {
            return;
        }

        _defaultAnimationsSpeed ??= chart.AnimationsSpeed;
        chart.AnimationsSpeed = animate ? _defaultAnimationsSpeed.Value : TimeSpan.Zero;
    }
}
