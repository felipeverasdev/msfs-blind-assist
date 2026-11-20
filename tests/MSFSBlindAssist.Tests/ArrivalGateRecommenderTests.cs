// tests/MSFSBlindAssist.Tests/ArrivalGateRecommenderTests.cs
using System.Collections.Generic;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Briefing;
using MSFSBlindAssist.Navigation.Planning;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class ArrivalGateRecommenderTests
{
    private static AircraftProfile CreateProfile(string typeCode, double wingspan, IcaoCodeLetter letter, bool isFreighter) =>
        new(typeCode, typeCode, wingspan, letter, isFreighter, 130.0);

    [Fact]
    public void RecommendGates_PrefersAirlineMatch()
    {
        var a320 = CreateProfile("A320", 35.8, IcaoCodeLetter.C, isFreighter: false);

        var spot1 = new ParkingSpot
        {
            Name = "Gate",
            Number = 12,
            Type = 9, // Gate
            Radius = 25.0,
            AirlineCodes = "AAL, DAL",
            Latitude = -15.8690,
            Longitude = -47.9200
        };

        var spot2 = new ParkingSpot
        {
            Name = "Gate",
            Number = 14,
            Type = 9, // Gate
            Radius = 25.0,
            AirlineCodes = "UAL",
            Latitude = -15.8691,
            Longitude = -47.9201
        };

        var recommendations = ArrivalGateRecommender.RecommendGates(
            new[] { spot2, spot1 }, a320, airlineIcao: "AAL", originLat: -15.8650, originLon: -47.9200);

        Assert.NotEmpty(recommendations);
        Assert.Equal(12, recommendations[0].Spot.Number);
        Assert.True(recommendations[0].AirlineMatch);
    }

    [Fact]
    public void RecommendGates_PrefersCargoStands_ForFreighters()
    {
        var b77f = CreateProfile("B77F", 64.8, IcaoCodeLetter.E, isFreighter: true);

        var passengerGate = new ParkingSpot
        {
            Name = "Gate",
            Number = 1,
            Type = 9, // Gate
            Radius = 35.0,
            HasJetway = true
        };

        var cargoStand = new ParkingSpot
        {
            Name = "Cargo",
            Number = 5,
            Type = 6, // Cargo
            Radius = 35.0
        };

        var recommendations = ArrivalGateRecommender.RecommendGates(
            new[] { passengerGate, cargoStand }, b77f, airlineIcao: null, originLat: null, originLon: null);

        Assert.NotEmpty(recommendations);
        Assert.Equal(5, recommendations[0].Spot.Number);
        Assert.Contains("Cargo Stand", recommendations[0].CategoryDescription);
    }

    [Fact]
    public void RecommendGates_PrefersGaRamp_ForLightAircraft()
    {
        var c172 = CreateProfile("C172", 11.0, IcaoCodeLetter.A, isFreighter: false);

        var commercialGate = new ParkingSpot
        {
            Name = "Gate",
            Number = 1,
            Type = 9, // Gate
            Radius = 20.0,
            HasJetway = true
        };

        var gaRamp = new ParkingSpot
        {
            Name = "Ramp",
            Number = 10,
            Type = 2, // Ramp GA Small
            Radius = 15.0
        };

        var recommendations = ArrivalGateRecommender.RecommendGates(
            new[] { commercialGate, gaRamp }, c172, airlineIcao: null, originLat: null, originLon: null);

        Assert.NotEmpty(recommendations);
        Assert.Equal(10, recommendations[0].Spot.Number);
        Assert.Contains("General Aviation Ramp", recommendations[0].CategoryDescription);
    }

    [Fact]
    public void RecommendGates_PenalizesNonFittingWingspan()
    {
        var a380 = CreateProfile("A388", 79.75, IcaoCodeLetter.F, isFreighter: false);

        var smallGate = new ParkingSpot
        {
            Name = "Gate",
            Number = 1,
            Type = 9,
            MaxWingspanMeters = 36.0 // Cannot fit A380
        };

        var heavyGate = new ParkingSpot
        {
            Name = "Gate",
            Number = 2,
            Type = 9,
            MaxWingspanMeters = 80.0 // Can fit A380
        };

        var recommendations = ArrivalGateRecommender.RecommendGates(
            new[] { smallGate, heavyGate }, a380, airlineIcao: null, originLat: null, originLon: null);

        Assert.NotEmpty(recommendations);
        Assert.Equal(2, recommendations[0].Spot.Number);
        Assert.True(recommendations[0].FitsWingspan);
        Assert.False(recommendations[1].FitsWingspan);
    }

    [Fact]
    public void RecommendGates_ExcludesDeiceAndMilitary()
    {
        var a320 = CreateProfile("A320", 35.8, IcaoCodeLetter.C, isFreighter: false);

        var deiceSpot = new ParkingSpot
        {
            Name = "Deice",
            Number = 1,
            Type = 9,
            IsDeiceArea = true
        };

        var milSpot = new ParkingSpot
        {
            Name = "Mil",
            Number = 2,
            Type = 7 // Military
        };

        var regularGate = new ParkingSpot
        {
            Name = "Gate",
            Number = 10,
            Type = 9,
            Radius = 25.0
        };

        var recommendations = ArrivalGateRecommender.RecommendGates(
            new[] { deiceSpot, milSpot, regularGate }, a320, airlineIcao: null, originLat: null, originLon: null);

        Assert.Single(recommendations);
        Assert.Equal(10, recommendations[0].Spot.Number);
    }

    [Fact]
    public void AutoSelectBestGate_ReturnsTopRankedSpot()
    {
        var a320 = CreateProfile("A320", 35.8, IcaoCodeLetter.C, isFreighter: false);

        var spot1 = new ParkingSpot
        {
            Name = "Gate",
            Number = 10,
            Type = 9,
            Radius = 25.0,
            HasJetway = true,
            AirlineCodes = "GLO"
        };

        var spot2 = new ParkingSpot
        {
            Name = "Gate",
            Number = 20,
            Type = 9,
            Radius = 25.0,
            HasJetway = false
        };

        var best = ArrivalGateRecommender.AutoSelectBestGate(
            new[] { spot2, spot1 }, a320, airlineIcao: "GLO", originLat: null, originLon: null);

        Assert.NotNull(best);
        Assert.Equal(10, best!.Number);
    }

    [Fact]
    public void RecommendGates_FormatsAccessibleLabelCorrectly()
    {
        var a320 = CreateProfile("A320", 35.8, IcaoCodeLetter.C, isFreighter: false);

        var spot = new ParkingSpot
        {
            Name = "Gate",
            Number = 12,
            Type = 9,
            HasJetway = true,
            TerminalName = "Terminal 2",
            Latitude = 0.005,
            Longitude = 0.0
        };

        var recommendations = ArrivalGateRecommender.RecommendGates(
            new[] { spot }, a320, airlineIcao: null, originLat: 0.0, originLon: 0.0);

        Assert.Single(recommendations);
        var rec = recommendations[0];
        Assert.StartsWith("[Recommended]", rec.AccessibleLabel);
        Assert.Contains("Gate 12", rec.AccessibleLabel);
        Assert.Contains("Terminal 2", rec.AccessibleLabel);
        Assert.Contains("Commercial Jetway", rec.AccessibleLabel);
        Assert.Contains("taxi", rec.AccessibleLabel);
    }
}

