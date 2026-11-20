// tests/MSFSBlindAssist.Tests/PushbackGeometryPlannerTests.cs
using System.Collections.Generic;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Planning;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class PushbackGeometryPlannerTests
{
    [Fact]
    public void CalculatePushback_NoseRightTailLeft_WhenClockwiseTurn()
    {
        var spot = new ParkingSpot
        {
            Heading = 90.0,
            Type = 1 // Gate
        };

        var route = new TaxiRoute
        {
            TaxiwaySequence = new List<string> { "B", "A" },
            Segments = new List<TaxiRouteSegment>
            {
                new()
                {
                    TaxiwayName = "B",
                    BearingDegrees = 180.0
                }
            }
        };

        // MagVar -10 (West): Mag = 180 - (-10) = 190
        var plan = PushbackGeometryPlanner.CalculatePushback(spot, route, magVar: -10.0);

        Assert.True(plan.NeedsPushback);
        Assert.Equal(PushbackDirection.NoseRightTailLeft, plan.Direction);
        Assert.Equal("B", plan.InitialTaxiway);
        Assert.Equal(190.0, plan.PostPushbackHeadingMag);
        Assert.Equal(180.0, plan.PostPushbackHeadingTrue);
        Assert.Equal(90.0, plan.TurnAngleDeg);
        Assert.Contains("Nose right, tail left", plan.Summary);
        Assert.Contains("190 degrees magnetic", plan.Summary);
    }

    [Fact]
    public void CalculatePushback_NoseLeftTailRight_WhenCounterClockwiseTurn()
    {
        var spot = new ParkingSpot
        {
            Heading = 90.0,
            Type = 1 // Gate
        };

        var route = new TaxiRoute
        {
            TaxiwaySequence = new List<string> { "Alpha" },
            Segments = new List<TaxiRouteSegment>
            {
                new()
                {
                    TaxiwayName = "Alpha",
                    BearingDegrees = 0.0
                }
            }
        };

        // MagVar +15 (East): Mag = 0 - 15 = 345
        var plan = PushbackGeometryPlanner.CalculatePushback(spot, route, magVar: 15.0);

        Assert.True(plan.NeedsPushback);
        Assert.Equal(PushbackDirection.NoseLeftTailRight, plan.Direction);
        Assert.Equal("Alpha", plan.InitialTaxiway);
        Assert.Equal(345.0, plan.PostPushbackHeadingMag);
        Assert.Equal(0.0, plan.PostPushbackHeadingTrue);
        Assert.Equal(-90.0, plan.TurnAngleDeg);
        Assert.Contains("Nose left, tail right", plan.Summary);
        Assert.Contains("345 degrees magnetic", plan.Summary);
    }

    [Fact]
    public void CalculatePushback_Straight_WhenTurnIsMinimal()
    {
        var spot = new ParkingSpot
        {
            Heading = 180.0,
            Type = 1 // Gate
        };

        var route = new TaxiRoute
        {
            TaxiwaySequence = new List<string> { "K" },
            Segments = new List<TaxiRouteSegment>
            {
                new()
                {
                    TaxiwayName = "K",
                    BearingDegrees = 185.0
                }
            }
        };

        var plan = PushbackGeometryPlanner.CalculatePushback(spot, route, magVar: 0.0);

        Assert.True(plan.NeedsPushback);
        Assert.Equal(PushbackDirection.Straight, plan.Direction);
        Assert.Equal(185.0, plan.PostPushbackHeadingMag);
        Assert.Equal(5.0, plan.TurnAngleDeg);
        Assert.Contains("Straight back", plan.Summary);
    }

    [Fact]
    public void CalculatePushback_DirectTaxiOut_ForGaRampWithStraightExit()
    {
        var spot = new ParkingSpot
        {
            Heading = 270.0,
            Type = 2 // Ramp GA Small (ParkingTypes.IsGaRamp returns true)
        };

        var route = new TaxiRoute
        {
            TaxiwaySequence = new List<string> { "D" },
            Segments = new List<TaxiRouteSegment>
            {
                new()
                {
                    TaxiwayName = "D",
                    BearingDegrees = 275.0
                }
            }
        };

        var plan = PushbackGeometryPlanner.CalculatePushback(spot, route, magVar: 0.0);

        Assert.False(plan.NeedsPushback);
        Assert.Equal(PushbackDirection.NoneDirectTaxiOut, plan.Direction);
        Assert.Equal("D", plan.InitialTaxiway);
        Assert.Contains("Direct taxi-out", plan.Summary);
    }

    [Fact]
    public void CalculatePushback_EmptyRoute_ReturnsNonPushbackPlan()
    {
        var spot = new ParkingSpot { Heading = 100.0, Type = 1 };
        var plan = PushbackGeometryPlanner.CalculatePushback(spot, null, magVar: 0.0);

        Assert.False(plan.NeedsPushback);
        Assert.Equal(PushbackDirection.NoneDirectTaxiOut, plan.Direction);
        Assert.Contains("No departure taxi route available", plan.Summary);
    }

    [Theory]
    [InlineData(100.0, 130.0, 45.0, true)]
    [InlineData(100.0, 70.0, 45.0, true)]
    [InlineData(10.0, 350.0, 45.0, true)]   // Across 360 wrap
    [InlineData(350.0, 10.0, 45.0, true)]   // Across 360 wrap reverse
    [InlineData(100.0, 150.0, 45.0, false)] // 50 deg diff > 45
    [InlineData(100.0, 50.0, 45.0, false)]  // 50 deg diff > 45
    public void IsAlignedForTaxi_ValidatesWithinTolerance(double currentHdg, double targetHdg, double tolerance, bool expected)
    {
        bool actual = PushbackGeometryPlanner.IsAlignedForTaxi(currentHdg, targetHdg, tolerance);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void HasMovedFromPosition_ReturnsTrueWhenMovedBeyondThreshold()
    {
        // Standard coordinates ~50 meters apart
        double lat1 = -15.8690;
        double lon1 = -47.9200;
        double lat2 = -15.8694;
        double lon2 = -47.9200;

        bool moved = PushbackGeometryPlanner.HasMovedFromPosition(lat2, lon2, lat1, lon1, minDistanceMeters: 15.0);
        Assert.True(moved);
    }

    [Fact]
    public void HasMovedFromPosition_ReturnsFalseWhenStillAtOrigin()
    {
        double lat = -15.8690;
        double lon = -47.9200;

        bool moved = PushbackGeometryPlanner.HasMovedFromPosition(lat, lon, lat, lon, minDistanceMeters: 15.0);
        Assert.False(moved);
    }
}

