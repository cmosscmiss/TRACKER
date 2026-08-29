using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tracker.ViewModels;

namespace Tracker.Controls.Views;

public sealed partial class SettingsControl : UserControl
{
    /// <summary>
    /// Fuente tipada para los x:Bind del XAML: es el DataContext heredado del árbol (el MainWindowViewModel).
    /// x:Bind resuelve contra el code-behind, no contra el DataContext, así que se expone aquí y se refresca
    /// cuando el DataContext se asigna (DataContextChanged → Bindings.Update()).
    /// </summary>
    public MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    public SettingsControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args) => Bindings.Update();
}
