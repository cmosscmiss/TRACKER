using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MM4LB.Services;

namespace MM4LB.Views;

/// <summary>
/// Contains the layout, reveal animation, clipping, and relocation logic for the
/// GameList section.
///
/// The GameList can be docked in two positions, toggled from the toolbar:
///   * "home": below the PlatformList, inside the left column (<see cref="glGamesRevealHost"/>).
///   * "aside": in its own dedicated column to the right of the platforms column
///     (<see cref="glGamesSideRevealHost"/>, inside the animated <see cref="colGamesSide"/>).
///
/// Switching position plays a relay animation: the list slides out of the current host,
/// the side column animates its width, and the list slides in at the destination host.
/// </summary>
public sealed partial class MainWindow
{
    #region Constants
    private const double GameListStarWeight = 3;
    private const int GameListHeightAnimationDuration = 500;
    private const int GameListFadeInDuration = 300;
    private const int GameListFadeOutDuration = 250;

    // Ancho de la columna lateral; coincide con el de la columna de plataformas.
    private const double GamesSideColumnWidth = 350;
    private const int GamesSideColumnAnimationDuration = 400;
    #endregion

    #region Initialization / Disposal
    /// <summary>
    /// Subscribes to size changes on both GameList reveal hosts so their clipping regions
    /// stay aligned with the animated height.
    /// </summary>
    private void InitializeGameListBehavior()
    {
        glGamesRevealHost.SizeChanged += OnGameListRevealHostSizeChanged;
        glGamesSideRevealHost.SizeChanged += OnGameListSideRevealHostSizeChanged;
    }

    /// <summary>
    /// Unsubscribes GameList-related events when the main window is closed.
    /// </summary>
    private void DisposeGameListBehavior()
    {
        glGamesRevealHost.SizeChanged -= OnGameListRevealHostSizeChanged;
        glGamesSideRevealHost.SizeChanged -= OnGameListSideRevealHostSizeChanged;
    }
    #endregion

    #region Subscribed events
    /// <summary>
    /// Handles position changes requested from the ViewModel for the GameList section.
    ///
    /// This is a pure relocation between two always-visible docked positions; the list is
    /// never fully hidden. The relay animation runs in the opposite order depending on the
    /// target position.
    /// </summary>
    /// <param name="dockedAside"> True to move the list into its side column; false to move it back below the platforms. </param>
    private void OnGameListDockedAsideChanged(bool dockedAside)
    {
        _viewModel.IsAnimating = true;

        if (dockedAside)
        {
            RelocateGameListToAside();
        }
        else
        {
            RelocateGameListToHome();
        }
    }

    /// <summary>
    /// Updates the clipping region whenever the below-platforms reveal host changes size.
    /// </summary>
    private void OnGameListRevealHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateGameListRevealHostClip();
    }

    /// <summary>
    /// Updates the clipping region whenever the side-column reveal host changes size.
    /// </summary>
    private void OnGameListSideRevealHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateGameListSideRevealHostClip();
    }
    #endregion

    #region Relocation choreography
    /// <summary>
    /// Moves the GameList from below the platforms into its side column.
    ///
    /// All three tracks run together: the side column widens while the list hides below
    /// and simultaneously reveals in the side column.
    /// </summary>
    private void RelocateGameListToAside()
    {
        bool platformBehavesAsList = _viewModel.PlatformListViewModel.BehavesAsList;

        RunGameListRelay(
            resizeFrom: 0,
            resizeTo: GamesSideColumnWidth,
            hide: done => HideGameListBelow(platformBehavesAsList, done),
            reveal: done => ShowGameListSide(done));
    }

    /// <summary>
    /// Moves the GameList from its side column back below the platforms.
    ///
    /// Mirror of <see cref="RelocateGameListToAside"/>: all three tracks run together —
    /// the side column shrinks while the list hides in the side column and simultaneously
    /// reveals below the platforms.
    /// </summary>
    private void RelocateGameListToHome()
    {
        bool platformBehavesAsList = _viewModel.PlatformListViewModel.BehavesAsList;

        RunGameListRelay(
            resizeFrom: GamesSideColumnWidth,
            resizeTo: 0,
            hide: done => HideGameListSide(done),
            reveal: done => ShowGameListBelow(platformBehavesAsList, done));
    }

    /// <summary>
    /// Runs the relocation relay with three fully parallel tracks and clears the animating
    /// flag once all of them finish:
    ///   * Track 1: animate the side column width from <paramref name="resizeFrom"/> to <paramref name="resizeTo"/>.
    ///   * Track 2: hide the list at the source.
    ///   * Track 3: reveal the list at the destination.
    /// </summary>
    /// <param name="resizeFrom"> The starting side column width, in pixels. </param>
    /// <param name="resizeTo"> The target side column width, in pixels. </param>
    /// <param name="hide"> Hides the list at the source; invokes its callback when finished. </param>
    /// <param name="reveal"> Reveals the list at the destination; invokes its callback when finished. </param>
    private void RunGameListRelay(double resizeFrom, double resizeTo, Action<Action> hide, Action<Action> reveal)
    {
        int pending = 3;

        void OnTrackCompleted()
        {
            if (--pending == 0)
                _viewModel.IsAnimating = false;
        }

        // The three tracks run concurrently: resize the column, hide at the source and
        // reveal at the destination, so the whole relocation moves as one.
        AnimateGamesSideColumn(resizeFrom, resizeTo, OnTrackCompleted);
        hide(OnTrackCompleted);
        reveal(OnTrackCompleted);
    }

    /// <summary>
    /// Animates the width of the side column between two pixel widths.
    /// </summary>
    /// <param name="from"> The starting width, in pixels. </param>
    /// <param name="to"> The target width, in pixels. </param>
    /// <param name="onCompleted"> Invoked when the width animation finishes. </param>
    private void AnimateGamesSideColumn(double from, double to, Action? onCompleted)
    {
        colGamesSide.Width = new GridLength(Math.Max(0, from), GridUnitType.Pixel);

        var animation = AnimationService.CreateDoubleAnimation(
            v => colGamesSide.Width = new GridLength(Math.Max(0, v), GridUnitType.Pixel),
            from,
            to,
            GamesSideColumnAnimationDuration);

        AnimationService.RunAnimations(new[] { animation }, () =>
        {
            colGamesSide.Width = new GridLength(Math.Max(0, to), GridUnitType.Pixel);
            onCompleted?.Invoke();
        });
    }
    #endregion

    #region Show / Hide animation - Below platforms
    /// <summary>
    /// Shows the GameList below the platforms by animating its reveal height and opacity.
    /// </summary>
    /// <param name="platformBehavesAsList"> Indicates whether the PlatformList is currently displayed as an expanded list. </param>
    /// <param name="onCompleted"> Invoked when the reveal finishes. </param>
    private void ShowGameListBelow(bool platformBehavesAsList, Action? onCompleted)
    {
        PrepareGameListRevealForShow();

        double targetHeight = GetGameListTargetHeightForShow(platformBehavesAsList);
        if (targetHeight <= 0)
        {
            FinalizeGameListVisible();
            onCompleted?.Invoke();
            return;
        }

        SetGameListRevealHeight(0, updateRowHeight: platformBehavesAsList);

        var animations = new[]
        {
            AnimationService.CreateDoubleAnimation(v => SetGameListRevealHeight(v, updateRowHeight: platformBehavesAsList), 0, targetHeight, GameListHeightAnimationDuration),
            AnimationService.CreateOpacityAnimation(glGamesRevealHost, 0, 1, GameListFadeInDuration)
        };
        AnimationService.RunAnimations(animations, () =>
        {
            FinalizeGameListVisible();
            onCompleted?.Invoke();
        });
    }

    /// <summary>
    /// Hides the GameList below the platforms by animating its reveal height and opacity.
    /// </summary>
    /// <param name="platformBehavesAsList"> Indicates whether the PlatformList is currently displayed as an expanded list. </param>
    /// <param name="onCompleted"> Invoked when the hide finishes. </param>
    private void HideGameListBelow(bool platformBehavesAsList, Action? onCompleted)
    {
        glGamesRevealHost.IsHitTestVisible = false;
        double startHeight = GetGameListStartHeightForHide(platformBehavesAsList);

        if (startHeight <= 0)
        {
            FinalizeGameListHidden(platformBehavesAsList);
            onCompleted?.Invoke();
            return;
        }

        PrepareGameListRevealForHide(platformBehavesAsList, startHeight);

        var animations = new[]
        {
            AnimationService.CreateDoubleAnimation(v => SetGameListRevealHeight(v, updateRowHeight: platformBehavesAsList), startHeight, 0, GameListHeightAnimationDuration),
            AnimationService.CreateOpacityAnimation(glGamesRevealHost, glGamesRevealHost.Opacity, 0, GameListFadeOutDuration)
        };
        AnimationService.RunAnimations(animations, () =>
        {
            FinalizeGameListHidden(platformBehavesAsList);
            onCompleted?.Invoke();
        });
    }
    #endregion

    #region Show / Hide animation - Side column
    /// <summary>
    /// Shows the GameList inside the side column by animating its reveal height and opacity.
    /// The side column is expected to already be at its full width.
    /// </summary>
    /// <param name="onCompleted"> Invoked when the reveal finishes. </param>
    private void ShowGameListSide(Action? onCompleted)
    {
        PrepareGameListSideRevealForShow();

        double targetHeight = GetGameListSideTargetHeight();
        if (targetHeight <= 0)
        {
            FinalizeGameListSideVisible();
            onCompleted?.Invoke();
            return;
        }

        SetGameListSideRevealHeight(0);

        var animations = new[]
        {
            AnimationService.CreateDoubleAnimation(SetGameListSideRevealHeight, 0, targetHeight, GameListHeightAnimationDuration),
            AnimationService.CreateOpacityAnimation(glGamesSideRevealHost, 0, 1, GameListFadeInDuration)
        };
        AnimationService.RunAnimations(animations, () =>
        {
            FinalizeGameListSideVisible();
            onCompleted?.Invoke();
        });
    }

    /// <summary>
    /// Hides the GameList inside the side column by animating its reveal height and opacity.
    /// </summary>
    /// <param name="onCompleted"> Invoked when the hide finishes. </param>
    private void HideGameListSide(Action? onCompleted)
    {
        glGamesSideRevealHost.IsHitTestVisible = false;
        double startHeight = glGamesSideRevealHost.ActualHeight;

        if (startHeight <= 0)
        {
            ApplyGameListSideHiddenVisualState();
            onCompleted?.Invoke();
            return;
        }

        glGamesSideRevealHost.VerticalAlignment = VerticalAlignment.Bottom;
        glGamesSideRevealHost.Height = startHeight;
        UpdateGameListSideRevealHostClip();

        var animations = new[]
        {
            AnimationService.CreateDoubleAnimation(SetGameListSideRevealHeight, startHeight, 0, GameListHeightAnimationDuration),
            AnimationService.CreateOpacityAnimation(glGamesSideRevealHost, glGamesSideRevealHost.Opacity, 0, GameListFadeOutDuration)
        };
        AnimationService.RunAnimations(animations, () =>
        {
            ApplyGameListSideHiddenVisualState();
            onCompleted?.Invoke();
        });
    }
    #endregion

    #region Preparation / Finalization - Below platforms
    /// <summary>
    /// Prepares the reveal host before showing the GameList.
    /// </summary>
    private void PrepareGameListRevealForShow()
    {
        glGamesRevealHost.Visibility = Visibility.Visible;
        glGamesRevealHost.Opacity = 0;
        glGamesRevealHost.Height = 0;
        glGamesRevealHost.VerticalAlignment = VerticalAlignment.Bottom;
        glGamesRevealHost.IsHitTestVisible = false;
        UpdateGameListRevealHostClip();
    }

    /// <summary>
    /// Prepares the reveal host and row height before hiding the GameList.
    /// </summary>
    /// <param name="platformBehavesAsList"> Indicates whether the PlatformList is currently displayed as an expanded list. </param>
    /// <param name="startHeight"> The current visual height from which the hide animation should start. </param>
    private void PrepareGameListRevealForHide(bool platformBehavesAsList, double startHeight)
    {
        glGamesRevealHost.VerticalAlignment = VerticalAlignment.Bottom;
        glGamesRevealHost.Height = startHeight;

        // In expanded PlatformList mode, the row is animated. In compact mode, the row
        // remains as available space and only the inner reveal host is animated.
        rowGameList.Height = platformBehavesAsList ? new GridLength(startHeight, GridUnitType.Pixel) : new GridLength(GameListStarWeight, GridUnitType.Star);

        colPlatformsAndGames.UpdateLayout();
        UpdateGameListRevealHostClip();
    }

    /// <summary>
    /// Applies the final visible state after the GameList show animation completes.
    /// </summary>
    private void FinalizeGameListVisible()
    {
        rowGameList.Height = new GridLength(GameListStarWeight, GridUnitType.Star);
        glGamesRevealHost.Visibility = Visibility.Visible;
        glGamesRevealHost.Opacity = 1;
        glGamesRevealHost.Height = double.NaN;
        glGamesRevealHost.VerticalAlignment = VerticalAlignment.Stretch;
        glGamesRevealHost.IsHitTestVisible = true;

        colPlatformsAndGames.UpdateLayout();
        UpdateGameListRevealHostClip();
    }

    /// <summary>
    /// Applies the final hidden state after the GameList hide animation completes.
    /// </summary>
    /// <param name="platformBehavesAsList"> Indicates whether the PlatformList is currently displayed as an expanded list. </param>
    private void FinalizeGameListHidden(bool platformBehavesAsList)
    {
        ApplyGameListHiddenVisualState(platformBehavesAsList);
    }

    /// <summary>
    /// Applies the hidden visual state for the GameList section without starting an animation.
    /// </summary>
    /// <param name="platformBehavesAsList"> Indicates whether the PlatformList is currently displayed as an expanded list. </param>
    private void ApplyGameListHiddenVisualState(bool platformBehavesAsList)
    {
        glGamesRevealHost.Visibility = Visibility.Collapsed;
        glGamesRevealHost.Opacity = 0;
        glGamesRevealHost.Height = 0;
        glGamesRevealHost.VerticalAlignment = VerticalAlignment.Bottom;
        glGamesRevealHost.IsHitTestVisible = false;

        rowGameList.Height = GetHiddenGameListRowHeight(platformBehavesAsList);
        colPlatformsAndGames.UpdateLayout();
        UpdateGameListRevealHostClip();
    }
    #endregion

    #region Preparation / Finalization - Side column
    /// <summary>
    /// Prepares the side reveal host before showing the GameList in the side column.
    /// </summary>
    private void PrepareGameListSideRevealForShow()
    {
        glGamesSideRevealHost.Visibility = Visibility.Visible;
        glGamesSideRevealHost.Opacity = 0;
        glGamesSideRevealHost.Height = 0;
        glGamesSideRevealHost.VerticalAlignment = VerticalAlignment.Bottom;
        glGamesSideRevealHost.IsHitTestVisible = false;
        UpdateGameListSideRevealHostClip();
    }

    /// <summary>
    /// Applies the final visible state after the side GameList show animation completes.
    /// </summary>
    private void FinalizeGameListSideVisible()
    {
        glGamesSideRevealHost.Visibility = Visibility.Visible;
        glGamesSideRevealHost.Opacity = 1;
        glGamesSideRevealHost.Height = double.NaN;
        glGamesSideRevealHost.VerticalAlignment = VerticalAlignment.Stretch;
        glGamesSideRevealHost.IsHitTestVisible = true;

        UpdateGameListSideRevealHostClip();
    }

    /// <summary>
    /// Applies the hidden visual state for the side GameList host without starting an animation.
    /// </summary>
    private void ApplyGameListSideHiddenVisualState()
    {
        glGamesSideRevealHost.Visibility = Visibility.Collapsed;
        glGamesSideRevealHost.Opacity = 0;
        glGamesSideRevealHost.Height = 0;
        glGamesSideRevealHost.VerticalAlignment = VerticalAlignment.Bottom;
        glGamesSideRevealHost.IsHitTestVisible = false;

        UpdateGameListSideRevealHostClip();
    }
    #endregion

    #region Measurement / Layout helpers
    /// <summary>
    /// Calculates the target height for the GameList show animation.
    /// </summary>
    /// <param name="platformBehavesAsList"> Indicates whether the PlatformList is currently displayed as an expanded list. </param>
    /// <returns> The target reveal height, in pixels. </returns>
    private double GetGameListTargetHeightForShow(bool platformBehavesAsList)
    {
        if (platformBehavesAsList)
        {
            rowGameList.Height = new GridLength(0, GridUnitType.Pixel);
            colPlatformsAndGames.UpdateLayout();
            return MeasureGameListRowTargetHeight();
        }

        rowGameList.Height = new GridLength(GameListStarWeight, GridUnitType.Star);
        colPlatformsAndGames.UpdateLayout();

        return rowGameList.ActualHeight;
    }

    /// <summary>
    /// Gets the current height from which the GameList hide animation should start.
    /// </summary>
    /// <param name="platformBehavesAsList"> Indicates whether the PlatformList is currently displayed as an expanded list. </param>
    /// <returns> The starting reveal height, in pixels. </returns>
    private double GetGameListStartHeightForHide(bool platformBehavesAsList)
    {
        return platformBehavesAsList ? rowGameList.ActualHeight : glGamesRevealHost.ActualHeight;
    }

    /// <summary>
    /// Updates the reveal host height, and optionally the containing row height,
    /// during the show or hide animation.
    /// </summary>
    /// <param name="value"> The requested height value. </param>
    /// <param name="updateRowHeight"> If true, the row height is animated together with the reveal host height. </param>
    private void SetGameListRevealHeight(double value, bool updateRowHeight)
    {
        double height = Math.Max(0, value);
        if (updateRowHeight)
        {
            rowGameList.Height = new GridLength(height, GridUnitType.Pixel);
        }

        glGamesRevealHost.Height = height;

        UpdateGameListRevealHostClip();
    }

    /// <summary>
    /// Updates the side reveal host height during the show or hide animation.
    /// </summary>
    /// <param name="value"> The requested height value. </param>
    private void SetGameListSideRevealHeight(double value)
    {
        glGamesSideRevealHost.Height = Math.Max(0, value);
        UpdateGameListSideRevealHostClip();
    }

    /// <summary>
    /// Measures the height the side reveal host occupies when stretched to fill its column.
    /// </summary>
    /// <returns> The target reveal height, in pixels. </returns>
    private double GetGameListSideTargetHeight()
    {
        glGamesSideRevealHost.VerticalAlignment = VerticalAlignment.Stretch;
        glGamesSideRevealHost.Height = double.NaN;
        glGamesSideRevealHost.UpdateLayout();

        double targetHeight = glGamesSideRevealHost.ActualHeight;

        // Restore the closed state so the animation can start from zero.
        glGamesSideRevealHost.VerticalAlignment = VerticalAlignment.Bottom;
        glGamesSideRevealHost.Height = 0;
        glGamesSideRevealHost.UpdateLayout();

        return targetHeight;
    }

    /// <summary>
    /// Restores the GameList layout from the saved ViewModel state without animation.
    /// </summary>
    private void RestoreGameListLayout()
    {
        bool platformBehavesAsList = _viewModel.PlatformListViewModel.BehavesAsList;

        if (_viewModel.IsGameListDockedAside)
        {
            // Below host hidden; the platform list takes over the left column.
            ApplyGameListHiddenVisualState(platformBehavesAsList);

            // Side column open and side host visible.
            colGamesSide.Width = new GridLength(GamesSideColumnWidth, GridUnitType.Pixel);
            FinalizeGameListSideVisible();
        }
        else
        {
            // Side column closed and side host hidden.
            ApplyGameListSideHiddenVisualState();
            colGamesSide.Width = new GridLength(0, GridUnitType.Pixel);

            // Below host visible.
            rowGameList.Height = new GridLength(GameListStarWeight, GridUnitType.Star);
            glGamesRevealHost.Visibility = Visibility.Visible;
            glGamesRevealHost.Opacity = 1;
            glGamesRevealHost.Height = double.NaN;
            glGamesRevealHost.VerticalAlignment = VerticalAlignment.Stretch;
            glGamesRevealHost.IsHitTestVisible = true;

            UpdateGameListRevealHostClip();
        }
    }

    /// <summary>
    /// Applies the correct row height for the current combination of GameList position
    /// and PlatformList display mode.
    /// </summary>
    /// <param name="platformBehavesAsList"> Indicates whether the PlatformList is currently displayed as an expanded list. </param>
    private void ApplyGameListRowHeightForCurrentState(bool platformBehavesAsList)
    {
        bool visibleBelow = !_viewModel.IsGameListDockedAside;
        rowGameList.Height = visibleBelow ? new GridLength(GameListStarWeight, GridUnitType.Star) : GetHiddenGameListRowHeight(platformBehavesAsList);
    }

    /// <summary>
    /// Returns the row height that should be used when the GameList is not below the platforms.
    /// </summary>
    /// <param name="platformBehavesAsList"> Indicates whether the PlatformList is currently displayed as an expanded list. </param>
    /// <returns> Auto when the PlatformList is expanded; star height when the PlatformList is compact. </returns>
    private GridLength GetHiddenGameListRowHeight(bool platformBehavesAsList)
    {
        return platformBehavesAsList ? GridLength.Auto : new GridLength(GameListStarWeight, GridUnitType.Star);
    }

    /// <summary>
    /// Measures the final row height that the GameList should occupy when visible.
    /// </summary>
    /// <returns> The target row height, in pixels. </returns>
    private double MeasureGameListRowTargetHeight()
    {
        rowGameList.Height = new GridLength(GameListStarWeight, GridUnitType.Star);

        colPlatformsAndGames.UpdateLayout();

        double targetHeight = rowGameList.ActualHeight;

        // Restore a closed pixel height so the animation can start from zero.
        rowGameList.Height = new GridLength(0, GridUnitType.Pixel);

        colPlatformsAndGames.UpdateLayout();

        return targetHeight;
    }

    /// <summary>
    /// Updates the clipping geometry of the below-platforms reveal host so the content does not
    /// render outside the animated bounds.
    /// </summary>
    private void UpdateGameListRevealHostClip()
    {
        glGamesRevealHost.Clip = new RectangleGeometry
        {
            Rect = new Windows.Foundation.Rect(0, 0, Math.Max(0, glGamesRevealHost.ActualWidth), Math.Max(0, glGamesRevealHost.ActualHeight))
        };
    }

    /// <summary>
    /// Updates the clipping geometry of the side-column reveal host so the content does not
    /// render outside the animated bounds.
    /// </summary>
    private void UpdateGameListSideRevealHostClip()
    {
        glGamesSideRevealHost.Clip = new RectangleGeometry
        {
            Rect = new Windows.Foundation.Rect(0, 0, Math.Max(0, glGamesSideRevealHost.ActualWidth), Math.Max(0, glGamesSideRevealHost.ActualHeight))
        };
    }
    #endregion
}
