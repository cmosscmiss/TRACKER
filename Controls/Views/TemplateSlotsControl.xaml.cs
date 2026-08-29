using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Tracker.Services;

namespace Tracker.Controls.Views;

/// <summary>
/// Componente visual de los templates disponibles, en tarjetas (imagen + nombre), para el selector de la toolbar.
/// Al pulsar uno se dispara <see cref="TemplateActivated"/> con la ruta de su JSON, para cargarlo.
/// </summary>
public sealed partial class TemplateSlotsControl : UserControl
{
    /// <summary>Item de una tarjeta para el binding.</summary>
    public sealed partial class SlotItem : ObservableObject
    {
        public string Name { get; init; } = string.Empty;
        public string JsonPath { get; init; } = string.Empty;
        public BitmapImage? Image { get; init; }
    }

    private readonly TemplateService _templateService;

    public TemplateSlotsControl()
    {
        InitializeComponent();
        _templateService = App.GetService<TemplateService>();
        Loaded += async (_, _) => await RefreshAsync();
    }

    /// <summary>Se activó un template; el argumento es la ruta de su JSON.</summary>
    public event EventHandler<string>? TemplateActivated;

    /// <summary>Recarga los items y sus miniaturas. La llama la toolbar al abrir el selector.</summary>
    public async Task RefreshAsync()
    {
        var items = new List<SlotItem>();

        foreach (TemplateService.TemplateEntry template in _templateService.GetAllTemplates())
            items.Add(new SlotItem
            {
                Name = template.Name,
                JsonPath = template.JsonPath,
                Image = await LoadThumbnailAsync(template.ImagePath)
            });

        Items.ItemsSource = items;
    }

    /// <summary>Al pulsar un template se activa (se carga su JSON).</summary>
    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SlotItem { JsonPath: { Length: > 0 } jsonPath })
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
