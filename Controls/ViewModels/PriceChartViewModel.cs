using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Options;
using MM4LB.Models;
using MM4LB.Services;
using static MM4LB.Services.SharedDataService;

namespace MM4LB.Controls.ViewModels;

/// <summary>
/// ViewModel del widget de gráfica de precios: expone la evolución del precio del producto seleccionado
/// (<see cref="SharedDataService.SelectedProduct"/>) como series listas para <c>ChartTypeSelectorControl</c>
/// (<see cref="Values"/> = precios, <see cref="Labels"/> = fechas). Se recalcula al cambiar de producto y al
/// registrarse un precio nuevo (evento <see cref="Product.PriceRecorded"/>, p. ej. desde el scheduler).
/// </summary>
public partial class PriceChartViewModel : WidgetViewModelBase
{
    #region Attributes
    private Product? _product;
    #endregion

    #region Properties
    /// <summary>Precios del histórico, en orden cronológico (eje Y de la gráfica).</summary>
    public IReadOnlyList<double> Values { get; private set; } = Array.Empty<double>();

    /// <summary>Fechas del histórico formateadas (eje X de la gráfica).</summary>
    public IReadOnlyList<string> Labels { get; private set; } = Array.Empty<string>();

    /// <summary>Símbolo de moneda a añadir a los valores (del mejor precio / primera tienda), o vacío.</summary>
    public string ValueSuffix { get; private set; } = string.Empty;
    #endregion

    #region Constructor
    public PriceChartViewModel(SharedDataService sharedDataService, IOptions<AppSettings> appSettings)
        : base(sharedDataService, appSettings)
    {
        SharedDataService.SelectedProductChanged += OnSelectedProductChanged;
        Bind(SharedDataService.SelectedProduct);
    }
    #endregion

    #region Subscribed events
    private void OnSelectedProductChanged(object? sender, ProductChangedEventArgs e) => Bind(e.NewProduct);

    private void OnPriceRecorded(object? sender, EventArgs e) => Recompute();
    #endregion

    #region Methods (private)
    private void Bind(Product? product)
    {
        if (_product is not null)
            _product.PriceRecorded -= OnPriceRecorded;

        _product = product;

        if (_product is not null)
            _product.PriceRecorded += OnPriceRecorded;

        Recompute();
    }

    private void Recompute()
    {
        List<PricePoint>? history = _product?.PriceHistory;
        if (history is null || history.Count == 0)
        {
            Values = Array.Empty<double>();
            Labels = Array.Empty<string>();
            ValueSuffix = string.Empty;
        }
        else
        {
            Values = history.Select(point => (double)point.Price).ToList();
            Labels = history.Select(point => point.Timestamp.ToLocalTime().ToString("dd/MM HH:mm", CultureInfo.CurrentCulture)).ToList();
            ValueSuffix = _product?.BestStore?.Currency ?? _product?.Stores.FirstOrDefault()?.Currency ?? string.Empty;
        }

        OnPropertyChanged(nameof(Values));
        OnPropertyChanged(nameof(Labels));
        OnPropertyChanged(nameof(ValueSuffix));
    }
    #endregion

    #region Methods (public)
    public override void Dispose()
    {
        SharedDataService.SelectedProductChanged -= OnSelectedProductChanged;
        if (_product is not null)
            _product.PriceRecorded -= OnPriceRecorded;
    }

    public override void LoadConfig()
    {
    }

    public override void SaveConfig()
    {
    }
    #endregion
}
