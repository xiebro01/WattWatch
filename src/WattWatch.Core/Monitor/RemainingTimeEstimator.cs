using WattWatch.Core.Models;
using WattWatch.Core.Storage;

namespace WattWatch.Core.Monitor;

/// <summary>
/// Very small linear estimate of remaining runtime from history. Only used as a
/// hint in the UI, so it stays deliberately conservative: it needs at least three
/// discharged readings over 45 minutes before it says anything.
/// </summary>
public static class RemainingTimeEstimator
{
    public static TimeSpan? Estimate(IReadOnlyList<HistoryPoint> points, int? current, ChargingState charging)
    {
        if (current is not { } now) return null;
        if (charging is ChargingState.Charging or ChargingState.Full) return null;
        if (points.Count < 3) return null;

        DateTimeOffset end = points[^1].Timestamp;
        DateTimeOffset start = points[0].Timestamp;
        double hours = (end - start).TotalHours;
        if (hours < 0.75) return null;

        // Least squares slope in percent per hour.
        double meanX = points.Average(p => (p.Timestamp - start).TotalHours);
        double meanY = points.Average(p => p.Percent);
        double numerator = points.Sum(p => ((p.Timestamp - start).TotalHours - meanX) * (p.Percent - meanY));
        double denominator = points.Sum(p => Math.Pow((p.Timestamp - start).TotalHours - meanX, 2));
        if (denominator <= 0) return null;

        double slope = numerator / denominator;
        if (slope > -0.2) return null;

        double hoursLeft = now / -slope;
        if (hoursLeft <= 0 || hoursLeft > 72) return null;
        return TimeSpan.FromHours(hoursLeft);
    }
}
