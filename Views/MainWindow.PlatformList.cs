using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using MM4LB.Controls.ViewModels;
using MM4LB.Services;

namespace MM4LB.Views;

/// <summary>
/// Contains the layout and animation logic for the platform list section
/// displayed in the left column of the main window.
/// </summary>
public sealed partial class MainWindow
{
    #region Constants
    private const double PlatformListStarWeight = 2;
    private const int PlatformListHeightAnimationDuration = 500;
    #endregion

    #region Subscribed events
    /// <summary>
    /// Handles changes in the platform list display mode.
    /// 
    /// When <see cref="PlatformListViewModel.BehavesAsList"/> changes, the platform selector
    /// switches between list mode and compact mode. The row height is animated so the transition
    /// does not produce an abrupt layout jump.
    /// </summary>
    /// <param name="sender"> The source object that raised the property change notification. </param>
    /// <param name="e"> Information about the property that changed. </param>
    private void OnPlatformListViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PlatformListViewModel.BehavesAsList))
            return;

        _viewModel.IsAnimating = true;
        bool behavesAsList = _viewModel.PlatformListViewModel.BehavesAsList;

        double startHeight = rowPlatformList.ActualHeight;
        if (startHeight <= 0)
        {
            RestorePlatformListLayout();

            _viewModel.IsAnimating = false;
            return;
        }

        // Lock the current row height in pixels before animating it.
        rowPlatformList.Height = new GridLength(startHeight, GridUnitType.Pixel);
        colPlatformsAndGames.UpdateLayout();
        // Apply the final display mode temporarily so the target row height can be measured.
        plPlatforms.ApplyPlatformDisplayMode(behavesAsList);

        double targetHeight = MeasurePlatformListRowTargetHeight(behavesAsList, startHeight);

        // Prepare the internal cross-fade between the list and compact platform selector.
        plPlatforms.PreparePlatformListDisplayTransition(behavesAsList);

        if (Math.Abs(startHeight - targetHeight) < 1)
        {
            plPlatforms.AnimatePlatformListDisplayTransition(behavesAsList);
            RestorePlatformListLayout();
            _viewModel.IsAnimating = false;
            return;
        }

        var animations = new[]
        {
            AnimationService.CreateDoubleAnimation(v => rowPlatformList.Height = new GridLength(Math.Max(0, v), GridUnitType.Pixel), startHeight, targetHeight, PlatformListHeightAnimationDuration)
        };
        AnimationService.RunAnimations(animations, () =>
        {
            rowPlatformList.Height = behavesAsList ? new GridLength(PlatformListStarWeight, GridUnitType.Star) : GridLength.Auto;
            colPlatformsAndGames.UpdateLayout();
            _viewModel.IsAnimating = false;
        });

        plPlatforms.AnimatePlatformListDisplayTransition(behavesAsList);
    }
    #endregion

    #region Layout
    /// <summary>
    /// Restores the platform list row layout without animation according to the current
    /// value of <see cref="PlatformListViewModel.BehavesAsList"/>.
    /// </summary>
    private void RestorePlatformListLayout()
    {
        bool behavesAsList = _viewModel.PlatformListViewModel.BehavesAsList;
        rowPlatformList.Height = behavesAsList ? new GridLength(PlatformListStarWeight, GridUnitType.Star) : GridLength.Auto;
        plPlatforms.ApplyPlatformDisplayMode(behavesAsList);
    }

    /// <summary>
    /// Measures the final height that the platform list row should have after switching
    /// between list mode and compact mode.
    /// </summary>
    /// <param name="behaveAsList"> Indicates whether the platform selector should behave as an expanded list. </param>
    /// <param name="lockedHeight"> The current row height, used to restore the row after temporarily applying
    /// the target layout for measurement. </param>
    /// <returns> The target row height, in pixels. </returns>
    private double MeasurePlatformListRowTargetHeight(bool behaveAsList, double lockedHeight)
    {
        rowPlatformList.Height = behaveAsList ? new GridLength(PlatformListStarWeight, GridUnitType.Star) : GridLength.Auto;
        colPlatformsAndGames.UpdateLayout();
        double targetHeight = rowPlatformList.ActualHeight;

        // Restore the locked pixel height so the animation starts from the current visual state.
        rowPlatformList.Height = new GridLength(lockedHeight, GridUnitType.Pixel);

        colPlatformsAndGames.UpdateLayout();

        return targetHeight;
    }
    #endregion
}