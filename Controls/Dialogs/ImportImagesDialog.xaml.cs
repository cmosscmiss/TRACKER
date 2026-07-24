using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MM4LB.Controls.Dialogs;

public sealed partial class ImportImagesDialog : Page
{
    #region Fields
    // Índice seleccionado por defecto: 0 = Discard (reemplazar), 1 = Keep (conservar). Lo lee el x:Bind del XAML.
#pragma warning disable CS0414 // usado por el x:Bind (código generado), no visible para el análisis de C#.
    private readonly int _collectionImagesIndex = 0;
#pragma warning restore CS0414
    #endregion

    #region Constructors
    public ImportImagesDialog()
    {
        InitializeComponent();
    }
    #endregion

    #region Subscribed events
    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        if (HelpTip != null)
            HelpTip.IsOpen = true;
    }
    #endregion

    #region Methods
    /// <summary>
    /// Devuelve la selección del usuario: true si se conservan las imágenes existentes (Keep), false si se
    /// reemplazan (Discard).
    /// </summary>
    public bool KeepCollectionImages() => rbCollectionImages.SelectedIndex == 1;
    #endregion
}
