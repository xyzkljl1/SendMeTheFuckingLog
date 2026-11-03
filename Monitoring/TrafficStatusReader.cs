using System.Text.Json;

namespace SendMeTheFuckingLog.Monitoring;

public sealed class TrafficStatusReader(SendMeTheFuckingLog.Hosting.ApplicationPaths paths)
{
    public TrafficStatus? Read()
    {
        var root = Path.Combine(paths.Root, "monitor-status");
        var file = Path.Combine(root, "traffic.json");
        if (!File.Exists(file)) return null;
        foreach (var path in new[] { root, file })
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Invalid monitor status path.");
        if (new FileInfo(file).Length > 8192) throw new InvalidDataException("Invalid monitor status size.");
        // FileShare.Delete permits the monitor's atomic replacement while this request reads.
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        var value = JsonSerializer.Deserialize<TrafficStatus>(stream)
            ?? throw new InvalidDataException("Empty monitor status.");
        if (value.Version != 1 || value.DailyBytes < 0 || value.MonthlyBytes < 0
            || value.DailyLimitBytes <= 0 || value.MonthlyLimitBytes <= 0
            || value.ObservedAt > DateTimeOffset.UtcNow.AddMinutes(2))
            throw new InvalidDataException("Invalid monitor status.");
        return value;
    }
}

public sealed record TrafficStatus(int Version, bool Healthy, DateTimeOffset ObservedAt,
    long DailyBytes, long MonthlyBytes, long DailyLimitBytes, long MonthlyLimitBytes, string Day, string Month)
{
    public bool Stale => DateTimeOffset.UtcNow - ObservedAt > TimeSpan.FromMinutes(3);
    public bool LimitReached => DailyBytes >= DailyLimitBytes || MonthlyBytes >= MonthlyLimitBytes;
}
