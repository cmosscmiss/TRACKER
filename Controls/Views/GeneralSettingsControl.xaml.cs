using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MM4LB.Services;
using MM4LB.ViewModels;

namespace MM4LB.Controls.Views;

/// <summary>
/// Página de la categoría "General" de la ventana de configuración. Sus controles se enlazan al staging del
/// <see cref="SettingsDialogViewModel"/> (DataContext heredado del diálogo).
/// </summary>
public sealed partial class GeneralSettingsControl : UserControl
{
    public GeneralSettingsControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>
    /// Preselecciona los combos por CÓDIGO (no por binding): el ComboBox de WinUI no aplica de forma fiable el
    /// <c>SelectedItem</c> enlazado al cargar (salía vacío al reabrir). La asignación se difiere al dispatcher para
    /// que el <c>ItemsSource</c> ya esté resuelto; el write-back lo hacen los handlers de SelectionChanged.
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (DataContext is not SettingsDialogViewModel vm)
                return;

            LanguageCombo.SelectedItem = vm.SelectedLanguageOption;
        });
    }

    /// <summary>Vuelca el idioma seleccionado al staging del VM.</summary>
    private void OnLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is SettingsDialogViewModel vm && LanguageCombo.SelectedItem is LocalizationService.LanguageOption option)
            vm.SelectedLanguageOption = option;
    }
}
