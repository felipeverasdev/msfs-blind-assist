// MSFSBlindAssist/Navigation/Planning/TaxiPlannedRouteState.cs
using System;
using System.Globalization;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Navigation.Planning;

/// <summary>
/// Data for a planned departure taxi route awaiting pushback completion.
/// </summary>
public sealed record PlannedDeparturePlan
{
    public required string AirportIcao { get; init; }
    public required string RunwayId { get; init; }
    public ParkingSpot? Spot { get; init; }
    public required PushbackPlan Pushback { get; init; }
    public required TaxiRoute Route { get; init; }
    public int DestinationNodeId { get; init; }
    public string HoldingPointName { get; init; } = "";
    public double TargetHeadingMag { get; init; }
    public double OriginLat { get; init; }
    public double OriginLon { get; init; }
    public bool IsArmed { get; set; } = true;
    public string Summary { get; init; } = "";
}

/// <summary>
/// Data for a planned arrival taxi route awaiting runway vacation.
/// </summary>
public sealed record PlannedArrivalPlan
{
    public required string AirportIcao { get; init; }
    public required string RunwayId { get; init; }
    public required ParkingSpot DestinationGate { get; init; }
    public LandingExit? SelectedExit { get; set; }
    public TaxiRoute? Route { get; set; }
    public double TotalDistanceMeters { get; set; }
    public bool IsArmed { get; set; } = true;
    public string Summary { get; set; } = "";
}

/// <summary>
/// Holds state for planned departure and arrival taxi routes, managing armed triggers.
/// </summary>
public class TaxiPlannedRouteState
{
    private static readonly Lazy<TaxiPlannedRouteState> _instance = new(() => new TaxiPlannedRouteState());
    public static TaxiPlannedRouteState Instance => _instance.Value;

    public PlannedDeparturePlan? DeparturePlan { get; private set; }
    public PlannedArrivalPlan? ArrivalPlan { get; private set; }

    public bool HasArmedDeparture => DeparturePlan is { IsArmed: true };
    public bool HasArmedArrival => ArrivalPlan is { IsArmed: true };

    public void ArmDeparture(PlannedDeparturePlan plan)
    {
        DeparturePlan = plan;
        plan.IsArmed = true;
    }

    public void DisarmDeparture()
    {
        if (DeparturePlan != null)
        {
            DeparturePlan.IsArmed = false;
        }
    }

    public void ClearDeparture()
    {
        DeparturePlan = null;
    }

    public void ArmArrival(PlannedArrivalPlan plan)
    {
        ArrivalPlan = plan;
        plan.IsArmed = true;
    }

    public void DisarmArrival()
    {
        if (ArrivalPlan != null)
        {
            ArrivalPlan.IsArmed = false;
        }
    }

    public void ClearArrival()
    {
        ArrivalPlan = null;
    }

    /// <summary>
    /// Synchronizes arrival plan with a new or updated exit chosen by the pilot in LandingExitPlanner (Shift+X).
    /// Recalculates route from new exit to the destination gate if graph is provided.
    /// </summary>
    public void UpdateArrivalExit(LandingExit newExit, TaxiGraph? graph)
    {
        if (ArrivalPlan == null) return;

        ArrivalPlan.SelectedExit = newExit;

        if (graph != null)
        {
            int startNodeId = newExit.ApronNodeId > 0 ? newExit.ApronNodeId : newExit.NodeId;
            var destNode = graph.FindNearestNode(ArrivalPlan.DestinationGate.Latitude, ArrivalPlan.DestinationGate.Longitude);
            if (destNode != null)
            {
                var newRoute = new TaxiRouter(graph).FindShortestPath(startNodeId, destNode.NodeId);
                if (newRoute != null)
                {
                    ArrivalPlan.Route = newRoute;
                    ArrivalPlan.TotalDistanceMeters = newRoute.TotalDistanceMeters;
                }
            }
        }
    }
}

