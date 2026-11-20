// MSFSBlindAssist/Navigation/Planning/TaxiRoutePlannerService.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Navigation.Briefing;

namespace MSFSBlindAssist.Navigation.Planning;

/// <summary>
/// Orchestrates departure and arrival taxi route planning, pushback instruction generation,
/// and screen-reader briefing formatting.
/// </summary>
public static class TaxiRoutePlannerService
{
    public const double MaxGateDetectionDistanceMeters = 75.0;

    /// <summary>
    /// Finds the closest parking spot to the given aircraft coordinates.
    /// </summary>
    public static ParkingSpot? FindCurrentParkingSpot(
        double lat,
        double lon,
        IReadOnlyList<ParkingSpot>? spots,
        double maxDistanceMeters = MaxGateDetectionDistanceMeters)
    {
        if (spots == null || spots.Count == 0) return null;

        ParkingSpot? bestSpot = null;
        double bestDist = double.MaxValue;

        foreach (var spot in spots)
        {
            if (spot.IsDeiceArea || ParkingTypes.IsMilitary(spot.Type) || ParkingTypes.IsDock(spot.Type))
                continue;

            double dist = TaxiGraph.FastDistanceMeters(lat, lon, spot.Latitude, spot.Longitude);
            double allowedDist = Math.Max(maxDistanceMeters, spot.Radius + 20.0);

            if (dist <= allowedDist && dist < bestDist)
            {
                bestDist = dist;
                bestSpot = spot;
            }
        }

        return bestSpot;
    }

    /// <summary>
    /// Plans a departure taxi route from current position / gate to the specified runway.
    /// </summary>
    public static PlannedDeparturePlan? PlanDeparture(
        string icao,
        string runwayId,
        double aircraftLat,
        double aircraftLon,
        double magVar,
        TaxiGraph graph,
        IReadOnlyList<ParkingSpot>? spots,
        int destinationNodeId,
        string? holdingPointName = null)
    {
        if (graph == null || destinationNodeId < 0) return null;

        ParkingSpot? spot = FindCurrentParkingSpot(aircraftLat, aircraftLon, spots);

        int startNodeId = -1;
        if (spot != null)
        {
            var node = graph.FindNearestNode(spot.Latitude, spot.Longitude, excludeBridgeOnlyStandStubs: true);
            if (node != null) startNodeId = node.NodeId;
        }

        if (startNodeId < 0)
        {
            var node = graph.FindNearestNode(aircraftLat, aircraftLon, excludeBridgeOnlyStandStubs: true);
            if (node == null) return null;
            startNodeId = node.NodeId;
        }

        var router = new TaxiRouter(graph);
        var route = router.FindShortestPath(startNodeId, destinationNodeId);
        if (route == null) return null;

        var pushback = PushbackGeometryPlanner.CalculatePushback(spot, route, magVar);

        string cleanRwy = runwayId.StartsWith("Runway ", StringComparison.OrdinalIgnoreCase)
            ? runwayId.Substring(7).Trim()
            : runwayId.Trim();

        string posDesc = spot != null
            ? $"{spot.DescribeIdentity()} ({spot.Describe()})"
            : "Current airport position";

        string holdPoint = !string.IsNullOrWhiteSpace(holdingPointName)
            ? holdingPointName
            : $"Runway {cleanRwy} entrance";

        string taxiwayList = route.TaxiwaySequence.Count > 0
            ? string.Join(", ", route.TaxiwaySequence)
            : "(direct onto runway)";

        string distStr = FormatDistance(route.TotalDistanceMeters);

        var lines = new List<string>
        {
            $"Departure taxi plan for Runway {cleanRwy} at {icao}:",
            $"Current position: {posDesc}.",
            pushback.Summary,
            $"Taxi route: {taxiwayList} ({distStr}).",
            $"Holding point: {holdPoint}."
        };

        string summary = string.Join("\r\n", lines);

        return new PlannedDeparturePlan
        {
            AirportIcao = icao,
            RunwayId = cleanRwy,
            Spot = spot,
            Pushback = pushback,
            Route = route,
            HoldingPointName = holdPoint,
            TargetHeadingMag = pushback.PostPushbackHeadingMag,
            OriginLat = aircraftLat,
            OriginLon = aircraftLon,
            IsArmed = true,
            Summary = summary
        };
    }

    /// <summary>
    /// Plans an arrival taxi route from runway exit to the selected parking gate.
    /// </summary>
    public static PlannedArrivalPlan? PlanArrival(
        string icao,
        string runwayId,
        Runway? runway,
        TaxiGraph graph,
        ParkingSpot gate,
        LandingExit? preferredExit,
        IReadOnlyList<LandingExit>? availableExits)
    {
        if (graph == null || gate == null) return null;

        var gateNode = graph.FindNearestNode(gate.Latitude, gate.Longitude);
        if (gateNode == null) return null;

        LandingExit? chosenExit = preferredExit;
        if (chosenExit == null && availableExits != null && availableExits.Count > 0)
        {
            // Pick the first available exit that can reach the gate
            var router = new TaxiRouter(graph);
            foreach (var exit in availableExits)
            {
                int exitStartNode = exit.ApronNodeId > 0 ? exit.ApronNodeId : exit.NodeId;
                var testRoute = router.FindShortestPath(exitStartNode, gateNode.NodeId);
                if (testRoute != null)
                {
                    chosenExit = exit;
                    break;
                }
            }
        }

        TaxiRoute? route = null;
        if (chosenExit != null)
        {
            int startNode = chosenExit.ApronNodeId > 0 ? chosenExit.ApronNodeId : chosenExit.NodeId;
            route = new TaxiRouter(graph).FindShortestPath(startNode, gateNode.NodeId);
        }

        string cleanRwy = runwayId.StartsWith("Runway ", StringComparison.OrdinalIgnoreCase)
            ? runwayId.Substring(7).Trim()
            : runwayId.Trim();

        string exitName = chosenExit != null ? chosenExit.TaxiwayName : "Runway exit";
        string exitDesc = chosenExit != null
            ? $"{exitName} ({FormatDistance(chosenExit.DistanceFromThresholdFeet * 0.3048)} from threshold)"
            : "Nearest comfortable runway exit";

        string taxiwayList = route != null && route.TaxiwaySequence.Count > 0
            ? string.Join(", ", route.TaxiwaySequence)
            : "(exit leads straight to stand)";

        string distStr = route != null ? FormatDistance(route.TotalDistanceMeters) : "distance to be calculated upon vacate";

        var lines = new List<string>
        {
            $"Arrival taxi plan for {gate.DescribeIdentity()} at {icao}:",
            $"Runway: {cleanRwy}.",
            $"Recommended exit: {exitDesc}.",
            $"Taxi route from exit: {taxiwayList} ({distStr}).",
            $"Gate details: {gate.Describe()}."
        };

        string summary = string.Join("\r\n", lines);

        return new PlannedArrivalPlan
        {
            AirportIcao = icao,
            RunwayId = cleanRwy,
            DestinationGate = gate,
            SelectedExit = chosenExit,
            Route = route,
            TotalDistanceMeters = route?.TotalDistanceMeters ?? 0.0,
            IsArmed = true,
            Summary = summary
        };
    }

    private static string FormatDistance(double meters)
    {
        return meters >= 1000.0
            ? $"{(meters / 1000.0).ToString("0.0", CultureInfo.InvariantCulture)} km"
            : $"{Math.Round(meters).ToString("0", CultureInfo.InvariantCulture)} m";
    }
}

