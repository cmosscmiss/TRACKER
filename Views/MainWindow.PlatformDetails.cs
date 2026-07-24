using System;
using Microsoft.UI.Xaml;
using MM4LB.Services;
using MM4LB.ViewModels;

namespace MM4LB.Views;

/// <summary>
/// Contains the layout, animation, persistence, and restoration logic for the
/// platform details panel displayed on the right side of the main window.
/// </summary>
public sealed partial class MainWindow
{
    #region Constants
    private const int PlatformDetailsFadeInDuration = 700;
    private const int PlatformDetailsFadeOutDuration = 300;
    private const int PlatformDetailsWidthAnimationDuration = 500;
    #endregion

    #region Attributes
    private GridLength _previousDetailsColumnWidth = new(MainWindowViewModel.PlatformDetailsMinWidth);
    #endregion

    #region Subscribed events
    /// <summary>
    /// Handles visibility changes requested from the ViewModel for the platform details panel.
    /// Starts the corresponding show or hide animation and temporarily blocks layout toggles
    /// while the animation is running.
    /// </summary>
    /// <param name="visible"> Indicates whether the platform details panel should be shown or hidden. </param>
    private void OnPlatformDetailsVisibilityChanged(bool visible)
    {
        _viewModel.IsAnimating = true;

        if (visible)
        {
            ShowPlatformDetails();
        }
        else
        {
            HidePlatformDetails();
        }
    }
    #endregion

    #region Show / Hide animation
    /// <summary>
    /// Shows the platform details panel by animating its opacity and restoring
    /// the column width to the last known visible width.
    /// </summary>
    private void ShowPlatformDetails()
    {
        var animations = new[]
        {
            AnimationService.CreateOpacityAnimation(pdcPlatformDetails, 0, 1, PlatformDetailsFadeInDuration),
            AnimationService.CreateDoubleAnimation(v => colPlatformDetails.Width = new GridLength(v), 0, _previousDetailsColumnWidth.Value, PlatformDetailsWidthAnimationDuration)
        };
        AnimationService.RunAnimations(animations, () =>
        {
            colPlatformDetails.MinWidth = MainWindowViewModel.PlatformDetailsMinWidth;
            _viewModel.IsAnimating = false;
        });
    }

    /// <summary>
    /// Hides the platform details panel by fading it out and animating its column
    /// width to zero. The current width is preserved so it can be restored later.
    /// </summary>
    private void HidePlatformDetails()
    {
        // Preserve the last visible width before collapsing the column.
        _previousDetailsColumnWidth = colPlatformDetails.Width;
        colPlatformDetails.MinWidth = 0;

        var animations = new[]
        {
            AnimationService.CreateOpacityAnimation(pdcPlatformDetails, 1, 0, PlatformDetailsFadeOutDuration),
            AnimationService.CreateDoubleAnimation(v => colPlatformDetails.Width = new GridLength(v), _previousDetailsColumnWidth.Value, 0, PlatformDetailsWidthAnimationDuration)
        };
        AnimationService.RunAnimations(animations, () =>
        {
            _viewModel.IsAnimating = false;
        });
    }
    #endregion

    #region Layout persistence / restoration
    /// <summary>
    /// Captures the current platform details panel width before closing the window,
    /// so the same layout can be restored on the next application start.
    /// </summary>
    private void CapturePlatformDetailsLayout()
    {
        var currentVisibleWidth = colPlatformDetails.ActualWidth;
        var previousVisibleWidth = _previousDetailsColumnWidth.Value;

        _viewModel.CapturePlatformDetailsLayout(currentVisibleWidth, previousVisibleWidth);
    }

    /// <summary>
    /// Restores the platform details panel layout from the saved configuration,
    /// applying the correct width, minimum width, and opacity without animation.
    /// </summary>
    private void RestorePlatformDetailsLayout()
    {
        var width = Math.Clamp(_viewModel.PlatformDetailsWidth, MainWindowViewModel.PlatformDetailsMinWidth, MainWindowViewModel.PlatformDetailsMaxWidth);

        _previousDetailsColumnWidth = new GridLength(width);

        if (_viewModel.IsPlatformDetailsVisible)
        {
            colPlatformDetails.MinWidth = MainWindowViewModel.PlatformDetailsMinWidth;
            colPlatformDetails.Width = new GridLength(width);
            pdcPlatformDetails.Opacity = 1;
        }
        else
        {
            colPlatformDetails.MinWidth = 0;
            colPlatformDetails.Width = new GridLength(0);
            pdcPlatformDetails.Opacity = 0;
        }
    }
    #endregion
}