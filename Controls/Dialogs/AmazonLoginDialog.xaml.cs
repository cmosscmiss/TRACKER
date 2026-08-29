using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Tracker.Controls.Dialogs;

/// <summary>
/// Contenido del diálogo de inicio de sesión de Amazon: email + contraseña. El botón primario (Iniciar sesión) se
/// habilita cuando ambos campos tienen valor. Las credenciales se usan solo para un autorrelleno puntual (no se guardan).
/// </summary>
public sealed partial class AmazonLoginDialog : Page, IAppDialogPrimaryGate
{
    public AmazonLoginDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => EmailBox.Focus(FocusState.Programmatic);
    }

    /// <summary>Email introducido (recortado).</summary>
    public string Email => EmailBox.Text?.Trim() ?? string.Empty;

    /// <summary>Contraseña introducida.</summary>
    public string Password => PasswordBox.Password ?? string.Empty;

    /// <inheritdoc/>
    public bool IsPrimaryEnabled => !string.IsNullOrWhiteSpace(EmailBox.Text) && !string.IsNullOrEmpty(PasswordBox.Password);

    /// <inheritdoc/>
    public event EventHandler? PrimaryEnabledChanged;

    private void OnEmailChanged(object sender, TextChangedEventArgs e) => PrimaryEnabledChanged?.Invoke(this, EventArgs.Empty);

    private void OnPasswordChanged(object sender, RoutedEventArgs e) => PrimaryEnabledChanged?.Invoke(this, EventArgs.Empty);
}
