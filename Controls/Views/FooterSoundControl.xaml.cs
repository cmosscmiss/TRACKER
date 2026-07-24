using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MM4LB.Controls.ViewModels;

namespace MM4LB.Controls.Views;

/// <summary>
/// Control de sonido del footer (estilo Windows 11): un botón que refleja el estado del sonido (con ondas / silenciado)
/// y despliega un overlay con el slider de volumen y un botón de mute. Opera sobre el volumen de vídeo global, que vive
/// en <see cref="GameImagesDashboardViewModel"/> (mismo valor que el slider de ajustes); por eso el ViewModel es de ese
/// tipo.
/// </summary>
public sealed partial class FooterSoundControl : UserControl
{
    #region Dependency Properties
    /// <summary>ViewModel que expone el volumen de vídeo y el estado de silencio.</summary>
    public GameImagesDashboardViewModel? ViewModel
    {
        get => (GameImagesDashboardViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    /// <summary>DependencyProperty asociada a <see cref="ViewModel"/>.</summary>
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(nameof(ViewModel), typeof(GameImagesDashboardViewModel), typeof(FooterSoundControl), new PropertyMetadata(null));
    #endregion

    #region Constructor
    public FooterSoundControl()
    {
        InitializeComponent();
    }
    #endregion

    #region Subscribed events
    /// <summary>Alterna el silencio global, conservando el nivel del slider.</summary>
    private void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
            ViewModel.IsMuted = !ViewModel.IsMuted;
    }
    #endregion
}
