using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics.Effects;
using Windows.Storage.Streams;
using Windows.UI;

namespace MM4LB.Controls.Templates;

/// <summary>
/// Control personalizado que renderiza una imagen aplicando un tinte configurable
/// mediante la Composition API.
///
/// Este control:
/// - Carga una imagen desde un recurso o ruta interna.
/// - Aplica un tinte basado en un color, opacidad, saturación y brillo.
/// - Ajusta el tinte modificando el color en el espacio HSL antes de mezclarlo.
/// - Usa BlendEffect para combinar la imagen con el tinte según el modo seleccionado.
/// - Mantiene sincronía entre RenderTransform (XAML) y el SpriteVisual de composición.
/// - Ofrece un rendimiento óptimo al usar efectos nativos de Win2D + Composition.
///
/// Ideal para fondos temáticos, pantallas de carga y efectos visuales dinámicos.
/// </summary>
public sealed class TintedImage : Control
{
    #region Attributes
    private SpriteVisual? _sprite;
    private CompositionSurfaceBrush? _surfaceBrush;
    private CompositionEffectBrush? _effectBrush;
    private LoadedImageSurface? _imageSurface;
    #endregion

    #region Dependency Properties
    public string Source
    {
        get => (string)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(
            nameof(Source),
            typeof(string),
            typeof(TintedImage),
            new PropertyMetadata(null, OnSourceChanged));

    public Color TintColor
    {
        get => (Color)GetValue(TintColorProperty);
        set => SetValue(TintColorProperty, value);
    }

    public static readonly DependencyProperty TintColorProperty =
        DependencyProperty.Register(
            nameof(TintColor),
            typeof(Color),
            typeof(TintedImage),
            new PropertyMetadata(Colors.Transparent, OnTintChanged));

    public double TintOpacity
    {
        get => (double)GetValue(TintOpacityProperty);
        set => SetValue(TintOpacityProperty, value);
    }

    public static readonly DependencyProperty TintOpacityProperty =
        DependencyProperty.Register(
            nameof(TintOpacity),
            typeof(double),
            typeof(TintedImage),
            new PropertyMetadata(1.0, OnTintChanged));
    public double TintSaturation
    {
        get => (double)GetValue(TintSaturationProperty);
        set => SetValue(TintSaturationProperty, value);
    }

    public static readonly DependencyProperty TintSaturationProperty =
        DependencyProperty.Register(
            nameof(TintSaturation),
            typeof(double),
            typeof(TintedImage),
            new PropertyMetadata(1.0, OnTintChanged));

    public double TintBrightness
    {
        get => (double)GetValue(TintBrightnessProperty);
        set => SetValue(TintBrightnessProperty, value);
    }

    public static readonly DependencyProperty TintBrightnessProperty =
        DependencyProperty.Register(
            nameof(TintBrightness),
            typeof(double),
            typeof(TintedImage),
            new PropertyMetadata(1.0, OnTintChanged));

    public BlendEffectMode BlendMode
    {
        get => (BlendEffectMode)GetValue(BlendModeProperty);
        set => SetValue(BlendModeProperty, value);
    }

    public static readonly DependencyProperty BlendModeProperty =
        DependencyProperty.Register(
            nameof(BlendMode),
            typeof(BlendEffectMode),
            typeof(TintedImage),
            new PropertyMetadata(BlendEffectMode.Multiply, OnTintChanged));

    public double Blur
    {
        get => (double)GetValue(BlurAmountProperty);
        set => SetValue(BlurAmountProperty, value);
    }

    public static readonly DependencyProperty BlurAmountProperty =
        DependencyProperty.Register(
            nameof(Blur),
            typeof(double),
            typeof(TintedImage),
            new PropertyMetadata(0.0, OnTintChanged));

    /// <summary>
    /// Activa una máscara de opacidad vertical (estilo máscara de capa): el alfa de la imagen se
    /// funde de opaco arriba a transparente abajo, dejando ver lo que haya detrás en vez de mezclar
    /// con un color de fondo. Desactivada por defecto para no alterar los usos existentes.
    /// </summary>
    public bool FadeMaskEnabled
    {
        get => (bool)GetValue(FadeMaskEnabledProperty);
        set => SetValue(FadeMaskEnabledProperty, value);
    }

    public static readonly DependencyProperty FadeMaskEnabledProperty =
        DependencyProperty.Register(
            nameof(FadeMaskEnabled),
            typeof(bool),
            typeof(TintedImage),
            new PropertyMetadata(false, OnTintChanged));

    /// <summary>
    /// Offset relativo [0..1] (de arriba a abajo) hasta el que la imagen permanece totalmente opaca
    /// antes de empezar a desvanecerse. Solo aplica con <see cref="FadeMaskEnabled"/>.
    /// </summary>
    public double FadeStart
    {
        get => (double)GetValue(FadeStartProperty);
        set => SetValue(FadeStartProperty, value);
    }

    public static readonly DependencyProperty FadeStartProperty =
        DependencyProperty.Register(
            nameof(FadeStart),
            typeof(double),
            typeof(TintedImage),
            new PropertyMetadata(0.5, OnTintChanged));

    /// <summary>
    /// Offset relativo [0..1] en el que la imagen queda totalmente transparente. Solo aplica con
    /// <see cref="FadeMaskEnabled"/>.
    /// </summary>
    public double FadeEnd
    {
        get => (double)GetValue(FadeEndProperty);
        set => SetValue(FadeEndProperty, value);
    }

    public static readonly DependencyProperty FadeEndProperty =
        DependencyProperty.Register(
            nameof(FadeEnd),
            typeof(double),
            typeof(TintedImage),
            new PropertyMetadata(0.9, OnTintChanged));
    #endregion

    #region Events
    public event EventHandler? ImageReady;
    #endregion

    #region Constructor
    /// <summary>
    /// Constructor del control.
    /// 
    /// Responsabilidades:
    /// - Establecer la plantilla visual por defecto.
    /// - Suscribirse a eventos clave (Loaded, SizeChanged).
    /// - Detectar cambios en RenderTransform para sincronizar transformaciones con el visual de composición.
    /// </summary>
    public TintedImage()
    {
        this.DefaultStyleKey = typeof(TintedImage);

        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;

        // Escuchar cambios en RenderTransform
        this.RegisterPropertyChangedCallback(RenderTransformProperty, (_, __) => ApplyRenderTransform());
    }
    #endregion

    #region Subscribed Events
    /// <summary>
    /// Evento ejecutado cuando el control se carga en el árbol visual.
    /// Inicializa la infraestructura de composición, carga la imagen,
    /// aplica el efecto y sincroniza transformaciones.
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        InitializeComposition();
        LoadImage();
        UpdateEffect();
        ApplyRenderTransform();
    }

    /// <summary>
    /// Evento ejecutado cuando el control cambia de tamaño.
    /// Ajusta el tamaño del SpriteVisual y reaplica transformaciones.
    /// </summary>
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_sprite != null)
        {
            _sprite.Size = new Vector2((float)e.NewSize.Width, (float)e.NewSize.Height);
        }

        ApplyRenderTransform();
    }

    /// <summary>
    /// Evento estático que detecta cambios en la propiedad Source.
    /// Recarga la imagen cuando cambia la ruta.
    /// </summary>
    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TintedImage img)
            img.LoadImage();
    }

    /// <summary>
    /// Evento estático que detecta cambios en TintColor o BlendMode.
    /// Actualiza el efecto de composición para reflejar el nuevo tinte.
    /// </summary>
    private static void OnTintChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TintedImage img)
            img.UpdateEffect();
    }
    #endregion

    #region Methods (private)
    /// <summary>
    /// Inicializa la infraestructura de composición creando el SpriteVisual
    /// que actuará como contenedor de la imagen y del efecto.
    /// </summary>
    private void InitializeComposition()
    {
        var compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;

        _sprite = compositor.CreateSpriteVisual();
        _sprite.Size = new Vector2((float)ActualWidth, (float)ActualHeight);

        ElementCompositionPreview.SetElementChildVisual(this, _sprite);
    }

    /// <summary>
    /// Carga la imagen indicada en la propiedad Source y crea el SurfaceBrush
    /// que servirá como entrada del efecto de composición.
    /// 
    /// Cuando la imagen termina de cargarse, dispara el evento ImageReady.
    /// </summary>
    private void LoadImage()
    {
        if (_sprite == null)
            return;

        // Sin Source: limpiar la imagen actual (de lo contrario se quedaría pegada la composición previa,
        // p. ej. el fanart de la plataforma anterior al pasar a una sin fanart).
        if (string.IsNullOrEmpty(Source))
        {
            ClearImage();
            return;
        }

        if (TryGetSupportedUri(Source, out var uri))
        {
            // Recursos del paquete/red (ms-appx, ms-appdata, http...): carga directa por URI.
            UseSurface(LoadedImageSurface.StartLoadFromUri(uri));
        }
        else
        {
            // Ruta de fichero del sistema (p. ej. el fanart de la plataforma). Se carga desde un
            // stream gestionado en memoria: se evita StorageFile.GetFileFromPathAsync, que en esta
            // app sin empaquetar hace fail-fast (STATUS_STOWED_EXCEPTION 0xc000027b).
            _ = LoadFromFileAsync(Source);
        }
    }

    /// <summary>
    /// Indica si <paramref name="source"/> es una URI con un esquema que
    /// <see cref="LoadedImageSurface.StartLoadFromUri(Uri)"/> admite directamente. Las rutas de
    /// fichero del sistema (C:\...) devuelven false y se cargan por stream.
    /// </summary>
    private static bool TryGetSupportedUri(string source, out Uri? uri)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out uri)
            && uri.Scheme is "ms-appx" or "ms-appdata" or "http" or "https")
        {
            return true;
        }

        uri = null;
        return false;
    }

    /// <summary>
    /// Carga la imagen desde una ruta de fichero local a través de un stream en memoria y construye
    /// el SurfaceBrush. El stream se libera al completar la carga.
    /// </summary>
    private async Task LoadFromFileAsync(string path)
    {
        // Fichero inexistente (p. ej. una plataforma sin fanart, cuyo Fanart.File apunta a un .png que no
        // está en disco): limpiar para no dejar pegada la imagen anterior.
        if (!File.Exists(path))
        {
            ClearImage();
            return;
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(path);

            var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);

            // Source pudo cambiar mientras se leía el fichero (cambio rápido de plataforma); descartar este
            // resultado tardío para no sobrescribir la imagen ya vigente.
            if (_sprite == null || path != Source)
            {
                stream.Dispose();
                return;
            }

            var surface = LoadedImageSurface.StartLoadFromStream(stream);
            surface.LoadCompleted += (_, __) => stream.Dispose();
            UseSurface(surface);
        }
        catch
        {
            // Imagen ilegible o en un formato no soportado: el control se queda vacío.
            ClearImage();
        }
    }

    /// <summary>
    /// Asocia el surface cargado al SurfaceBrush, reaplica el efecto y propaga <see cref="ImageReady"/>.
    /// </summary>
    private void UseSurface(LoadedImageSurface surface)
    {
        if (_sprite == null)
            return;

        // Soltar el surface/brush anterior antes de reemplazarlos (evita acumularlos al cambiar de imagen).
        _effectBrush?.Dispose();
        _effectBrush = null;
        _surfaceBrush?.Dispose();
        _imageSurface?.Dispose();

        _imageSurface = surface;
        _surfaceBrush = _sprite.Compositor.CreateSurfaceBrush(surface);
        _surfaceBrush.Stretch = CompositionStretch.UniformToFill;

        surface.LoadCompleted += (_, __) => ImageReady?.Invoke(this, EventArgs.Empty);

        UpdateEffect();
    }

    /// <summary>
    /// Vacía la imagen del control: quita el brush del visual y libera el surface/brushes actuales. Se usa
    /// cuando no hay <see cref="Source"/> o el fichero no existe, para que no quede pegada la imagen previa.
    /// </summary>
    private void ClearImage()
    {
        if (_sprite != null)
            _sprite.Brush = null;

        _effectBrush?.Dispose();
        _effectBrush = null;
        _surfaceBrush?.Dispose();
        _surfaceBrush = null;
        _imageSurface?.Dispose();
        _imageSurface = null;
    }

    /// <summary>
    /// Convierte un color RGB a su representación en el espacio HSL.
    /// 
    /// Se utiliza para poder modificar saturación y brillo del tinte
    /// antes de reconstruir el color final.
    /// </summary>
    private static (double H, double S, double L) ToHSL(Color c)
    {
        double r = c.R / 255.0;
        double g = c.G / 255.0;
        double b = c.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double h = 0, s, l = (max + min) / 2.0;

        if (max == min)
        {
            h = s = 0;
        }
        else
        {
            double d = max - min;
            s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);

            if (max == r)
                h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g)
                h = (b - r) / d + 2;
            else
                h = (r - g) / d + 4;

            h /= 6.0;
        }

        return (h, s, l);
    }

    /// <summary>
    /// Reconstruye un color RGB a partir de valores HSL y un canal alfa.
    /// 
    /// Permite generar el color final del tinte tras aplicar saturación,
    /// brillo y opacidad.
    /// </summary>
    private static Color FromHSL(double h, double s, double l, byte alpha)
    {
        double r, g, b;

        if (s == 0)
        {
            r = g = b = l;
        }
        else
        {
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;

            double HueToRGB(double t)
            {
                if (t < 0) t += 1;
                if (t > 1) t -= 1;
                if (t < 1.0 / 6) return p + (q - p) * 6 * t;
                if (t < 1.0 / 2) return q;
                if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
                return p;
            }

            r = HueToRGB(h + 1.0 / 3);
            g = HueToRGB(h);
            b = HueToRGB(h - 1.0 / 3);
        }

        return Color.FromArgb(
            alpha,
            (byte)(r * 255),
            (byte)(g * 255),
            (byte)(b * 255)
        );
    }


    /// <summary>
    /// Construye o actualiza el efecto de composición que mezcla la imagen
    /// con el tinte configurado.
    ///
    /// Proceso:
    /// 1. Convierte TintColor a HSL.
    /// 2. Aplica TintSaturation y TintBrightness.
    /// 3. Aplica TintOpacity al canal alfa.
    /// 4. Reconstruye el color final del tinte.
    /// 5. Crea un BlendEffect que mezcla la imagen con el tinte.
    /// 
    /// El resultado mantiene la imagen completamente opaca y ajusta únicamente
    /// la intensidad del tinte.
    /// </summary>
    private void UpdateEffect()
    {
        if (_sprite == null || _surfaceBrush == null)
            return;

        var compositor = _sprite.Compositor;

        // Convertir a HSL
        var (h, s, l) = ToHSL(TintColor);

        // Aplicar saturación y brillo
        s *= TintSaturation;
        l *= TintBrightness;

        // Clamp
        s = Math.Clamp(s, 0, 1);
        l = Math.Clamp(l, 0, 1);

        // Aplicar TintOpacity al alfa
        byte alpha = (byte)(TintOpacity * 255);

        // Reconstruir color final
        var finalTint = FromHSL(h, s, l, alpha);

        // Fuente de la imagen: opcionalmente con blur
        var sourceParameter = new CompositionEffectSourceParameter("source");

        IGraphicsEffectSource foregroundSource;

        var blur = Math.Max(0.0, Blur);
        if (blur > 0)
        {
            foregroundSource = new GaussianBlurEffect
            {
                Name = "Blur",
                BlurAmount = (float)blur,
                BorderMode = EffectBorderMode.Hard,
                Source = sourceParameter
            };
        }
        else
        {
            foregroundSource = sourceParameter;
        }

        // BlendEffect con el tinte modificado y la imagen (posiblemente desenfocada)
        var blend = new BlendEffect
        {
            Mode = BlendMode,
            Background = new ColorSourceEffect { Color = finalTint },
            Foreground = foregroundSource
        };

        var factory = compositor.CreateEffectFactory(blend);
        _effectBrush = factory.CreateBrush();
        _effectBrush.SetSourceParameter("source", _surfaceBrush);

        _sprite.Brush = ApplyFadeMask(_effectBrush);
    }

    /// <summary>
    /// Envuelve el brush con una máscara de opacidad vertical (degradado opaco→transparente) cuando
    /// <see cref="FadeMaskEnabled"/> está activa; si no, devuelve el brush sin cambios. El degradado usa
    /// mapeo relativo, así que el desvanecido escala con el tamaño del control.
    /// </summary>
    private CompositionBrush ApplyFadeMask(CompositionBrush content)
    {
        if (!FadeMaskEnabled || _sprite == null)
            return content;

        var compositor = _sprite.Compositor;

        var start = (float)Math.Clamp(FadeStart, 0.0, 1.0);
        var end = (float)Math.Clamp(FadeEnd, 0.0, 1.0);
        if (end <= start)
            end = Math.Min(1f, start + 0.0001f);

        var gradient = compositor.CreateLinearGradientBrush();
        gradient.MappingMode = CompositionMappingMode.Relative;
        gradient.StartPoint = new Vector2(0f, 0f);
        gradient.EndPoint = new Vector2(0f, 1f);
        gradient.ColorStops.Add(compositor.CreateColorGradientStop(0f, Colors.White));
        gradient.ColorStops.Add(compositor.CreateColorGradientStop(start, Colors.White));
        gradient.ColorStops.Add(compositor.CreateColorGradientStop(end, Colors.Transparent));

        var mask = compositor.CreateMaskBrush();
        mask.Source = content;
        mask.Mask = gradient;
        return mask;
    }

    /// <summary>
    /// Aplica transformaciones (escala y desplazamiento) al SpriteVisual
    /// para mantener sincronía con el RenderTransform del control XAML.
    /// 
    /// Esto garantiza que cualquier transformación aplicada en XAML
    /// también afecte al visual de composición.
    /// </summary>
    private void ApplyRenderTransform()
    {
        if (_sprite == null || RenderTransform == null)
            return;

        _sprite.CenterPoint = new Vector3((float)ActualWidth / 2, (float)ActualHeight / 2, 0);
        _sprite.Offset = Vector3.Zero;
        _sprite.Scale = Vector3.One;

        switch (RenderTransform)
        {
            case ScaleTransform scale:
                _sprite.Scale = new Vector3((float)scale.ScaleX, (float)scale.ScaleY, 1);
                break;

            case TranslateTransform translate:
                _sprite.Offset = new Vector3((float)translate.X, (float)translate.Y, 0);
                break;

            case TransformGroup group:
                foreach (var t in group.Children)
                {
                    if (t is ScaleTransform s)
                        _sprite.Scale = new Vector3((float)s.ScaleX, (float)s.ScaleY, 1);

                    if (t is TranslateTransform tr)
                        _sprite.Offset = new Vector3((float)tr.X, (float)tr.Y, 0);
                }
                break;
        }
    }
    #endregion
}