// MSFSBlindAssist/Navigation/Planning/PushbackGeometryPlanner.cs
using System;
using System.Globalization;
using System.Linq;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Navigation.Planning;

/// <summary>
/// Direction of pushback maneuver from the stand to the initial taxiway.
/// </summary>
public enum PushbackDirection
{
    /// <summary>No pushback needed; stand allows direct forward taxi-out.</summary>
    NoneDirectTaxiOut,
    /// <summary>Tail pushed left, nose turns right (clockwise heading change).</summary>
    NoseRightTailLeft,
    /// <summary>Tail pushed right, nose turns left (counter-clockwise heading change).</summary>
    NoseLeftTailRight,
    /// <summary>Pushed straight back without turn.</summary>
    Straight
}

/// <summary>
/// Planned pushback instructions and post-pushback alignment.
/// </summary>
public sealed record PushbackPlan
{
    public PushbackDirection Direction { get; init; }
    public string InitialTaxiway { get; init; } = "";
    public double PostPushbackHeadingMag { get; init; }
    public double PostPushbackHeadingTrue { get; init; }
    public double TurnAngleDeg { get; init; }
    public bool NeedsPushback { get; init; }
    public string Summary { get; init; } = "";

    public string DirectionDescription => Direction switch
    {
        PushbackDirection.NoseRightTailLeft => "Nose right, tail left",
        PushbackDirection.NoseLeftTailRight => "Nose left, tail right",
        PushbackDirection.Straight => "Straight back",
        _ => "Direct taxi-out (no pushback required)"
    };
}

/// <summary>
/// Calculates pushback geometry, direction, and post-pushback alignment heading
/// from stand coordinates, stand heading, and departure taxi route.
/// </summary>
public static class PushbackGeometryPlanner
{
    /// <summary>Default angular tolerance in degrees for detecting alignment after pushback.</summary>
    public const double DefaultAlignmentToleranceDeg = 45.0;

    /// <summary>Minimum distance in meters to consider the aircraft has moved away from the stand.</summary>
    public const double DefaultMinMovedDistanceMeters = 15.0;

    /// <summary>
    /// Normalizes an angle in degrees to [0, 360).
    /// </summary>
    public static double Normalize360(double deg) => ((deg % 360.0) + 360.0) % 360.0;

    /// <summary>
    /// Normalizes an angle difference in degrees to [-180, +180).
    /// </summary>
    public static double Normalize180(double deg) => ((deg % 360.0) + 540.0) % 360.0 - 180.0;

    /// <summary>
    /// Calculates the pushback plan for a departure from a given parking spot and route.
    /// </summary>
    /// <param name="spot">The parking spot the aircraft is parked at.</param>
    /// <param name="route">The calculated taxi route from the stand to the departure runway.</param>
    /// <param name="magVar">Magnetic variation at the airport (East positive, West negative).</param>
    /// <returns>A complete <see cref="PushbackPlan"/>.</returns>
    public static PushbackPlan CalculatePushback(ParkingSpot? spot, TaxiRoute? route, double magVar)
    {
        if (route == null || route.Segments.Count == 0)
        {
            return new PushbackPlan
            {
                Direction = PushbackDirection.NoneDirectTaxiOut,
                InitialTaxiway = "",
                PostPushbackHeadingMag = 0,
                PostPushbackHeadingTrue = 0,
                TurnAngleDeg = 0,
                NeedsPushback = false,
                Summary = "No departure taxi route available."
            };
        }

        // Find the initial named taxiway or fallback to the first segment
        string initialTaxiway = route.TaxiwaySequence.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t))
            ?? route.Segments.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.TaxiwayName))?.TaxiwayName
            ?? "(unnamed)";

        // Find the primary forward taxi segment after exiting the stand
        TaxiRouteSegment taxiSegment = route.Segments.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.TaxiwayName))
            ?? route.Segments[0];

        double targetTrueHdg = Normalize360(taxiSegment.BearingDegrees);
        double targetMagHdg = Normalize360(targetTrueHdg - magVar);

        if (spot == null)
        {
            return new PushbackPlan
            {
                Direction = PushbackDirection.NoneDirectTaxiOut,
                InitialTaxiway = initialTaxiway,
                PostPushbackHeadingMag = Math.Round(targetMagHdg),
                PostPushbackHeadingTrue = Math.Round(targetTrueHdg),
                TurnAngleDeg = 0,
                NeedsPushback = false,
                Summary = $"Taxi forward onto {initialTaxiway}. Heading {Math.Round(targetMagHdg):000} degrees magnetic."
            };
        }

        double standTrueHdg = Normalize360(spot.Heading);
        double turnAngle = Normalize180(targetTrueHdg - standTrueHdg);

        // Determine if pushback is required:
        // Stands explicitly flagged or open GA ramps with almost straight exit (< 25 deg turn)
        bool isGaRamp = ParkingTypes.IsGaRamp(spot.Type);
        bool almostStraightExit = Math.Abs(turnAngle) < 25.0;

        if (isGaRamp && almostStraightExit)
        {
            return new PushbackPlan
            {
                Direction = PushbackDirection.NoneDirectTaxiOut,
                InitialTaxiway = initialTaxiway,
                PostPushbackHeadingMag = Math.Round(targetMagHdg),
                PostPushbackHeadingTrue = Math.Round(targetTrueHdg),
                TurnAngleDeg = turnAngle,
                NeedsPushback = false,
                Summary = $"Direct taxi-out: Taxi straight onto taxiway {initialTaxiway}. Heading {Math.Round(targetMagHdg):000} degrees magnetic."
            };
        }

        PushbackDirection direction;
        if (Math.Abs(turnAngle) <= 15.0 || Math.Abs(turnAngle) >= 165.0)
        {
            direction = PushbackDirection.Straight;
        }
        else if (turnAngle > 15.0)
        {
            direction = PushbackDirection.NoseRightTailLeft;
        }
        else
        {
            direction = PushbackDirection.NoseLeftTailRight;
        }

        string dirDesc = direction switch
        {
            PushbackDirection.NoseRightTailLeft => "Nose right, tail left",
            PushbackDirection.NoseLeftTailRight => "Nose left, tail right",
            _ => "Straight back"
        };

        string summary = $"Pushback: {dirDesc}. Post-pushback heading: {Math.Round(targetMagHdg):000} degrees magnetic onto taxiway {initialTaxiway}.";

        return new PushbackPlan
        {
            Direction = direction,
            InitialTaxiway = initialTaxiway,
            PostPushbackHeadingMag = Math.Round(targetMagHdg),
            PostPushbackHeadingTrue = Math.Round(targetTrueHdg),
            TurnAngleDeg = turnAngle,
            NeedsPushback = true,
            Summary = summary
        };
    }

    /// <summary>
    /// Checks whether the aircraft's current heading is within the allowed tolerance
    /// of the target post-pushback magnetic heading.
    /// </summary>
    /// <param name="currentHeadingMag">Aircraft's current magnetic heading.</param>
    /// <param name="targetHeadingMag">Target post-pushback magnetic heading.</param>
    /// <param name="toleranceDeg">Allowed angular deviation in degrees (default 45.0).</param>
    /// <returns>True if aligned within tolerance.</returns>
    public static bool IsAlignedForTaxi(double currentHeadingMag, double targetHeadingMag, double toleranceDeg = DefaultAlignmentToleranceDeg)
    {
        double diff = Math.Abs(Normalize180(currentHeadingMag - targetHeadingMag));
        return diff <= toleranceDeg;
    }

    /// <summary>
    /// Checks whether the aircraft has moved sufficiently far from the parking stand origin.
    /// </summary>
    /// <param name="currentLat">Current latitude.</param>
    /// <param name="currentLon">Current longitude.</param>
    /// <param name="originLat">Stand latitude.</param>
    /// <param name="originLon">Stand longitude.</param>
    /// <param name="minDistanceMeters">Minimum distance threshold in meters.</param>
    /// <returns>True if the aircraft has moved at least <paramref name="minDistanceMeters"/> away.</returns>
    public static bool HasMovedFromPosition(double currentLat, double currentLon, double originLat, double originLon, double minDistanceMeters = DefaultMinMovedDistanceMeters)
    {
        double distanceMeters = TaxiGraph.FastDistanceMeters(currentLat, currentLon, originLat, originLon);
        return distanceMeters >= minDistanceMeters;
    }
}

