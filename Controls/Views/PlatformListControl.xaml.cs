using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MM4LB.Controls.ViewModels;
using MM4LB.Services;

namespace MM4LB.Controls.Views;

/// <summary>
/// Control de usuario encargado de mostrar y gestionar la selección de plataformas.
/// El control puede comportarse visualmente de dos formas:
/// como una lista completa de plataformas mediante <see cref="ListView"/>,
/// o como un selector compacto mediante <see cref="ComboBox"/>.
/// Expone su <see cref="PlatformListViewModel"/> mediante una <see cref="DependencyProperty"/>
/// para permitir que la ventana principal u otros contenedores puedan inyectar el ViewModel
/// desde el exterior.
/// </summary>
public sealed partial class PlatformListControl : UserControl
{
    #region Dependency Properties
    /// <summary>
    /// ViewModel asociado al control.
    /// Proporciona acceso a la colección de plataformas, a la plataforma seleccionada
    /// y al estado visual que determina si el control debe mostrarse como lista
    /// o como selector compacto.
    /// </summary>
    public PlatformListViewModel? ViewModel
    {
        get => (PlatformListViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    /// <summary>
    /// DependencyProperty que permite enlazar el <see cref="ViewModel"/>
    /// desde el exterior del control.
    /// </summary>
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(nameof(ViewModel), typeof(PlatformListViewModel), typeof(PlatformListControl), new PropertyMetadata(null));
    #endregion

    #region Constructor
    /// <summary>
    /// Inicializa una nueva instancia de <see cref="PlatformListControl"/>
    /// y carga su contenido XAML asociado.
    /// </summary>
    public PlatformListControl()
    {
        InitializeComponent();

        Loaded += OnLoaded;
    }
    #endregion

    #region Subscribed Events
    /// <summary>
    /// Gestiona la carga del control en el árbol visual.
    /// Carga la configuración asociada al ViewModel para restaurar
    /// el estado visual guardado del control.
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        if (ViewModel is null)
            return;

        ViewModel.LoadConfig();
    }
    #endregion

}