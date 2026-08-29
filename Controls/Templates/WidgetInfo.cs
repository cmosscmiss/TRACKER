using Tracker.Controls.ViewModels;

namespace Tracker.Controls.Templates;

/// <summary>
/// Representa la información lógica y visual mínima necesaria para identificar
/// y renderizar un widget fuera de su contenedor visual real.
/// 
/// Esta clase permite compartir datos de un widget sin exponer ni reutilizar
/// el control visual asociado, manteniendo separado el estado lógico del árbol
/// visual de WinUI.
/// </summary>
public sealed class WidgetInfo
{
    /// <summary>
    /// Crea una nueva instancia de <see cref="WidgetInfo"/>.
    /// </summary>
    /// <param name="viewModel">
    /// ViewModel asociado al widget. Contiene el estado funcional del widget,
    /// incluyendo su <c>SlotIndex</c>.
    /// </param>
    /// <param name="title">
    /// Título descriptivo del widget.
    /// </param>
    /// <param name="iconName">
    /// Nombre base del icono asociado al widget. Se utiliza para resolver
    /// tanto el icono activo como el icono inactivo según el tema actual.
    /// </param>
    public WidgetInfo(WidgetViewModelBase viewModel, string title, string iconName)
    {
        ViewModel = viewModel;
        Title = title;
        IconName = iconName;
    }

    /// <summary>
    /// ViewModel asociado al widget.
    /// </summary>
    public WidgetViewModelBase ViewModel { get; }

    /// <summary>
    /// Título descriptivo del widget.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Nombre base del icono del widget.
    /// </summary>
    public string IconName { get; }
}