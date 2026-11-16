using System.Globalization;
using System.Text;
using System.Text.Json;
using WattWatch.Core.Models;

namespace WattWatch.Core.Storage;

public sealed record HistoryPoint(DateTimeOffset Timestamp, int Percent, ChargingState Charging);

/// <summary>
/// Append-only JSONL history, one file per device. Deliberately tiny: this app's
/// job is to be invisible, so history is a by-product, not a database.
/// </summary>
public sealed class HistoryStore
{
    private readonly string _directory;
    private readonly Dictionary<string, List<HistoryPoint>> _cache = new();
    private readonly object _gate = new();

    public HistoryStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(AppSettings.DataDirectory, "history");
    }

    public void Append(string deviceKey, int percent, ChargingState charging, DateTimeOffset when)
    {
        try
        {
            lock (_gate)
            {
                if (!_cache.TryGetValue(deviceKey, out List<HistoryPoint>? points))
                {
                    points = Load(deviceKey);
                    _cache[deviceKey] = points;
                }

                // One line per meaningful change, plus a heartbeat every 30 minutes.
                HistoryPoint? last = points.Count > 0 ? points[^1] : null;
                if (last != null
                    && last.Percent == percent
                    && last.Charging == charging
                    && when - last.Timestamp < TimeSpan.FromMinutes(30))
                    return;

                points.Add(new HistoryPoint(when, percent, charging));
                if (points.Count > 20000) points.RemoveRange(0, 2000);
                WriteLine(deviceKey, when, percent, charging);
            }
        }
        catch
        {
            // History is best-effort.
        }
    }

    public IReadOnlyList<HistoryPoint> Recent(string deviceKey, TimeSpan window)
    {
        lock (_gate)
        {
            if (!_cache.TryGetValue(deviceKey, out List<HistoryPoint>? points))
            {
                points = Load(deviceKey);
                _cache[deviceKey] = points;
            }

            DateTimeOffset since = DateTimeOffset.Now - window;
            return points.Where(p => p.Timestamp >= since).ToList();
        }
    }

    private void WriteLine(string deviceKey, DateTimeOffset when, int percent, ChargingState charging)
    {
        Directory.CreateDirectory(_directory);
        string path = PathFor(deviceKey);
        var payload = JsonSerializer.Serialize(new HistoryLine(when.ToUnixTimeSeconds(), percent, (int)charging));
        File.AppendAllText(path, payload + Environment.NewLine, Encoding.UTF8);
    }

    private List<HistoryPoint> Load(string deviceKey)
    {
        var result = new List<HistoryPoint>();
        try
        {
            string path = PathFor(deviceKey);
            if (!File.Exists(path)) return result;

            foreach (string line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    HistoryLine? entry = JsonSerializer.Deserialize<HistoryLine>(line);
                    if (entry == null) continue;
                    result.Add(new HistoryPoint(
                        DateTimeOffset.FromUnixTimeSeconds(entry.T),
                        entry.P,
                        (ChargingState)entry.C));
                }
                catch
                {
                    // Skip a corrupt line rather than losing the whole file.
                }
            }
        }
        catch
        {
            // ignore
        }

        return result;
    }

    private string PathFor(string deviceKey)
    {
        string safe = new(deviceKey.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
        return Path.Combine(_directory, safe + ".jsonl");
    }

    private sealed record HistoryLine(long T, int P, int C);
}
