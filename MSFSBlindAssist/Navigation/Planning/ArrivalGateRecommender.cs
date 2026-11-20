// MSFSBlindAssist/Navigation/Planning/ArrivalGateRecommender.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Navigation.Briefing;

namespace MSFSBlindAssist.Navigation.Planning;

/// <summary>
/// Recommended arrival parking gate/stand enriched with metadata for screen readers.
/// </summary>
public sealed record GateRecommendation
{
    public ParkingSpot Spot { get; init; } = null!;
    public string GateLabel { get; init; } = "";
    public string Terminal { get; init; } = "";
    public string CategoryDescription { get; init; } = "";
    public double EstimatedDistanceMeters { get; init; }
    public double Score { get; init; }
    public bool FitsWingspan { get; init; }
    public bool AirlineMatch { get; init; }
    public string AccessibleLabel { get; init; } = "";
}

/// <summary>
/// Smart arrival gate recommendation and auto-selection based on aircraft profile,
/// airline codes, terminal metadata, and taxi distance.
/// </summary>
public static class ArrivalGateRecommender
{
    /// <summary>
    /// Evaluates and ranks arrival parking spots, returning the most recommended ones.
    /// </summary>
    public static IReadOnlyList<GateRecommendation> RecommendGates(
        IReadOnlyList<ParkingSpot>? spots,
        AircraftProfile aircraft,
        string? airlineIcao,
        double? originLat,
        double? originLon,
        int maxCount = 5)
    {
        if (spots == null || spots.Count == 0)
            return Array.Empty<GateRecommendation>();

        string airline = (airlineIcao ?? "").Trim();
        var recommendations = new List<GateRecommendation>();

        foreach (var spot in spots)
        {
            if (IsExcluded(spot)) continue;

            bool fits = CheckWingspanFit(spot, aircraft);
            bool airlineMatch = !string.IsNullOrEmpty(airline) && MatchesAirline(spot, airline);
            double distMeters = 0.0;
            if (originLat.HasValue && originLon.HasValue)
            {
                distMeters = TaxiGraph.FastDistanceMeters(originLat.Value, originLon.Value, spot.Latitude, spot.Longitude);
            }

            double score = CalculateScore(spot, aircraft, fits, airlineMatch, distMeters);
            string gateLabel = spot.DescribeIdentity();
            string terminal = !string.IsNullOrWhiteSpace(spot.TerminalName) ? spot.TerminalName.Trim() : "";
            string category = GetCategoryDescription(spot);

            string distText = distMeters >= 1000.0
                ? $"{(distMeters / 1000.0).ToString("0.0", CultureInfo.InvariantCulture)} km taxi"
                : $"{Math.Round(distMeters).ToString("0", CultureInfo.InvariantCulture)} m taxi";

            var rec = new GateRecommendation
            {
                Spot = spot,
                GateLabel = gateLabel,
                Terminal = terminal,
                CategoryDescription = category,
                EstimatedDistanceMeters = distMeters,
                Score = score,
                FitsWingspan = fits,
                AirlineMatch = airlineMatch
            };

            // Format accessible label: "[Recommended] Gate B12 (Terminal 2, Commercial Jetway, 950 m taxi)"
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(terminal)) parts.Add(terminal);
            parts.Add(category);
            if (originLat.HasValue) parts.Add(distText);

            string details = parts.Count > 0 ? $" ({string.Join(", ", parts)})" : "";
            rec = rec with { AccessibleLabel = $"[Recommended] {gateLabel}{details}" };

            recommendations.Add(rec);
        }

        return recommendations
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.EstimatedDistanceMeters)
            .Take(maxCount)
            .ToList();
    }

    /// <summary>
    /// Automatically selects the single best parking gate available.
    /// </summary>
    public static ParkingSpot? AutoSelectBestGate(
        IReadOnlyList<ParkingSpot>? spots,
        AircraftProfile aircraft,
        string? airlineIcao,
        double? originLat,
        double? originLon)
    {
        var recommended = RecommendGates(spots, aircraft, airlineIcao, originLat, originLon, maxCount: 1);
        return recommended.FirstOrDefault()?.Spot;
    }

    /// <summary>
    /// Convenience overload to automatically select the single best parking gate available by wingspan in meters.
    /// </summary>
    public static ParkingSpot? AutoSelectBestGate(
        IReadOnlyList<ParkingSpot>? spots,
        double wingspanMeters,
        string? airlineIcao = null,
        double? originLat = null,
        double? originLon = null)
    {
        var letter = wingspanMeters > 0 ? AircraftSizeClass.LetterForWingspan(wingspanMeters) : IcaoCodeLetter.Unknown;
        var profile = new AircraftProfile("", "Aircraft", wingspanMeters > 0 ? wingspanMeters : null, letter, false, 0.0);
        return AutoSelectBestGate(spots, profile, airlineIcao, originLat, originLon);
    }

    private static bool IsExcluded(ParkingSpot spot)
    {
        return spot.IsDeiceArea
            || ParkingTypes.IsMilitary(spot.Type)
            || ParkingTypes.IsDock(spot.Type)
            || ParkingTypes.IsFuel(spot.Type)
            || ParkingTypes.IsVehicle(spot.Type);
    }

    private static bool CheckWingspanFit(ParkingSpot spot, AircraftProfile aircraft)
    {
        if (aircraft.WingspanMetres is not double span) return true;

        if (spot.MaxWingspanMeters is double max && max > 0.0)
            return span <= max;

        if (spot.Radius > 0.0)
            return span <= (spot.Radius * 2.0);

        return true;
    }

    private static bool MatchesAirline(ParkingSpot spot, string airline)
    {
        if (string.IsNullOrWhiteSpace(spot.AirlineCodes)) return false;

        string[] codes = spot.AirlineCodes.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
        return codes.Any(c => string.Equals(c.Trim(), airline, StringComparison.OrdinalIgnoreCase));
    }

    private static double CalculateScore(ParkingSpot spot, AircraftProfile aircraft, bool fits, bool airlineMatch, double distMeters)
    {
        double score = 0.0;

        // Wingspan fit is fundamental
        if (fits) score += 50.0;
        else score -= 500.0;

        // Airline code match is highest priority when available
        if (airlineMatch) score += 150.0;

        // Aircraft operation role matching
        if (aircraft.IsFreighter)
        {
            if (ParkingTypes.IsCargo(spot.Type)) score += 100.0;
            else if (ParkingTypes.IsGate(spot.Type)) score -= 50.0;
        }
        else if (aircraft.CodeLetter is IcaoCodeLetter.A or IcaoCodeLetter.B)
        {
            // GA and light bizjets
            if (ParkingTypes.IsGaRamp(spot.Type)) score += 80.0;
            else if (ParkingTypes.IsGate(spot.Type)) score += 20.0;
        }
        else
        {
            // Commercial passenger airliners
            if (ParkingTypes.IsGate(spot.Type))
            {
                score += 80.0;
                if (spot.HasJetway) score += 20.0;
            }
            else if (ParkingTypes.IsGaRamp(spot.Type))
            {
                score += 20.0;
            }
            else if (ParkingTypes.IsCargo(spot.Type))
            {
                score -= 50.0;
            }
        }

        // Proximity bonus: closer stands to the runway get slight preference (up to 30 points)
        if (distMeters > 0.0)
        {
            score += Math.Max(0.0, 30.0 - (distMeters / 200.0));
        }

        return score;
    }

    private static string GetCategoryDescription(ParkingSpot spot)
    {
        if (ParkingTypes.IsCargo(spot.Type)) return "Cargo Stand";
        if (ParkingTypes.IsGate(spot.Type))
            return spot.HasJetway ? "Commercial Jetway" : "Commercial Gate";
        if (ParkingTypes.IsGaRamp(spot.Type)) return "General Aviation Ramp";
        return "Parking";
    }
}

