using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MM4LB.Enums;
using MM4LB.Models;
using System;
using System.IO;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace MM4LB.Controls.Views;

#region Enums
/// <summary>
/// Define los modos visuales disponibles para representar una imagen de juego
/// dentro de <see cref="GameImageControl"/>.
/// </summary>
public enum GameImageDisplayMode
{
    /// <summary>
    /// Modo visual por defecto.
    /// 
    /// Mantiene la disposición compacta original, pensada para integrarse en
    /// listas, grids u otros contextos donde el control no ocupa todo el espacio.
    /// </summary>
    Default,

    /// <summary>
    /// Modo independiente horizontal.
    /// 
    /// Se utiliza cuando el control debe mostrar la imagen y su información
    /// asociada en una composición optimizada para orientación horizontal.
    /// </summary>
    StandAloneHorizontalMode,

    /// <summary>
    /// Modo independiente vertical.
    ///
    /// Se utiliza cuando el control debe mostrar la imagen y su información
    /// asociada en una composición optimizada para orientación vertical.
    /// </summary>
    StandAloneVerticalMode,

    /// <summary>
    /// Modo compacto que muestra únicamente la imagen, sin la barra inferior con los datos.
    ///
    /// Pensado para tiras de miniaturas pequeñas (p. ej. las imágenes propias de la plataforma)
    /// donde el overlay de datos no aporta y resta espacio.
    /// </summary>
    ImageOnly
}
#endregion

/// <summary>
/// Control visual encargado de mostrar una imagen asociada a un juego.
/// 
/// El control expone una imagen mediante la propiedad <see cref="GameImage"/>
/// y permite alternar entre diferentes variantes visuales mediante
/// <see cref="DisplayMode"/>.
/// 
/// Internamente, el cambio de modo visual se resuelve mediante estados visuales
/// definidos en XAML y activados desde código con <see cref="VisualStateManager"/>.
/// </summary>
public sealed partial class GameImageControl : UserControl
{
    #region Dependency Properties
    /// <summary>
    /// Imagen de juego mostrada por el control.
    /// 
    /// Esta propiedad permite enlazar desde XAML o desde otros controles el modelo
    /// de imagen que debe representarse visualmente.
    /// </summary>
    public GameImage? GameImage
    {
        get => GetValue(GameImageProperty) as GameImage;
        set => SetValue(GameImageProperty, value);
    }

    /// <summary>
    /// DependencyProperty asociada a <see cref="GameImage"/>.
    /// 
    /// Permite que la imagen mostrada por el control participe en el sistema de
    /// binding, estilos, plantillas y actualización de propiedades de WinUI.
    /// </summary>
    public static readonly DependencyProperty GameImageProperty = DependencyProperty.Register(nameof(GameImage), typeof(GameImage), typeof(GameImageControl), new PropertyMetadata(null, OnGameImageChanged));

    private static void OnGameImageChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is GameImageControl control)
        {
            control.UpdateVideo();
        }
    }

    /// <summary>
    /// Modo visual utilizado por el control.
    /// 
    /// Determina qué estado visual debe activarse para adaptar la composición
    /// interna del control al contexto en el que se está mostrando.
    /// </summary>
    public GameImageDisplayMode DisplayMode
    {
        get => (GameImageDisplayMode)GetValue(DisplayModeProperty);
        set => SetValue(DisplayModeProperty, value);
    }

    /// <summary>
    /// DependencyProperty asociada a <see cref="DisplayMode"/>.
    /// 
    /// Cuando su valor cambia, se actualiza el estado visual del control para
    /// reflejar el nuevo modo de visualización.
    /// </summary>
    public static readonly DependencyProperty DisplayModeProperty = DependencyProperty.Register(nameof(DisplayMode), typeof(GameImageDisplayMode), typeof(GameImageControl), new PropertyMetadata(GameImageDisplayMode.Default, OnDisplayModeChanged));

    public bool IsSearchStringsPanelVisible
    {
        get => (bool)GetValue(IsSearchStringsPanelVisibleProperty);
        set => SetValue(IsSearchStringsPanelVisibleProperty, value);
    }

    public static readonly DependencyProperty IsSearchStringsPanelVisibleProperty = DependencyProperty.Register(nameof(IsSearchStringsPanelVisible), typeof(bool), typeof(GameImageControl), new PropertyMetadata(false));

    /// <summary>
    /// SlotIndex del widget anfitrión (el GameImagesDashboard) cuando este control hace de preview en grande.
    /// Un valor &lt; 0 indica que el widget no está colocado en el layout (oculto): en ese caso no debe arrancar
    /// la reproducción automática del vídeo aunque el control conserve tamaño momentáneamente. Vale 0 (activo)
    /// por defecto, de modo que los usos sin anfitrión (miniaturas, galería, ficha de plataforma) —que además no
    /// reproducen vídeo, al no estar en modo StandAlone— no se vean afectados.
    /// </summary>
    public int HostSlotIndex
    {
        get => (int)GetValue(HostSlotIndexProperty);
        set => SetValue(HostSlotIndexProperty, value);
    }

    public static readonly DependencyProperty HostSlotIndexProperty = DependencyProperty.Register(nameof(HostSlotIndex), typeof(int), typeof(GameImageControl), new PropertyMetadata(0, OnHostSlotIndexChanged));

    private static void OnHostSlotIndexChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is GameImageControl control)
        {
            control.UpdateVideo();
        }
    }

    /// <summary>
    /// Volumen (0–100) del preview de vídeo. 0 = silencio; cualquier valor mayor reproduce con sonido a ese nivel.
    /// Por defecto 0. Gobierna <c>Volume</c> e <c>IsMuted</c> del reproductor. Lo fija el dashboard desde el setting global.
    /// </summary>
    public double VideoVolume
    {
        get => (double)GetValue(VideoVolumeProperty);
        set => SetValue(VideoVolumeProperty, value);
    }

    public static readonly DependencyProperty VideoVolumeProperty = DependencyProperty.Register(nameof(VideoVolume), typeof(double), typeof(GameImageControl), new PropertyMetadata(0.0, OnVideoVolumeChanged));

    private static void OnVideoVolumeChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        // Si el reproductor ya existe, aplica el cambio en caliente; si no, EnsureVideoPlayer lo leerá al crearlo.
        if (dependencyObject is GameImageControl control && control._videoPlayer != null)
        {
            control.ApplyVideoVolume();
        }
    }

    /// <summary>Traduce <see cref="VideoVolume"/> (0–100) al reproductor: nivel 0–1 y mute cuando es 0.</summary>
    private void ApplyVideoVolume()
    {
        if (_videoPlayer is null) { return; }
        _videoPlayer.Volume = Math.Clamp(VideoVolume / 100.0, 0.0, 1.0);
        _videoPlayer.IsMuted = VideoVolume <= 0;
    }

    /// <summary>
    /// Si el preview de vídeo muestra los controles de reproducción (transport controls). False por defecto.
    /// Enlazado a <c>AreTransportControlsEnabled</c> del MediaPlayerElement. Lo fija el dashboard desde el setting.
    /// </summary>
    public bool ShowTransportControls
    {
        get => (bool)GetValue(ShowTransportControlsProperty);
        set => SetValue(ShowTransportControlsProperty, value);
    }

    public static readonly DependencyProperty ShowTransportControlsProperty = DependencyProperty.Register(nameof(ShowTransportControls), typeof(bool), typeof(GameImageControl), new PropertyMetadata(false));
    #endregion

    #region Constructors
    /// <summary>
    /// Inicializa una nueva instancia de <see cref="GameImageControl"/>.
    /// 
    /// Carga los componentes definidos en XAML y suscribe el control al evento
    /// Loaded para aplicar el estado visual correcto una vez que el control forma
    /// parte del árbol visual.
    /// </summary>
    public GameImageControl()
    {
        InitializeComponent();

        Loaded += GameImageControl_Loaded;
        Unloaded += GameImageControl_Unloaded;
        // El control vive en dos instancias (vista horizontal y vertical del dashboard) que coexisten en el
        // árbol; solo una es visible a la vez. Reproducir en la oculta sonaría a la vez que la visible, así que
        // SizeChanged (que cruza 0↔N al alternar de vista) reevalúa la reproducción según la visibilidad real.
        SizeChanged += GameImageControl_SizeChanged;
    }
    #endregion

    #region Helpers
    /// <summary>
    /// Texto de la región para la pastilla del preview. Si la imagen no tiene región asignada
    /// (<see cref="ImageRegion.NoRegion"/>, cuyo valor es cadena vacía) devuelve un valor por defecto en lugar
    /// de dejar la pastilla en blanco. Es solo presentación: no altera el dato del modelo.
    /// </summary>
    public string RegionText(ImageRegion region)
    {
        string? value = region?.Value;
        return string.IsNullOrEmpty(value) ? (MM4LB.Services.LocalizationService.Instance?[MM4LB.Helpers.LocKeys.Common_NoneRegion_Placeholder] ?? "None") : value;
    }

    /// <summary>
    /// Visibility of the "play" badge shown over the thumbnail when the item is a video (a game video —
    /// Video Snap, Theme Video — or the platform video). Its bitmap is a still frame extracted from the
    /// clip, so the badge marks it as playable. Solo se muestra en modo <see cref="GameImageDisplayMode.ImageOnly"/>
    /// (la tira del PlatformDetails); en las miniaturas de las listas/grids (modo Default) se omite. Collapsed
    /// para imágenes normales.
    /// </summary>
    public Visibility VideoBadgeVisibility(GameImage image, GameImageDisplayMode mode)
        => IsVideoItem(image) && mode == GameImageDisplayMode.ImageOnly ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Visibility of the corner "play" badge shown on video items in the list/grid thumbnails (Default mode), so a
    /// video is identifiable among images in a mixed gallery (e.g. the game gallery). Not shown in ImageOnly (that
    /// uses the centred badge) nor in StandAlone.
    /// </summary>
    public Visibility VideoCornerBadgeVisibility(GameImage image, GameImageDisplayMode mode)
        => IsVideoItem(image) && mode != GameImageDisplayMode.ImageOnly && !IsStandAlone(mode)
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <summary>
    /// Quality label (e.g. "1080p") derived from a video's vertical resolution. The raw WxH already lives in
    /// the "Dimensions" stat; this is the familiar 360p/720p/1080p classification. Empty until the height loads.
    /// </summary>
    public string VideoQuality(int height) => height > 0 ? $"{height}p" : string.Empty;

    /// <summary>Visible only for video items: the stats row shows Quality + Duration in place of the Region.</summary>
    public Visibility VideoInfoVisibility(GameImage image)
        => IsVideoItem(image) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Visible only for non-video items: the stats row shows the image Region.</summary>
    public Visibility ImageRegionVisibility(GameImage image)
        => IsVideoItem(image) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// Igual que <see cref="VideoInfoVisibility"/> pero con nombre propio para el overlay del modo lista
    /// (DefaultDisplayMode). Tener un binding de función distinto evita que el generador de x:Bind deduplique
    /// la misma expresión usada en los dos subárboles hermanos (Default / StandAlone), lo que disparaba la
    /// generación de un método UpdateFallback que el propio compilador no llegaba a definir.
    /// </summary>
    public Visibility VideoOverlayVisibility(GameImage image) => VideoInfoVisibility(image);

    /// <summary>Igual que <see cref="ImageRegionVisibility"/>, con nombre propio para el overlay del modo lista.</summary>
    public Visibility ImageOverlayVisibility(GameImage image) => ImageRegionVisibility(image);

    /// <summary>True when the item is a video (game video — Video Snap / Theme Video — or platform video).</summary>
    private static bool IsVideoItem(GameImage? image)
        => image?.Type != null && (MediaType.IsVideo(image.Type.Key) || MediaType.IsPlatformVideo(image.Type.Key));
    #endregion

    #region Event Handlers
    /// <summary>
    /// Gestiona el evento Loaded del control.
    /// 
    /// Al cargarse el control, fuerza la aplicación del estado visual
    /// correspondiente al modo de visualización actual, sin usar transiciones.
    /// </summary>
    /// <param name="sender">Objeto que ha originado el evento.</param>
    /// <param name="e">Argumentos del evento Loaded.</param>
    private void GameImageControl_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateDisplayModeState(false);
        UpdateVideo();
    }

    /// <summary>
    /// Libera el reproductor al sacar el control del árbol (p. ej. al navegar fuera del dashboard), para no
    /// dejar audio sonando ni retener el archivo de vídeo.
    /// </summary>
    private void GameImageControl_Unloaded(object sender, RoutedEventArgs e)
    {
        DisposeVideoPlayer();
    }

    /// <summary>
    /// Reevalúa la reproducción cuando cambia el tamaño efectivo del control: al alternar entre la vista
    /// horizontal y la vertical del dashboard, la instancia que se oculta pasa a 0 (debe parar) y la que
    /// aparece recupera tamaño (debe arrancar si su imagen es un vídeo).
    /// </summary>
    private void GameImageControl_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateVideo();
    }
    #endregion

    #region Private Methods
    /// <summary>
    /// Callback ejecutado cuando cambia la propiedad <see cref="DisplayMode"/>.
    /// 
    /// Si el objeto que ha cambiado es una instancia de <see cref="GameImageControl"/>,
    /// actualiza el estado visual del control usando transiciones.
    /// </summary>
    /// <param name="dependencyObject">Objeto sobre el que ha cambiado la propiedad.</param>
    /// <param name="e">Información sobre el cambio de valor de la propiedad.</param>
    private static void OnDisplayModeChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is GameImageControl control)
        {
            control.UpdateDisplayModeState(true);
            control.UpdateVideo();
        }
    }

    /// <summary>
    /// Actualiza el estado visual del control según el valor actual de
    /// <see cref="DisplayMode"/>.
    /// 
    /// Este método traduce el valor del enum a un nombre de VisualState definido
    /// en el XAML del control y solicita a <see cref="VisualStateManager"/> que
    /// active dicho estado.
    /// </summary>
    /// <param name="useTransitions">
    /// Indica si el cambio de estado debe aplicar las transiciones visuales
    /// definidas en XAML.
    /// </param>
    private void UpdateDisplayModeState(bool useTransitions)
    {
        string stateName = DisplayMode switch
        {
            GameImageDisplayMode.StandAloneHorizontalMode => "StandAloneHorizontalMode",
            GameImageDisplayMode.StandAloneVerticalMode => "StandAloneVerticalMode",
            GameImageDisplayMode.ImageOnly => "ImageOnly",
            _ => "Default"
        };

        VisualStateManager.GoToState(this, stateName, useTransitions);
    }
    #endregion

    #region Video preview
    private MediaPlayer? _videoPlayer;
    private MediaSource? _videoSource;
    private string? _currentVideoPath;

    /// <summary>True cuando el vídeo ha llegado a su fin y se está mostrando el póster con la chapa de "play":
    /// en ese estado un toque sobre la imagen (<see cref="ImageHost_Tapped"/>) reinicia la reproducción.</summary>
    private bool _videoEnded;

    private static bool IsStandAlone(GameImageDisplayMode mode)
        => mode is GameImageDisplayMode.StandAloneHorizontalMode or GameImageDisplayMode.StandAloneVerticalMode;

    private bool IsCurrentImageVideo() => IsVideoItem(GameImage);

    /// <summary>El widget anfitrión está colocado en el layout (visible). Ver <see cref="HostSlotIndex"/>.</summary>
    private bool IsHostActive => HostSlotIndex >= 0;

    /// <summary>
    /// Decide si el vídeo debe reproducirse en este control y en qué estado dejar el reproductor. Solo
    /// reproduce en el modo StandAlone (la vista en grande del dashboard), con una imagen de tipo vídeo y
    /// cuando el control es realmente visible (tamaño efectivo &gt; 0; ver el comentario del constructor sobre
    /// las dos instancias del dashboard). En cualquier otro caso para y suelta la reproducción. Es idempotente:
    /// si ya está reproduciendo el mismo archivo no reinicia (un relayout/SizeChanged no debe rebobinar).
    /// </summary>
    private void UpdateVideo()
    {
        if (PreviewVideo == null)
            return; // la plantilla aún no se ha aplicado

        bool visible = IsLoaded && ActualWidth > 0 && ActualHeight > 0;
        string? path = GameImage?.File;
        // IsHostActive evita arrancar la reproducción (con su audio) cuando el dashboard que aloja este preview
        // no está colocado en el layout (SlotIndex < 0), aunque el control conserve tamaño en ese instante.
        bool shouldPlay = IsStandAlone(DisplayMode) && IsCurrentImageVideo() && visible && IsHostActive
                          && path != null && File.Exists(path);

        if (shouldPlay)
        {
            if (_videoPlayer != null && string.Equals(_currentVideoPath, path, StringComparison.OrdinalIgnoreCase))
                return;

            EnsureVideoPlayer();
            _currentVideoPath = path;
            ResetReplayState();
            // Oculto hasta el primer frame (OnVideoMediaOpened); mientras, se ve el póster (el Image con el fotograma).
            PreviewVideo.Visibility = Visibility.Collapsed;
            // new Uri(path) puede lanzar UriFormatException con rutas con caracteres raros (datos de LaunchBox
            // editados a mano); Uri.TryCreate evita el crash: si no es una URI válida, no se reproduce.
            if (Uri.TryCreate(path, UriKind.Absolute, out Uri? videoUri))
                SetVideoSource(MediaSource.CreateFromUri(videoUri));
        }
        else
        {
            if (_currentVideoPath != null || _videoSource != null)
            {
                StopVideo();
                _currentVideoPath = null;
            }

            ResetReplayState();
            PreviewVideo.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Oculta la chapa de "play" del fin de vídeo y limpia el estado de reinicio.</summary>
    private void ResetReplayState()
    {
        _videoEnded = false;

        if (ReplayBadge != null)
            ReplayBadge.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Crea y cablea el reproductor en el primer uso (autoplay, con audio; los controles de transporte los
    /// aporta el propio MediaPlayerElement).
    /// </summary>
    private void EnsureVideoPlayer()
    {
        if (_videoPlayer != null)
            return;

        _videoPlayer = new MediaPlayer
        {
            AutoPlay = true
        };
        ApplyVideoVolume();

        _videoPlayer.MediaOpened += OnVideoMediaOpened;
        _videoPlayer.MediaEnded += OnVideoMediaEnded;
        _videoPlayer.MediaFailed += OnVideoMediaFailed;
        PreviewVideo.SetMediaPlayer(_videoPlayer);
    }

    /// <summary>
    /// Un vídeo corrupto o con códec no soportado dejaba el preview congelado sin ningún rastro. Lo registramos;
    /// el póster (el Image con el fotograma) sigue visible porque PreviewVideo no se revela hasta OnVideoMediaOpened.
    /// </summary>
    private void OnVideoMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        MM4LB.Services.ExceptionService.LogToFile(args.ExtendedErrorCode, $"Video playback failed ({args.Error}): {args.ErrorMessage}");
    }

    /// <summary>
    /// Revela el vídeo solo cuando su primer frame ha decodificado, para que aparezca sobre el póster sin flash
    /// negro. El evento se levanta fuera del hilo de UI, así que se reencola; revalida que sigue tocando
    /// reproducir (la selección o la vista pudieron cambiar mientras abría).
    /// </summary>
    private void OnVideoMediaOpened(MediaPlayer sender, object args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (IsStandAlone(DisplayMode) && IsCurrentImageVideo() && IsHostActive && IsLoaded && ActualWidth > 0 && ActualHeight > 0)
            {
                ResetReplayState();
                PreviewVideo.Visibility = Visibility.Visible;
            }
        });
    }

    /// <summary>
    /// Al terminar el vídeo, vuelve a revelar el póster (el fotograma extraído al cargarlo) ocultando el
    /// reproductor y, con él, sus controles de transporte, y muestra la chapa de "play" sobre la imagen. El
    /// evento llega fuera del hilo de UI, por lo que se reencola. Un toque sobre el área de imagen
    /// (<see cref="ImageHost_Tapped"/>) reinicia la reproducción desde el principio.
    /// </summary>
    private void OnVideoMediaEnded(MediaPlayer sender, object args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (PreviewVideo == null)
                return;

            _videoEnded = true;
            PreviewVideo.Visibility = Visibility.Collapsed;

            if (ReplayBadge != null)
                ReplayBadge.Visibility = Visibility.Visible;
        });
    }

    /// <summary>
    /// Gestiona el toque sobre el área de imagen del preview de vídeo:
    /// <list type="bullet">
    /// <item>Si el vídeo ha terminado, reinicia la reproducción desde el principio.</item>
    /// <item>Si está en reproducción y NO se muestran los controles de transporte, alterna play/pausa (un toque
    /// pausa, el siguiente reanuda). Con los controles visibles no hace nada: la interacción la gobiernan ellos.</item>
    /// </list>
    /// </summary>
    private void ImageHost_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_videoPlayer == null)
            return;

        if (_videoEnded)
        {
            ResetReplayState();
            PreviewVideo.Visibility = Visibility.Visible;
            _videoPlayer.PlaybackSession.Position = TimeSpan.Zero;
            _videoPlayer.Play();
            return;
        }

        if (ShowTransportControls || !IsCurrentImageVideo())
            return;

        if (_videoPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
        {
            _videoPlayer.Pause();
            // Pausado: muestra la chapa de "play" sobre el vídeo para indicar que está detenido.
            if (ReplayBadge != null)
                ReplayBadge.Visibility = Visibility.Visible;
        }
        else
        {
            _videoPlayer.Play();
            if (ReplayBadge != null)
                ReplayBadge.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Cambia la fuente del reproductor, liberando la anterior.
    /// </summary>
    private void SetVideoSource(MediaSource source)
    {
        var previous = _videoSource;

        _videoSource = source;
        _videoPlayer!.Source = source;

        previous?.Dispose();
    }

    /// <summary>
    /// Pausa la reproducción y libera la fuente actual.
    /// </summary>
    private void StopVideo()
    {
        if (_videoPlayer != null)
        {
            _videoPlayer.Pause();
            _videoPlayer.Source = null;
        }

        if (_videoSource != null)
        {
            _videoSource.Dispose();
            _videoSource = null;
        }
    }

    /// <summary>
    /// Para y destruye por completo el reproductor (al sacar el control del árbol).
    /// </summary>
    private void DisposeVideoPlayer()
    {
        StopVideo();
        _currentVideoPath = null;
        ResetReplayState();

        if (_videoPlayer != null)
        {
            _videoPlayer.MediaOpened -= OnVideoMediaOpened;
            _videoPlayer.MediaEnded -= OnVideoMediaEnded;
            _videoPlayer.Dispose();
            _videoPlayer = null;
        }
    }
    #endregion
}