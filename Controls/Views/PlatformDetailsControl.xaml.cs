using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MM4LB.Controls.ViewModels;
using MM4LB.Services;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace MM4LB.Controls.Views;

public sealed partial class PlatformDetailsControl : UserControl
{
    #region Constants
    // Content stays hidden for this long before the entrance starts, so the previous platform's content
    // (including a playing video) does not flicker while the new one settles.
    private const int EntrancePreDelayMs = 200;
    private const double EntranceSlideOffset = 30;
    private const double EntranceDurationMs = 1000;
    // The logo's fade-in starts a bit later than the rest of the content.
    private const int LogoFadeDelayMs = 200;
    private const double LogoFadeDurationMs = 1200;
    private const double LogoScaleStart = 0.8;
    private const double LogoScaleDurationMs = 1000;
    #endregion

    #region Attributes
    private MediaPlayer? _videoPlayer;
    private MediaSource? _videoSource;
    private AnimationService.IAnimationHandle? _entranceAnimation;
    private AnimationService.IAnimationHandle? _logoFadeAnimation;
    private CancellationTokenSource? _entranceCts;

    /// <summary>Glue del chart de cobertura (volcado de Sections + velocidad de animación); compartido con StatsPlatformControl.</summary>
    private readonly CoverageChartGlue _coverageGlue = new();

    /// <summary>View model cuya configuración ya se ha cargado, para restaurar los ajustes una sola vez por instancia.</summary>
    private readonly ViewModelConfigGate<PlatformDetailsViewModel> _configGate = new();
    #endregion

    #region Dependency Properties
    /// <summary>
    /// Property to hold the view model for the control.
    /// </summary>
    public PlatformDetailsViewModel? ViewModel
    {
        get => (PlatformDetailsViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register("ViewModel", typeof(PlatformDetailsViewModel), typeof(PlatformDetailsControl), new PropertyMetadata(null, OnViewModelChanged));
    #endregion

    #region Constructors
    public PlatformDetailsControl()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }
    #endregion

    #region Subscribed events
    /// <summary>
    /// Re-subscribes to the new ViewModel so the preview can react to selection changes (image vs video).
    /// </summary>
    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PlatformDetailsControl control)
        {
            return;
        }


        if (e.OldValue is PlatformDetailsViewModel oldViewModel)
        {
            oldViewModel.PropertyChanged -= control.OnViewModelPropertyChanged;
            oldViewModel.SharedDataService.SelectedPlatformChanged -= control.OnSelectedPlatformChanged;
            oldViewModel.SharedDataService.SelectedImageSetChanged -= control.OnSelectedImageSetChanged;
        }

        if (e.NewValue is PlatformDetailsViewModel newViewModel)
        {
            newViewModel.PropertyChanged += control.OnViewModelPropertyChanged;
            newViewModel.SharedDataService.SelectedPlatformChanged += control.OnSelectedPlatformChanged;
            newViewModel.SharedDataService.SelectedImageSetChanged += control.OnSelectedImageSetChanged;
        }

        control.UpdatePreviewVideo();
        // La propiedad Sections del CartesianChart no admite x:Bind (su tipo genérico rompe la binding compilada),
        // así que la sincronizamos a mano desde el view model.
        control.UpdateCoverageSections();
        control.ApplyCoverageChartAnimation();
        control.EnsureConfigurationLoaded();
    }

    /// <summary>
    /// Plays the entrance once on startup (when the control first enters the visual tree).
    /// </summary>
    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        EnsureConfigurationLoaded();
        UpdateCoverageSections();
        ApplyCoverageChartAnimation();
        RunEntranceAnimation();
    }

    /// <summary>
    /// Replays the content entrance animation whenever the active platform changes. Called directly (the
    /// event is raised on the UI thread) so the synchronous "hide" at the start of the entrance runs before
    /// the first await — no frame of the new content shows at full opacity before it is hidden.
    /// </summary>
    private void OnSelectedPlatformChanged(object? sender, SharedDataService.PlatformChangedEventArgs e)
    {
        RunEntranceAnimation();
    }

    /// <summary>
    /// Updates the preview player whenever the selected own image (or its image/video nature) changes.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlatformDetailsViewModel.SelectedOwnImage)
            || e.PropertyName == nameof(PlatformDetailsViewModel.IsSelectedOwnImageVideo))
        {
            UpdatePreviewVideo();
        }
        else if (e.PropertyName == nameof(PlatformDetailsViewModel.CoverageByPlatformSections))
        {
            UpdateCoverageSections();
        }
        // Se aplica ANTES de que lleguen las series/secciones nuevas (el VM fija AnimateCoverageByPlatformChart primero).
        else if (e.PropertyName == nameof(PlatformDetailsViewModel.AnimateCoverageByPlatformChart))
        {
            ApplyCoverageChartAnimation();
        }
    }

    /// <summary>
    /// Releases the player and unsubscribes when the control leaves the visual tree, so no video keeps
    /// playing in the background and nothing leaks.
    /// </summary>
    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            ViewModel.SharedDataService.SelectedPlatformChanged -= OnSelectedPlatformChanged;
            ViewModel.SharedDataService.SelectedImageSetChanged -= OnSelectedImageSetChanged;
        }

        _entranceCts?.Cancel();
        _entranceAnimation?.Cancel();
        _logoFadeAnimation?.Cancel();

        StopPreviewVideo();

        if (_videoPlayer != null)
        {
            _videoPlayer.MediaOpened -= OnVideoMediaOpened;
            _videoPlayer.Dispose();
            _videoPlayer = null;
        }
    }

    /// <summary>
    /// Keeps the selected-image preview area at a fixed 16:9 ratio by deriving its height from its current
    /// width. The width is driven by the (resizable) platform details column, so the ratio is preserved as
    /// the column gets wider or narrower. Setting the height does not change the width, so this converges
    /// without a layout loop. Targets the element that raised the event (the image area), so the thumbnail
    /// strip below it — now inside the same frame — does not affect the image proportions.
    /// </summary>
    private void PreviewFrame_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is not FrameworkElement element)
            return;

        var targetHeight = e.NewSize.Width * 9.0 / 16.0;

        if (double.IsNaN(element.Height) || Math.Abs(element.Height - targetHeight) > 0.5)
            element.Height = targetHeight;
    }
    #endregion

    #region Coverage-by-platform chart
    /// <summary>Abre el TeachingTip de ayuda de la gráfica de cobertura por plataforma.</summary>
    private void CoverageByPlatformHelp_Click(object? sender, RoutedEventArgs e) => CoverageByPlatformHelpTip.IsOpen = true;

    /// <summary>
    /// Carga la configuración del view model (gráfica activa + tipo/orden/Top X de las 3 gráficas con toolbar) una
    /// sola vez por instancia, tras cargarse el control y restaurarse los ajustes de disco.
    /// </summary>
    private void EnsureConfigurationLoaded() => _configGate.Ensure(ViewModel);

    /// <summary>Vuelca <see cref="PlatformDetailsViewModel.CoverageByPlatformSections"/> en la propiedad Sections del chart.</summary>
    private void UpdateCoverageSections()
        => _coverageGlue.UpdateSections(CoverageByPlatformChart, ViewModel?.CoverageByPlatformSections);

    /// <summary>
    /// Ajusta la velocidad de animación del chart de cobertura: cero al cambiar de plataforma (solo se mueve el
    /// resaltado → instantáneo) y la velocidad por defecto cuando hay recálculo real (alta/baja de imágenes propias).
    /// </summary>
    private void ApplyCoverageChartAnimation()
    {
        if (ViewModel is null)
            return;

        _coverageGlue.ApplyAnimation(CoverageByPlatformChart, ViewModel.AnimateCoverageByPlatformChart);
    }
    #endregion

    #region Entrance animation
    /// <summary>
    /// Plays the content entrance (everything except the background fanart). The content is hidden
    /// immediately and stays hidden for <see cref="EntrancePreDelayMs"/> (killing the flicker of the
    /// previous platform's content), then slides up from <see cref="EntranceSlideOffset"/> px while fading
    /// in. The logo fades in slightly later (<see cref="LogoFadeDelayMs"/>) and scales up from
    /// <see cref="LogoScaleStart"/> to 1, starting with the content opacity and ending 200 ms after it.
    /// A cancellation token aborts a pending entrance when the platform changes again mid-animation.
    /// </summary>
    private async void RunEntranceAnimation()
    {
        _entranceCts?.Cancel();
        _entranceAnimation?.Cancel();
        _logoFadeAnimation?.Cancel();

        var cts = new CancellationTokenSource();
        _entranceCts = cts;

        // Hidden initial state, applied immediately.
        ContentTranslate.X = 0;
        ContentTranslate.Y = EntranceSlideOffset;
        LogoScale.ScaleX = LogoScale.ScaleY = LogoScaleStart;
        PlatformLogo.Opacity = 0;
        PlatformName.Opacity = 0;
        PreviewFrame.Opacity = 0;
        StatsPanel.Opacity = 0;
        CoveragePanel.Opacity = 0;

        // Collapse and stop the video during the transition. MediaPlayerElement composites video on a
        // separate swap chain that does not honour XAML opacity in lockstep, so merely fading the frame to 0
        // still flashes a video frame when switching platforms. Removing it from rendering avoids that; it
        // is restored after the pre-delay.
        StopPreviewVideo();
        PreviewVideo.Visibility = Visibility.Collapsed;

        try
        {
            await Task.Delay(EntrancePreDelayMs, cts.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        // Restore the video (if the new selection is one) now that the previous frame is gone.
        UpdatePreviewVideo();

        // Content slides up and fades in; the logo scales up from this same moment, ending a bit later.
        _entranceAnimation = AnimationService.RunAnimations(new[]
        {
            AnimationService.CreateTranslateAnimation(ContentTranslate, 0, 0, EntranceSlideOffset, 0, EntranceDurationMs),
            AnimationService.CreateOpacityAnimation(PlatformName, 0, 1, EntranceDurationMs),
            AnimationService.CreateOpacityAnimation(PreviewFrame, 0, 1, EntranceDurationMs),
            AnimationService.CreateOpacityAnimation(StatsPanel, 0, 1, EntranceDurationMs),
            AnimationService.CreateOpacityAnimation(CoveragePanel, 0, 1, EntranceDurationMs),
            AnimationService.CreateScaleAnimation(LogoScale, LogoScaleStart, 1, LogoScaleDurationMs)
        });

        // The logo becomes visible a bit later than the rest of the content.
        try
        {
            await Task.Delay(LogoFadeDelayMs, cts.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        _logoFadeAnimation = AnimationService.CreateOpacityAnimation(PlatformLogo, 0, 1, LogoFadeDurationMs);
        _logoFadeAnimation.Start();
    }
    #endregion

    #region Video preview
    /// <summary>
    /// Plays the platform video (muted, looping, autoplay) in the preview when the selected own image is the
    /// video; otherwise stops and releases the current playback.
    /// </summary>
    private void UpdatePreviewVideo()
    {
        var selected = ViewModel?.SelectedOwnImage;

        // Cualquier cambio de selección reinicia el estado de la chapa de "play" (solo se muestra al pausar).
        PlayBadge.Visibility = Visibility.Collapsed;

        if (ViewModel?.IsSelectedOwnImageVideo == true && selected?.File is string path && File.Exists(path))
        {
            EnsureVideoPlayer();
            // Keep the video hidden until its first frame is ready (OnVideoMediaOpened). Until then the
            // poster (the still frame on the Image underneath) shows, so the swap chain never flashes black.
            PreviewVideo.Visibility = Visibility.Collapsed;
            // new Uri(path) puede lanzar UriFormatException con rutas con caracteres raros; Uri.TryCreate evita
            // el crash: si no es una URI válida, no se reproduce el vídeo (el póster sigue visible).
            if (Uri.TryCreate(path, UriKind.Absolute, out Uri? videoUri))
                SetVideoSource(MediaSource.CreateFromUri(videoUri));
        }
        else
        {
            StopPreviewVideo();
            PreviewVideo.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Reveals the video only once its first frame has decoded, so it appears over the poster without a
    /// black flash. Marshalled to the UI thread (the event is raised off it).
    /// </summary>
    private void OnVideoMediaOpened(MediaPlayer sender, object args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (ViewModel?.IsSelectedOwnImageVideo == true)
            {
                PreviewVideo.Visibility = Visibility.Visible;
                PlayBadge.Visibility = Visibility.Collapsed;   // arranca reproduciendo: sin chapa de "play"
            }
        });
    }

    /// <summary>
    /// El vídeo de plataforma se reproduce sin controles de transporte; un toque sobre él alterna play/pausa
    /// (un toque pausa, el siguiente reanuda).
    /// </summary>
    private void PreviewVideo_Tapped(object? sender, TappedRoutedEventArgs e)
    {
        if (_videoPlayer == null || ViewModel?.IsSelectedOwnImageVideo != true)
            return;

        if (_videoPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
        {
            _videoPlayer.Pause();
            // Pausado: muestra la chapa de "play" sobre el vídeo para indicar que está detenido.
            PlayBadge.Visibility = Visibility.Visible;
        }
        else
        {
            _videoPlayer.Play();
            PlayBadge.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Aplica al reproductor el volumen efectivo de la ficha (<see cref="PlatformDetailsViewModel.EffectiveVideoVolume"/>):
    /// el global, o 0 si hay un set de vídeo seleccionado (el dashboard manda). Se llama al crear el reproductor y en
    /// caliente cuando cambia el set seleccionado, para que no suenen dos vídeos a la vez.
    /// </summary>
    private void ApplyVideoVolume()
    {
        if (_videoPlayer == null)
            return;

        double volume = ViewModel?.EffectiveVideoVolume ?? 0;
        _videoPlayer.Volume = Math.Clamp(volume / 100.0, 0.0, 1.0);
        _videoPlayer.IsMuted = volume <= 0;
    }

    /// <summary>
    /// El set de medios seleccionado cambió: re-evalúa el volumen efectivo del vídeo de la ficha para cederle el
    /// audio al dashboard si ahora hay un vídeo seleccionado (o recuperarlo en caso contrario).
    /// </summary>
    private void OnSelectedImageSetChanged(object? sender, SharedDataService.ImageSetChangedEventArgs e)
    {
        ApplyVideoVolume();
    }

    /// <summary>
    /// Creates and wires the looping, auto-playing media player on first use. El volumen lo aporta el setting global
    /// a través de <see cref="ApplyVideoVolume"/>; si hay un set de vídeo seleccionado el dashboard tiene prioridad y
    /// la ficha va en silencio (ver <see cref="PlatformDetailsViewModel.EffectiveVideoVolume"/>).
    /// </summary>
    private void EnsureVideoPlayer()
    {
        if (_videoPlayer != null)
        {
            return;
        }

        _videoPlayer = new MediaPlayer
        {
            IsLoopingEnabled = true,
            AutoPlay = true
        };
        ApplyVideoVolume();

        _videoPlayer.MediaOpened += OnVideoMediaOpened;
        _videoPlayer.MediaFailed += OnVideoMediaFailed;
        PreviewVideo.SetMediaPlayer(_videoPlayer);
    }

    /// <summary>
    /// Un vídeo corrupto o con códec no soportado dejaba el preview congelado sin rastro. Lo registramos; el
    /// póster sigue visible porque PreviewVideo no se revela hasta OnVideoMediaOpened.
    /// </summary>
    private void OnVideoMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        MM4LB.Services.ExceptionService.LogToFile(args.ExtendedErrorCode, $"Platform video playback failed ({args.Error}): {args.ErrorMessage}");
    }

    /// <summary>
    /// Swaps the player's source, disposing the previous one.
    /// </summary>
    private void SetVideoSource(MediaSource source)
    {
        var previous = _videoSource;

        _videoSource = source;
        _videoPlayer!.Source = source;

        previous?.Dispose();
    }

    /// <summary>
    /// Pauses playback and releases the current source.
    /// </summary>
    private void StopPreviewVideo()
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

        // Al parar, oculta la chapa de "play" (solo tiene sentido sobre un vídeo pausado).
        PlayBadge.Visibility = Visibility.Collapsed;
    }
    #endregion
}
