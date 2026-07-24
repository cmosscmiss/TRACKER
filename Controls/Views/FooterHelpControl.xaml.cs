using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MM4LB.Services;

namespace MM4LB.Controls.Views;

/// <summary>
/// Botón de ayuda del footer: alterna en caliente el toggle global de ayuda
/// (<see cref="SharedDataService.HelpTooltipsEnabled"/>), que gobierna tooltips y paneles de ayuda de toda la app.
/// Se enlaza directamente al <see cref="SharedDataService"/> (observable) resuelto por DI.
/// </summary>
public sealed partial class FooterHelpControl : UserControl
{
    /// <summary>Estado compartido (observable) que expone y alterna la ayuda. Fijado antes de InitializeComponent para x:Bind.</summary>
    public SharedDataService Shared { get; }

    public FooterHelpControl()
    {
        Shared = App.GetService<SharedDataService>();
        InitializeComponent();
    }

    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        Shared.HelpTooltipsEnabled = !Shared.HelpTooltipsEnabled;
    }
}
