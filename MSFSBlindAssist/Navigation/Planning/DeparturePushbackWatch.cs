// MSFSBlindAssist/Navigation/Planning/DeparturePushbackWatch.cs
using System;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Navigation.Planning;

/// <summary>
/// Evaluates whether a pushback maneuver has concluded and the aircraft is aligned
/// and stopped, ready for departure taxi guidance auto-activation.
/// </summary>
public static class DeparturePushbackWatch
{
    /// <summary>Maximum ground speed in knots to consider aircraft stopped after pushback if parking brake is not set.</summary>
    public const double MaxStoppedGroundSpeedKnots = 1.5;

    /// <summary>
    /// Checks whether the aircraft has completed pushback and is ready for taxi guidance activation.
    /// </summary>
    public static bool CheckPushbackComplete(
        PlannedDeparturePlan? plan,
        double currentLat,
        double currentLon,
        double currentHeadingMag,
        double groundSpeedKnots,
        bool parkingBrakeSet)
    {
        if (plan == null || !plan.IsArmed) return false;

        // Condition 1: Aircraft must have moved away from the stand origin (min 15m)
        bool hasMoved = PushbackGeometryPlanner.HasMovedFromPosition(
            currentLat, currentLon, plan.OriginLat, plan.OriginLon, minDistanceMeters: 15.0);
        if (!hasMoved) return false;

        // Condition 2: Pushback tractor has stopped moving (parking brake set OR ground speed <= 1.5 kt)
        bool isStopped = parkingBrakeSet || groundSpeedKnots <= MaxStoppedGroundSpeedKnots;
        if (!isStopped) return false;

        // Condition 3: Aircraft heading is aligned within flexible tolerance (+-45 deg) with target taxiway heading
        bool isAligned = PushbackGeometryPlanner.IsAlignedForTaxi(
            currentHeadingMag, plan.TargetHeadingMag, toleranceDeg: PushbackGeometryPlanner.DefaultAlignmentToleranceDeg);

        return isAligned;
    }
}

