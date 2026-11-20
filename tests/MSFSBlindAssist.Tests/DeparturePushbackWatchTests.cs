// tests/MSFSBlindAssist.Tests/DeparturePushbackWatchTests.cs
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Planning;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class DeparturePushbackWatchTests
{
    private static PlannedDeparturePlan CreateSamplePlan(double originLat, double originLon, double targetHdgMag) =>
        new()
        {
            AirportIcao = "SBBR",
            RunwayId = "11R",
            OriginLat = originLat,
            OriginLon = originLon,
            TargetHeadingMag = targetHdgMag,
            Pushback = new PushbackPlan { Direction = PushbackDirection.NoseRightTailLeft, PostPushbackHeadingMag = targetHdgMag },
            Route = new TaxiRoute(),
            IsArmed = true
        };

    [Fact]
    public void CheckPushbackComplete_ReturnsFalse_WhenPlanIsNull()
    {
        bool result = DeparturePushbackWatch.CheckPushbackComplete(
            null, currentLat: 0.0, currentLon: 0.0, currentHeadingMag: 100.0, groundSpeedKnots: 0.0, parkingBrakeSet: true);

        Assert.False(result);
    }

    [Fact]
    public void CheckPushbackComplete_ReturnsFalse_WhenNotArmed()
    {
        var plan = CreateSamplePlan(0.0, 0.0, 100.0);
        plan.IsArmed = false;

        bool result = DeparturePushbackWatch.CheckPushbackComplete(
            plan, currentLat: 0.0005, currentLon: 0.0, currentHeadingMag: 100.0, groundSpeedKnots: 0.0, parkingBrakeSet: true);

        Assert.False(result);
    }

    [Fact]
    public void CheckPushbackComplete_ReturnsFalse_WhenNotMovedFromStand()
    {
        var plan = CreateSamplePlan(0.0, 0.0, 100.0);

        // Aircraft only ~5m away from stand origin
        double currentLat = 0.00004;
        double currentLon = 0.0;

        bool result = DeparturePushbackWatch.CheckPushbackComplete(
            plan, currentLat, currentLon, currentHeadingMag: 100.0, groundSpeedKnots: 0.0, parkingBrakeSet: true);

        Assert.False(result);
    }

    [Fact]
    public void CheckPushbackComplete_ReturnsFalse_WhenStillMovingFast()
    {
        var plan = CreateSamplePlan(0.0, 0.0, 100.0);

        // Aircraft 40m away, aligned, but rolling at 6 knots and brake off
        double currentLat = 0.00036;
        double currentLon = 0.0;

        bool result = DeparturePushbackWatch.CheckPushbackComplete(
            plan, currentLat, currentLon, currentHeadingMag: 100.0, groundSpeedKnots: 6.0, parkingBrakeSet: false);

        Assert.False(result);
    }

    [Fact]
    public void CheckPushbackComplete_ReturnsFalse_WhenHeadingNotAligned()
    {
        var plan = CreateSamplePlan(0.0, 0.0, 100.0);

        // Aircraft 40m away, stopped, but heading 180 (80 deg difference > 45 deg)
        double currentLat = 0.00036;
        double currentLon = 0.0;

        bool result = DeparturePushbackWatch.CheckPushbackComplete(
            plan, currentLat, currentLon, currentHeadingMag: 180.0, groundSpeedKnots: 0.0, parkingBrakeSet: true);

        Assert.False(result);
    }

    [Fact]
    public void CheckPushbackComplete_ReturnsTrue_WhenMovedStoppedAndAligned()
    {
        var plan = CreateSamplePlan(0.0, 0.0, 100.0);

        // Aircraft 40m away, stopped (ground speed 0.5 kt), heading 120 (20 deg difference <= 45 deg)
        double currentLat = 0.00036;
        double currentLon = 0.0;

        bool result = DeparturePushbackWatch.CheckPushbackComplete(
            plan, currentLat, currentLon, currentHeadingMag: 120.0, groundSpeedKnots: 0.5, parkingBrakeSet: false);

        Assert.True(result);
    }

    [Fact]
    public void CheckPushbackComplete_ReturnsTrue_WhenParkingBrakeSetEvenIfSpeedSlightlyAboveZero()
    {
        var plan = CreateSamplePlan(0.0, 0.0, 100.0);

        // Aircraft 40m away, parking brake set, heading 90 (10 deg difference <= 45 deg)
        double currentLat = 0.00036;
        double currentLon = 0.0;

        bool result = DeparturePushbackWatch.CheckPushbackComplete(
            plan, currentLat, currentLon, currentHeadingMag: 90.0, groundSpeedKnots: 1.8, parkingBrakeSet: true);

        Assert.True(result);
    }
}

