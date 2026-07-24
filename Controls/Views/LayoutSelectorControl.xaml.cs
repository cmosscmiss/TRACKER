using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MM4LB.Controls.Templates;
using MM4LB.Controls.ViewModels;
using MM4LB.Models;

namespace MM4LB.Controls.Views;

/// <summary>
/// Control que muestra una serie de layouts tipo “Snap Layouts” al estilo Windows 11.
/// Gestiona la selección visual y notifica al ViewModel y a consumidores externos.
/// </summary>
public sealed partial class LayoutSelectorControl : UserControl
{
    #region Dependency Properties
    /// <summary>
    /// Obtiene o establece el ViewModel asociado al control.
    /// Esta propiedad permite enlazar el estado del selector con la lógica de la aplicación.
    /// </summary>
    public LayoutSelectorViewModel? ViewModel
    {
        get => (LayoutSelectorViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    /// <summary>
    /// DependencyProperty para exponer el ViewModel al sistema de bindings.
    /// </summary>
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register("ViewModel", typeof(LayoutSelectorViewModel), typeof(LayoutSelectorControl), new PropertyMetadata(null));
    #endregion

    #region Events
    /// <summary>
    /// Evento que se dispara cuando el usuario selecciona un layout.
    /// Proporciona el índice del layout seleccionado.
    /// </summary>
    public event EventHandler<int>? LayoutSelected;
    #endregion

    #region Constructor
    /// <summary>
    /// Inicializa el control, asigna los tipos e índices de cada item
    /// y conecta los eventos de ciclo de vida del control.
    /// </summary>
    public LayoutSelectorControl()
    {
        InitializeComponent();

        // Asignación de índices a cada item.
        Item0.Index = 0;
        Item1.Index = 1;
        Item2.Index = 2;
        Item3.Index = 3;
        Item4.Index = 4;
        Item5.Index = 5;
        Item6.Index = 6;
        Item7.Index = 7;
        Item8.Index = 8;
        Item9.Index = 9;

        // Eventos de ciclo de vida del control.
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }
    #endregion

    #region Subscribed Events
    /// <summary>
    /// Evento ejecutado cuando el control se carga en el árbol visual.
    /// Construye los layouts y suscribe los eventos de los items.
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        ViewModel?.LoadConfig();

        BuildLayouts();

        // Suscribe cada item al evento Clicked.
        foreach (var child in RootPanel.Children)
            if (child is LayoutItemControl item)
                item.OnLayoutItemClicked += OnItemClicked;

        int index = ViewModel!.SelectedLayout.Index;

        foreach (var child in RootPanel.Children)
            if (child is LayoutItemControl item)
                item.SetSelected(item.Index == index);
    }

    /// <summary>
    /// Evento ejecutado cuando el control se descarga del árbol visual.
    /// Desuscribe los eventos de los items para evitar fugas de memoria.
    /// </summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        foreach (var child in RootPanel.Children)
            if (child is LayoutItemControl item)
                item.OnLayoutItemClicked -= OnItemClicked;
    }

    /// <summary>
    /// Maneja la selección de un layout cuando el usuario hace clic en un item.
    /// Actualiza el ViewModel, la UI y notifica a los suscriptores externos.
    /// </summary>
    private void OnItemClicked(object? sender, int index)
    {
        ViewModel!.SelectedLayout = Layouts.Get(index);

        // Marca visualmente el item seleccionado.
        foreach (var child in RootPanel.Children)
            if (child is LayoutItemControl item)
                item.SetSelected(item.Index == index);

        // Notifica a quien escuche el evento.
        LayoutSelected?.Invoke(this, index);
    }
    #endregion

    #region Methods (private)
    /// <summary>
    /// Construye todos los layouts disponibles dentro de cada host Grid.
    /// </summary>
    private void BuildLayouts()
    {
        foreach (var child in RootPanel.Children)
        {
            if (child is not LayoutItemControl item)
                continue;

            LayoutBuilder.Build(item.GetLayoutHost(), item.Index);
        }
    }
    #endregion
}
