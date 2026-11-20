// tests/MSFSBlindAssist.Tests/TaxiRoutePlannerServiceTests.cs
using System.Collections.Generic;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Navigation.Planning;
using MSFSBlindAssist.Services;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class TaxiRoutePlannerServiceTests
{
    private static TaxiGraph CreateSampleGraph()
    {
        var g = new TaxiGraph();

        // Node 1: Stand position (Gate B12)
        g.Nodes[1] = new TaxiNode
        {
            NodeId = 1,
            Latitude = -15.8690,
            Longitude = -47.9200,
            ComponentId = 0,
            Type = TaxiNodeType.Parking,
            ParkingName = "Gate B12"
        };

        // Node 2: Taxilane entry
        g.Nodes[2] = new TaxiNode
        {
            NodeId = 2,
            Latitude = -15.8690,
            Longitude = -47.9210,
            ComponentId = 0
        };

        // Node 3: Runway holding point / entrance
        g.Nodes[3] = new TaxiNode
        {
            NodeId = 3,
            Latitude = -15.8690,
            Longitude = -47.9250,
            ComponentId = 0
        };

        g.Adjacency[1] = new List<TaxiEdge>
        {
            new() { FromNodeId = 1, ToNodeId = 2, DistanceMeters = 100.0, TaxiwayName = "B" }
        };
        g.Adjacency[2] = new List<TaxiEdge>
        {
            new() { FromNodeId = 2, ToNodeId = 1, DistanceMeters = 100.0, TaxiwayName = "B" },
            new() { FromNodeId = 2, ToNodeId = 3, DistanceMeters = 400.0, TaxiwayName = "B" }
        };
        g.Adjacency[3] = new List<TaxiEdge>
        {
            new() { FromNodeId = 3, ToNodeId = 2, DistanceMeters = 400.0, TaxiwayName = "B" }
        };

        return g;
    }

    [Fact]
    public void FindCurrentParkingSpot_FindsClosestSpotWithinRadius()
    {
        var spot1 = new ParkingSpot
        {
            Name = "Gate",
            Number = 12,
            Latitude = -15.8690,
            Longitude = -47.9200,
            Radius = 25.0
        };

        var spot2 = new ParkingSpot
        {
            Name = "Gate",
            Number = 20,
            Latitude = -15.8800,
            Longitude = -47.9200,
            Radius = 25.0
        };

        // Aircraft coordinates ~10m from spot 1
        var spot = TaxiRoutePlannerService.FindCurrentParkingSpot(
            -15.86908, -47.9200, new[] { spot2, spot1 });

        Assert.NotNull(spot);
        Assert.Equal(12, spot!.Number);
    }

    [Fact]
    public void FindCurrentParkingSpot_ReturnsNull_WhenTooFar()
    {
        var spot = new ParkingSpot
        {
            Name = "Gate",
            Number = 12,
            Latitude = -15.8690,
            Longitude = -47.9200,
            Radius = 20.0
        };

        // Aircraft 2 km away
        var result = TaxiRoutePlannerService.FindCurrentParkingSpot(
            -15.8890, -47.9200, new[] { spot });

        Assert.Null(result);
    }

    [Fact]
    public void PlanDeparture_BuildsFullDeparturePlanAndPushback()
    {
        var graph = CreateSampleGraph();

        var spot = new ParkingSpot
        {
            Name = "Gate",
            Number = 12,
            Latitude = -15.8690,
            Longitude = -47.9200,
            Heading = 90.0,
            Radius = 25.0,
            Type = 9
        };

        var plan = TaxiRoutePlannerService.PlanDeparture(
            icao: "SBBR",
            runwayId: "11R",
            aircraftLat: -15.8690,
            aircraftLon: -47.9200,
            magVar: -20.0,
            graph: graph,
            spots: new[] { spot },
            destinationNodeId: 3,
            holdingPointName: "B1");

        Assert.NotNull(plan);
        Assert.Equal("SBBR", plan!.AirportIcao);
        Assert.Equal("11R", plan.RunwayId);
        Assert.True(plan.IsArmed);
        Assert.NotNull(plan.Pushback);
        Assert.Equal("B1", plan.HoldingPointName);
        Assert.Contains("Departure taxi plan for Runway 11R at SBBR", plan.Summary);
        Assert.Contains("Gate 12", plan.Summary);
        Assert.Contains("Holding point: B1 (Runway 11R entrance)", plan.Summary);
        Assert.Contains("Taxi route: B", plan.Summary);
        Assert.Single(plan.Route.TaxiwaySequence);
        Assert.Equal("B", plan.Route.TaxiwaySequence[0]);
    }

    [Fact]
    public void PlanArrival_BuildsArrivalPlanAndSynchronizesWithPreferredExit()
    {
        var graph = CreateSampleGraph();

        var gate = new ParkingSpot
        {
            Name = "Gate",
            Number = 12,
            Latitude = -15.8690,
            Longitude = -47.9200,
            Type = 9
        };

        var exit = new LandingExit
        {
            TaxiwayName = "B",
            NodeId = 3,
            ApronNodeId = 3,
            DistanceFromThresholdFeet = 2500.0
        };

        var plan = TaxiRoutePlannerService.PlanArrival(
            icao: "SBBR",
            runwayId: "11R",
            runway: null,
            graph: graph,
            gate: gate,
            preferredExit: exit,
            availableExits: new[] { exit });

        Assert.NotNull(plan);
        Assert.Equal("SBBR", plan!.AirportIcao);
        Assert.Equal("11R", plan.RunwayId);
        Assert.Equal(exit, plan.SelectedExit);
        Assert.True(plan.IsArmed);
        Assert.Contains("Arrival taxi plan for Gate 12 at SBBR", plan.Summary);
        Assert.Contains("Recommended exit: B", plan.Summary);
    }

    [Fact]
    public void TaxiPlannedRouteState_ManagesArmedStateCorrectly()
    {
        var state = TaxiPlannedRouteState.Instance;
        state.ClearDeparture();
        state.ClearArrival();

        Assert.False(state.HasArmedDeparture);
        Assert.False(state.HasArmedArrival);

        var depPlan = new PlannedDeparturePlan
        {
            AirportIcao = "SBBR",
            RunwayId = "11R",
            Pushback = new PushbackPlan { Direction = PushbackDirection.Straight },
            Route = new TaxiRoute()
        };

        state.ArmDeparture(depPlan);
        Assert.True(state.HasArmedDeparture);

        state.DisarmDeparture();
        Assert.False(state.HasArmedDeparture);

        var arrPlan = new PlannedArrivalPlan
        {
            AirportIcao = "SBBR",
            RunwayId = "11R",
            DestinationGate = new ParkingSpot { Name = "Gate", Number = 1 }
        };

        state.ArmArrival(arrPlan);
        Assert.True(state.HasArmedArrival);

        state.DisarmArrival();
        Assert.False(state.HasArmedArrival);

        state.ClearArrival();
        Assert.Null(state.ArrivalPlan);
    }

    [Fact]
    public void TaxiPlannedRouteState_UpdateArrivalExit_RecalculatesRoute()
    {
        var graph = CreateSampleGraph();
        var state = TaxiPlannedRouteState.Instance;

        var gate = new ParkingSpot
        {
            Name = "Gate",
            Number = 12,
            Latitude = -15.8690,
            Longitude = -47.9200,
            Type = 9
        };

        var initialExit = new LandingExit
        {
            TaxiwayName = "B",
            NodeId = 2,
            ApronNodeId = 2
        };

        var plan = new PlannedArrivalPlan
        {
            AirportIcao = "SBBR",
            RunwayId = "11R",
            DestinationGate = gate,
            SelectedExit = initialExit
        };

        state.ArmArrival(plan);

        // Pilot changes exit in LandingExitPlanner to Exit Node 3 (e.g. further down the runway)
        var newExit = new LandingExit
        {
            TaxiwayName = "B_Far",
            NodeId = 3,
            ApronNodeId = 3
        };

        state.UpdateArrivalExit(newExit, graph);

        Assert.Equal(newExit, state.ArrivalPlan!.SelectedExit);
        Assert.NotNull(state.ArrivalPlan.Route);
        // From Node 3 to Node 1 distance is 400 + 100 = 500m
        Assert.Equal(500.0, state.ArrivalPlan.TotalDistanceMeters);
    }

    [Fact]
    public void DescribeSpotWithTerminal_SBGL_IdentifiesTerminalsAndOperations()
    {
        var spotA = new ParkingSpot { Name = "A", Number = 14, Type = 9 };
        var descA = TaxiRoutePlannerService.DescribeSpotWithTerminal(spotA, "SBGL");
        Assert.Contains("A 14", descA);
        Assert.Contains("Terminal 1 (Domestic)", descA);

        var spotB = new ParkingSpot { Name = "B", Number = 20, Type = 9 };
        var descB = TaxiRoutePlannerService.DescribeSpotWithTerminal(spotB, "SBGL");
        Assert.Contains("Terminal 1 (Domestic)", descB);

        var spotC = new ParkingSpot { Name = "C", Number = 31, Type = 9 };
        var descC = TaxiRoutePlannerService.DescribeSpotWithTerminal(spotC, "SBGL");
        Assert.Contains("Terminal 2 (Domestic / International)", descC);

        var spotD = new ParkingSpot { Name = "D", Number = 44, Type = 9 };
        var descD = TaxiRoutePlannerService.DescribeSpotWithTerminal(spotD, "SBGL");
        Assert.Contains("Terminal 2 - Pier Sul (International)", descD);

        var spotCargo = new ParkingSpot { Name = "TECA", Number = 1, Type = 6 };
        var descCargo = TaxiRoutePlannerService.DescribeSpotWithTerminal(spotCargo, "SBGL");
        Assert.Contains("Cargo", descCargo);
        Assert.Contains("Terminal de Cargas", descCargo);
    }

    [Fact]
    public void PlanDeparture_ResolvesFallbackHoldingPointFromTaxiway()
    {
        var graph = CreateSampleGraph();

        var spot = new ParkingSpot
        {
            Name = "Gate",
            Number = 12,
            Latitude = -15.8690,
            Longitude = -47.9200,
            Heading = 90.0,
            Radius = 25.0,
            Type = 9
        };

        var plan = TaxiRoutePlannerService.PlanDeparture(
            icao: "SBGL",
            runwayId: "10",
            aircraftLat: -15.8690,
            aircraftLon: -47.9200,
            magVar: -23.0,
            graph: graph,
            spots: new[] { spot },
            destinationNodeId: 3,
            holdingPointName: null);

        Assert.NotNull(plan);
        // Fallback uses the last taxiway segment ("B") to name the entrance
        Assert.Contains("Holding point: B (Runway 10 entrance)", plan!.Summary);
        Assert.Contains("Taxi route: B", plan.Summary);
    }

    [Fact]
    public void ArmDeparturePlan_RetainsGraphAndProvider_AndAutoActivatesOnPushbackTrigger()
    {
        var graph = CreateSampleGraph();
        var provider = new FakeAirportDataProvider();
        var announcer = new ScreenReaderAnnouncer(System.IntPtr.Zero);
        using var manager = new TaxiGuidanceManager(announcer);

        var spot = new ParkingSpot
        {
            Name = "Gate",
            Number = 12,
            Latitude = -15.8690,
            Longitude = -47.9200,
            Heading = 90.0,
            Radius = 25.0,
            Type = 9
        };

        var plan = TaxiRoutePlannerService.PlanDeparture(
            icao: "SBBR",
            runwayId: "11R",
            aircraftLat: -15.8690,
            aircraftLon: -47.9200,
            magVar: -20.0,
            graph: graph,
            spots: new[] { spot },
            destinationNodeId: 3,
            holdingPointName: "B1");

        Assert.NotNull(plan);
        Assert.Equal(3, plan!.DestinationNodeId);
        manager.ArmDeparturePlan(plan, graph, provider);

        Assert.True(TaxiPlannedRouteState.Instance.HasArmedDeparture);

        // Before pushback finishes (still at origin gate, < 15m)
        bool firedTooEarly = manager.CheckDeparturePushbackTrigger(
            lat: -15.8690, lon: -47.9200, headingMag: plan.TargetHeadingMag,
            headingTrue: plan.TargetHeadingMag - 20.0, groundSpeedKts: 0.0);
        Assert.False(firedTooEarly);
        Assert.True(TaxiPlannedRouteState.Instance.HasArmedDeparture);

        // Pushback finishes (aircraft moved ~100m onto taxiway, stopped, aligned with target heading +-45)
        bool fired = manager.CheckDeparturePushbackTrigger(
            lat: -15.8690, lon: -47.9210, headingMag: plan.TargetHeadingMag,
            headingTrue: plan.TargetHeadingMag - 20.0, groundSpeedKts: 0.0);

        Assert.True(fired);
        Assert.False(TaxiPlannedRouteState.Instance.HasArmedDeparture);
        Assert.Equal(TaxiGuidanceState.Taxiing, manager.State);
    }

    [Fact]
    public void ArmArrivalPlan_RetainsGraphAndProvider_AndArmsArrivalState()
    {
        var graph = CreateSampleGraph();
        var provider = new FakeAirportDataProvider();
        var announcer = new ScreenReaderAnnouncer(System.IntPtr.Zero);
        using var manager = new TaxiGuidanceManager(announcer);

        var gate = new ParkingSpot
        {
            Name = "Gate",
            Number = 14,
            Latitude = -15.8690,
            Longitude = -47.9200,
            Heading = 90.0,
            Radius = 25.0,
            Type = 9
        };

        var plan = new PlannedArrivalPlan
        {
            AirportIcao = "SBBR",
            RunwayId = "11R",
            DestinationGate = gate,
            SelectedExit = null,
            Route = new TaxiRoute { DestinationName = "Gate 14" },
            Summary = "Arrival taxi plan for Gate 14"
        };

        manager.ArmArrivalPlan(plan, graph, provider);

        Assert.True(TaxiPlannedRouteState.Instance.HasArmedArrival);
        Assert.Equal("SBBR", TaxiPlannedRouteState.Instance.ArrivalPlan?.AirportIcao);
        Assert.Equal(gate, TaxiPlannedRouteState.Instance.ArrivalPlan?.DestinationGate);

        TaxiPlannedRouteState.Instance.DisarmArrival();
        Assert.False(TaxiPlannedRouteState.Instance.HasArmedArrival);
    }

    [Fact]
    public void PlannedDeparturePlan_StoresExtendedRoutingParameters()
    {
        var plan = new PlannedDeparturePlan
        {
            AirportIcao = "SBGL",
            RunwayId = "10",
            Pushback = new PushbackPlan(),
            Route = new TaxiRoute(),
            DestinationNodeId = 100,
            DestinationName = "Runway 10",
            HoldingPointName = "R",
            HoldingPointHoldNodeId = 55,
            TaxiwaySequence = new List<string> { "L1", "R" },
            DestinationHeading = 98.0,
            DestinationHeadingTrue = 120.0,
            ThresholdLat = -22.8100,
            ThresholdLon = -43.2500,
            PreferIlsHold = true,
            FullLengthBacktrack = false,
            IntersectionDeparture = false,
            TargetHeadingMag = 335.0,
            OriginLat = -22.8120,
            OriginLon = -43.2450,
            Summary = "Departure taxi plan"
        };

        Assert.Equal(55, plan.HoldingPointHoldNodeId);
        Assert.Equal("R", plan.HoldingPointName);
        Assert.Equal(98.0, plan.DestinationHeading);
        Assert.Equal(-22.8100, plan.ThresholdLat);
        Assert.Equal(-43.2500, plan.ThresholdLon);
        Assert.True(plan.PreferIlsHold);
        Assert.NotNull(plan.TaxiwaySequence);
        Assert.Equal(2, plan.TaxiwaySequence!.Count);
    }

    [Fact]
    public void LandingExit_RunwayAxisProjection_DeterminesRecommendedSide()
    {
        // Runway pointing North (Start: 0, 0, End: 0.02, 0)
        var runway = new Runway
        {
            RunwayID = "36",
            StartLat = 0.0,
            StartLon = 0.0,
            EndLat = 0.02,
            EndLon = 0.0,
            Heading = 0.0
        };

        var axis = RunwayAxis.For(runway);

        // Gate placed to the East (Right of runway centerline)
        var rightGate = new ParkingSpot
        {
            Name = "Gate",
            Number = 1,
            Latitude = 0.01,
            Longitude = 0.005
        };

        var proj = axis.Project(rightGate.Latitude, rightGate.Longitude);
        Assert.True(proj.LateralMetres > 5.0, "Gate should project onto the Right (positive lateral metres)");

        string preferredSide = proj.LateralMetres > 5.0 ? "Right" : (proj.LateralMetres < -5.0 ? "Left" : "Any");
        Assert.Equal("Right", preferredSide);
    }

    private sealed class FakeAirportDataProvider : IAirportDataProvider
    {
        public bool DatabaseExists => true;
        public string DatabaseType => "Fake";
        public string DatabasePath => string.Empty;
        public Airport? GetAirport(string icao) => new Airport { ICAO = icao, MagVar = -20.0 };
        public List<Runway> GetRunways(string icao) => new() { new Runway { RunwayID = "11R", Heading = 110.0 } };
        public ILSData? GetILSForRunway(string icao, string runwayName) => null;
        public List<ParkingSpot> GetParkingSpots(string icao) => new();
        public bool AirportExists(string icao) => true;
        public int GetAirportCount() => 1;
        public int GetRunwayCount() => 1;
        public int GetParkingSpotCount() => 0;
        public HashSet<string> GetAllAirportICAOs() => new() { "SBBR", "SBGL" };
        public List<string> GetNearbyAirportICAOs(double latitude, double longitude, double radiusNm) => new();
        public List<TaxiPath> GetTaxiPaths(string icao) => new();
        public List<StartPosition> GetRunwayStarts(string icao) => new();
    }
}

