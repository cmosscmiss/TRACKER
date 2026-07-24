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

    #region Methods private
    /// <summary>
    /// Devuelve el elemento visual que debe aparecer durante la transición
    /// entre el modo ComboBox y el modo ListView.
    /// </summary>
    /// <param name="behavesAsList"> Indica si el estado final de la transición es el modo lista. </param>
    /// <returns> El elemento visual que debe entrar en pantalla. </returns>
    private FrameworkElement GetIncomingPlatformElement(bool behavesAsList)
    {
        return behavesAsList ? PlatformListView : PlatformListComboBox;
    }

    /// <summary>
    /// Devuelve el elemento visual que debe desaparecer durante la transición
    /// entre el modo ComboBox y el modo ListView.
    /// </summary>
    /// <param name="behavesAsList"> Indica si el estado final de la transición es el modo lista. </param>
    /// <returns> El elemento visual que debe salir de pantalla. </returns>
    private FrameworkElement GetOutgoingPlatformElement(bool behavesAsList)
    {
        return behavesAsList ? PlatformListComboBox : PlatformListView;
    }
    #endregion

    #region Methods public
    /// <summary>
    /// Aplica inmediatamente el modo visual final del control, sin animación.
    /// Se usa para restaurar el estado inicial del control y para dejar
    /// los elementos visuales en un estado coherente al finalizar una transición.
    /// </summary>
    /// <param name="behavesAsList"> Indica si el control debe mostrarse como lista. </param>
    public void ApplyPlatformDisplayMode(bool behavesAsList)
    {
        if (behavesAsList)
        {
            PlatformListComboBox.Visibility = Visibility.Collapsed;
            PlatformListComboBox.Opacity = 0;

            PlatformListView.Visibility = Visibility.Visible;
            PlatformListView.Opacity = 1;
        }
        else
        {
            PlatformListComboBox.Visibility = Visibility.Visible;
            PlatformListComboBox.Opacity = 1;

            PlatformListView.Visibility = Visibility.Collapsed;
            PlatformListView.Opacity = 0;
        }
    }

    /// <summary>
    /// Prepara los elementos visuales del control para una transición animada.
    /// El elemento entrante se hace visible con opacidad inicial cero.
    /// El elemento saliente se mantiene visible con opacidad completa para permitir
    /// que desaparezca progresivamente mediante fade-out.
    /// </summary>
    /// <param name="behavesAsList"> Indica si el estado final de la transición es el modo lista. </param>
    public void PreparePlatformListDisplayTransition(bool behavesAsList)
    {
        var incoming = GetIncomingPlatformElement(behavesAsList);
        var outgoing = GetOutgoingPlatformElement(behavesAsList);

        incoming.Visibility = Visibility.Visible;
        incoming.Opacity = 0;

        outgoing.Visibility = Visibility.Visible;
        outgoing.Opacity = 1;
    }

    /// <summary>
    /// Ejecuta una transición visual mediante fade cruzado entre el ComboBox
    /// y la ListView del control.
    /// </summary>
    /// <param name="behavesAsList"> Indica si el estado final de la transición es el modo lista. </param>
    /// <param name="onCompleted"> Acción opcional que se ejecuta al finalizar la animación. </param>
    public void AnimatePlatformListDisplayTransition(bool behavesAsList, Action? onCompleted = null)
    {
        var incoming = GetIncomingPlatformElement(behavesAsList);
        var outgoing = GetOutgoingPlatformElement(behavesAsList);

        var animations = new[]
        {
            AnimationService.CreateOpacityAnimation(outgoing, outgoing.Opacity, 0, 300),
            AnimationService.CreateOpacityAnimation(incoming, incoming.Opacity, 1, 300)
        };

        AnimationService.RunAnimations(animations, () =>
        {
            ApplyPlatformDisplayMode(behavesAsList);
            onCompleted?.Invoke();
        });
    }
    #endregion
}