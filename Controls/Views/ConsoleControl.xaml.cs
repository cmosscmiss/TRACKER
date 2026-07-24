using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MM4LB.Controls.ViewModels;

namespace MM4LB.Controls.Views;

/// <summary>
/// User control that displays the application logging console.
/// 
/// The control is responsible only for rendering the console UI. Its data and state
/// are provided through the <see cref="ViewModel"/> dependency property, allowing the
/// control to be reused and bound from XAML.
/// </summary>
public sealed partial class ConsoleControl : UserControl
{
    #region Dependency Properties
    /// <summary>
    /// Gets or sets the view model used by the console control.
    /// 
    /// The view model exposes the log entries and any additional console-related
    /// information required by the XAML view.
    /// </summary>
    public ConsoleViewModel? ViewModel
    {
        get => (ConsoleViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    /// <summary>
    /// Identifies the <see cref="ViewModel"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(nameof(ViewModel), typeof(ConsoleViewModel), typeof(ConsoleControl), new PropertyMetadata(null));
    #endregion

    #region Constructor
    /// <summary>
    /// Initializes a new instance of the <see cref="ConsoleControl"/> class.
    /// </summary>
    public ConsoleControl()
    {
        InitializeComponent();
    }
    #endregion
}