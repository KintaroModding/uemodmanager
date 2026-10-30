using System.Windows;
using UEManager.Core.Storage;
using Xunit;

namespace UEManager.UI.Tests;

/// <summary>The window opens where it was last time, while that place is still on a screen.</summary>
public class WindowPlacementTests
{
    private static readonly Rect TwoScreens = new(0, 0, 3840, 1080);
    private static readonly Size Minimum = new(1000, 640);

    [Fact]
    public void A_place_on_a_screen_is_kept()
    {
        var bounds = MainWindow.Usable(new WindowPlacement(2100, 80, 1400, 900, false), TwoScreens, Minimum);

        Assert.Equal(new Rect(2100, 80, 1400, 900), bounds);
    }

    [Fact]
    public void A_place_on_a_screen_that_is_gone_opens_centred()
    {
        var oneScreen = new Rect(0, 0, 1920, 1080);

        Assert.Null(MainWindow.Usable(new WindowPlacement(2100, 80, 1400, 900, false), oneScreen, Minimum));
    }

    [Fact]
    public void The_title_bar_must_stay_on_a_screen()
    {
        Assert.Null(MainWindow.Usable(new WindowPlacement(100, -20, 1400, 900, false), TwoScreens, Minimum));
        Assert.Null(MainWindow.Usable(new WindowPlacement(100, 1060, 1400, 900, false), TwoScreens, Minimum));
    }

    [Fact]
    public void The_size_stays_within_the_minimum_and_the_screens()
    {
        var bounds = MainWindow.Usable(new WindowPlacement(10, 10, 300, 99999, true), TwoScreens, Minimum);

        Assert.Equal(new Rect(10, 10, 1000, 1080), bounds);
    }

    [Fact]
    public void A_broken_value_opens_centred()
    {
        Assert.Null(MainWindow.Usable(new WindowPlacement(double.NaN, 10, 1400, 900, false), TwoScreens, Minimum));
    }
}
