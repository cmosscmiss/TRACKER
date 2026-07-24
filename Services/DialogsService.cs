using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MM4LB.Controls.Dialogs;
using MM4LB.Enums;
using MM4LB.Helpers;
using MM4LB.Models;
using System;
using System.Threading.Tasks;

namespace MM4LB.Services;

/// <summary>
/// Service exposing the different dialogs of the application, con el estilo propio de la app
/// (<see cref="AppDialog"/>, mostrado en un Popup sobre la ventana activa).
/// </summary>
public class DialogsService
{
    #region Attributes
    private readonly AppSettings _appSettings;
    private readonly PersistAndRestoreService _persistAndRestoreService;
    #endregion

    #region Constructors
    public DialogsService(IOptions<AppSettings> appSettings, PersistAndRestoreService persistAndRestoreService)
    {
        _appSettings = appSettings?.Value ?? throw new ArgumentNullException(nameof(appSettings));
        _persistAndRestoreService = persistAndRestoreService;
    }
    #endregion

    /// <summary>Texto localizado (o la clave si no hay servicio). Los diálogos se crean al abrir → idioma actual.</summary>
    private static string L(string key) => LocalizationService.Instance?[key] ?? key;

    /// <summary>
    /// Confirmación de borrado de un medio. Si el setting
    /// <see cref="AppSettings.GeneralSettings.PromptBeforeDeleteImage"/> está apagado, devuelve true sin mostrar
    /// nada. Si está encendido, muestra el diálogo con un check que permite apagarlo: al confirmar, persiste el
    /// nuevo valor del check. Devuelve true si el usuario confirmó el borrado.
    /// </summary>
    public async Task<bool> ConfirmImageDeletionAsync(XamlRoot xamlRoot, string message)
    {
        if (!_appSettings.General.PromptBeforeDeleteImage)
            return true;

        DeleteConfirmDialog content = new(message, _appSettings.General.PromptBeforeDeleteImage);
        AppDialog dialog = new();
        AppDialogResult result = await dialog.ShowAsync(xamlRoot, L(LocKeys.DialogsService_DeleteMedia_Title), content, L(LocKeys.Common_Delete_Label), null, L(LocKeys.Common_Cancel_Label));

        if (result != AppDialogResult.Primary)
            return false;

        if (content.AskBeforeDeleting != _appSettings.General.PromptBeforeDeleteImage)
        {
            _appSettings.General.PromptBeforeDeleteImage = content.AskBeforeDeleting;
            _persistAndRestoreService.PersistData();
        }

        return true;
    }

    /// <summary>
    /// Diálogo de confirmación: devuelve true si el usuario pulsó el botón primario.
    /// </summary>
    public async Task<bool> ConfirmAsync(XamlRoot xamlRoot, string title, string message, string primaryText, string closeText)
    {
        AppDialog dialog = new();
        AppDialogResult result = await dialog.ShowAsync(xamlRoot, title, BuildText(message), primaryText, null, closeText);
        return result == AppDialogResult.Primary;
    }

    /// <summary>
    /// Diálogo informativo de un solo botón (p. ej. errores).
    /// </summary>
    public async Task AlertAsync(XamlRoot xamlRoot, string title, string message, string closeText)
    {
        AppDialog dialog = new();
        await dialog.ShowAsync(xamlRoot, title, BuildText(message), null, null, closeText);
    }


    /// <summary>
    /// Muestra el diálogo para soltar una imagen sobre el panel de plataforma: el usuario elige el tipo de
    /// imagen de plataforma y si las existentes de ese tipo se conservan (Keep) o se reemplazan (Discard).
    /// Devuelve la elección, o <c>null</c> si canceló o no eligió tipo.
    /// </summary>
    public async Task<(MediaType Type, bool Discard)?> ShowPlatformImageDropAsync(XamlRoot xamlRoot)
    {
        PlatformImageDropDialog options = new();
        AppDialog dialog = new();
        AppDialogResult result = await dialog.ShowAsync(xamlRoot, L(LocKeys.DialogsService_AddPlatformImage_Title), options, L(LocKeys.Common_Add_Label), null, L(LocKeys.Common_Cancel_Label));

        if (result != AppDialogResult.Primary || options.SelectedType == null)
            return null;

        return (options.SelectedType, options.Discard);
    }

    /// <summary>Texto del diálogo como TextBlock con ajuste de línea (el color lo hereda de la tarjeta).</summary>
    private static TextBlock BuildText(string message) => new()
    {
        Text = message ?? string.Empty,
        TextWrapping = TextWrapping.Wrap
    };
}
