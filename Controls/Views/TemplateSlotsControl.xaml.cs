using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using MM4LB.Services;

namespace MM4LB.Controls.Views;

/// <summary>De dónde saca sus items el <see cref="TemplateSlotsControl"/>.</summary>
public enum TemplateSlotsSource
{
    /// <summary>Los 3 slots de USUARIO (con placeholders vacíos), para el diálogo de grabar.</summary>
    UserSlots,

    /// <summary>Todos los templates disponibles (app + usuario ocupados), para el selector de la toolbar.</summary>
    AllTemplates
}

/// <summary>
/// Componente visual de templates en tarjetas (reutilizado por el diálogo de grabar y por el selector de la toolbar).
/// Según <see cref="Source"/>:
/// - <see cref="TemplateSlotsSource.UserSlots"/>: los 3 slots de usuario (con placeholder si están vacíos).
/// - <see cref="TemplateSlotsSource.AllTemplates"/>: los de app (built-in, con candado) + los de usuario ocupados.
/// Según <see cref="SelectionMode"/>:
/// - true (grabar): al pulsar un slot se selecciona (marco de acento); expone <see cref="SelectedSlot"/> y dispara <see cref="SelectionChanged"/>.
/// - false (cargar): al pulsar un template se dispara <see cref="TemplateActivated"/> con la ruta de su JSON.
/// </summary>
public sealed partial class TemplateSlotsControl : UserControl
{
    /// <summary>Item de una tarjeta para el binding.</summary>
    public sealed partial class SlotItem : ObservableObject
    {
        public int Slot { get; init; }
        public bool Occupied { get; init; }
        public string Name { get; init; } = string.Empty;
        public string JsonPath { get; init; } = string.Empty;
        public bool IsBuiltIn { get; init; }
        public BitmapImage? Image { get; init; }

        /// <summary>Texto bajo la miniatura: el nombre si está ocupado, "Empty" si está vacío.</summary>
        public string DisplayName => Occupied ? Name : "Empty";
    }

    private readonly TemplateService _templateService;

    public TemplateSlotsControl()
    {
        InitializeComponent();
        _templateService = App.GetService<TemplateService>();
        Loaded += async (_, _) => await RefreshAsync();
    }

    #region Dependency properties
    public static readonly DependencyProperty SelectionModeProperty =
        DependencyProperty.Register(nameof(SelectionMode), typeof(bool), typeof(TemplateSlotsControl), new PropertyMetadata(false));

    /// <summary>true = seleccionar un slot (grabar); false = activar un template (cargar).</summary>
    public bool SelectionMode
    {
        get => (bool)GetValue(SelectionModeProperty);
        set => SetValue(SelectionModeProperty, value);
    }

    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(TemplateSlotsSource), typeof(TemplateSlotsControl), new PropertyMetadata(TemplateSlotsSource.UserSlots));

    /// <summary>De dónde se sacan los items (slots de usuario para grabar, o todos los templates para cargar).</summary>
    public TemplateSlotsSource Source
    {
        get => (TemplateSlotsSource)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }
    #endregion

    /// <summary>Slot seleccionado (1..N) en modo selección, o -1 si ninguno.</summary>
    public int SelectedSlot { get; private set; } = -1;

    /// <summary>Nombre del slot seleccionado si está ocupado (para prellenar al sobreescribir); vacío si no.</summary>
    public string SelectedSlotName
    {
        get
        {
            if (Items.ItemsSource is IEnumerable<SlotItem> items)
                foreach (SlotItem s in items)
                    if (s.Slot == SelectedSlot && s.Occupied)
                        return s.Name;
            return string.Empty;
        }
    }

    /// <summary>Cambia la selección (modo selección).</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Se activó un template (modo carga); el argumento es la ruta de su JSON.</summary>
    public event EventHandler<string>? TemplateActivated;

    /// <summary>Recarga los items y sus miniaturas. La llaman el diálogo/toolbar al abrir y tras grabar.</summary>
    public async Task RefreshAsync()
    {
        // El GridView usa el estilo de item del dashboard (hover/pressed/selección con acento). En modo grabar se usa
        // su selección (Single); en modo cargar, ItemClick sin selección persistente.
        Items.SelectionMode = SelectionMode ? ListViewSelectionMode.Single : ListViewSelectionMode.None;
        Items.IsItemClickEnabled = !SelectionMode;

        var items = new List<SlotItem>();

        if (Source == TemplateSlotsSource.AllTemplates)
        {
            foreach (TemplateService.TemplateEntry t in _templateService.GetAllTemplates())
                items.Add(new SlotItem
                {
                    Slot = t.UserSlot,
                    Occupied = true,
                    Name = t.Name,
                    JsonPath = t.JsonPath,
                    IsBuiltIn = t.IsBuiltIn,
                    Image = await LoadThumbnailAsync(t.ImagePath)
                });
        }
        else
        {
            foreach (TemplateService.SlotInfo info in _templateService.GetUserSlots())
                items.Add(new SlotItem
                {
                    Slot = info.Slot,
                    Occupied = info.Occupied,
                    Name = info.Name,
                    Image = await LoadThumbnailAsync(info.ImagePath)
                });
        }

        SelectedSlot = -1;
        Items.ItemsSource = items;
    }

    /// <summary>Modo grabar: la selección del GridView fija el slot elegido.</summary>
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!SelectionMode)
            return;

        SelectedSlot = Items.SelectedItem is SlotItem item ? item.Slot : -1;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Modo cargar: al pulsar un template ocupado se activa (se carga su JSON).</summary>
    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SlotItem { Occupied: true, JsonPath: { Length: > 0 } jsonPath })
            TemplateActivated?.Invoke(this, jsonPath);
    }

    /// <summary>Carga la miniatura desde un stream (evita problemas de rutas de fichero en apps sin empaquetar).</summary>
    private static async Task<BitmapImage?> LoadThumbnailAsync(string imagePath)
    {
        if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            return null;

        try
        {
            var bitmap = new BitmapImage { DecodePixelWidth = 440 };
            using FileStream fileStream = File.OpenRead(imagePath);
            await bitmap.SetSourceAsync(fileStream.AsRandomAccessStream());
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
