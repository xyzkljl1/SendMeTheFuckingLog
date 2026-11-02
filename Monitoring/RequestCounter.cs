using System.Text.Json;
using SendMeTheFuckingLog.Storage;

namespace SendMeTheFuckingLog.Monitoring;

// One process owns the data directory through SubmissionStore's writer lock.
public sealed class RequestCounter : BackgroundService
{
    private const int Size = 7 * 24 * 60;
    private static readonly int[] Windows = [60, 1440, Size];
    private readonly object gate = new();
    private readonly long[] counts = new long[Size];
    private readonly long[] totals = new long[3];
    private readonly long[] limits;
    private readonly string path;
    private readonly TimeProvider clock;
    private readonly ILogger<RequestCounter> logger;
    private long minute;
    private bool dirty;
    private bool storageHealthy = true;

    public RequestCounter(RequestLimits limits, TimeProvider clock, ILogger<RequestCounter> logger)
        : this(limits, clock, logger, Path.Combine(AppContext.BaseDirectory, "data", ".request-counts.json")) { }

    internal RequestCounter(RequestLimits limits, TimeProvider clock, ILogger<RequestCounter> logger, string path)
    {
        limits.Validate();
        this.limits = [limits.MaxRequestsPerHour, limits.MaxRequestsPerDay, limits.MaxRequestsPerWeek];
        this.clock = clock;
        this.logger = logger;
        this.path = path;
    }

    public void Initialize()
    {
        lock (gate)
        {
            minute = clock.GetUtcNow().ToUnixTimeSeconds() / 60;
            EnsurePlain(path);
            if (File.Exists(path))
            {
                if (new FileInfo(path).Length > 256 * 1024)
                    throw new InvalidDataException("Request counter file is too large.");
                var saved = JsonSerializer.Deserialize<Snapshot>(File.ReadAllBytes(path))
                    ?? throw new InvalidDataException("Request counter file is empty.");
                if (saved.Version != 1 || saved.Minute < 0 || saved.Counts?.Length != Size
                    || saved.Counts.Any(value => value < 0 || value > 1_000_000_000_000))
                    throw new InvalidDataException("Invalid request counter file; refusing to reset it silently.");
                minute = saved.Minute;
                saved.Counts.CopyTo(counts, 0);
                for (var window = 0; window < Windows.Length; window++)
                    for (var age = 0; age < Windows[window]; age++)
                        totals[window] += counts[Index(minute - age)];
                Advance();
            }
            dirty = true;
            Save();
        }
    }

    // No allocations or disk writes on the request path; rejected requests consume no quota.
    public int Admit()
    {
        lock (gate)
        {
            Advance();
            if (!storageHealthy) return StatusCodes.Status503ServiceUnavailable;
            for (var index = 0; index < limits.Length; index++)
                if (totals[index] >= limits[index]) return StatusCodes.Status429TooManyRequests;
            counts[Index(minute)]++;
            for (var index = 0; index < totals.Length; index++) totals[index]++;
            dirty = true;
            return StatusCodes.Status200OK;
        }
    }

    private static int Index(long value) => (int)((value % Size + Size) % Size);

    private void Advance()
    {
        // A backwards clock change must not reset quotas or count the same minute twice.
        var now = clock.GetUtcNow().ToUnixTimeSeconds() / 60;
        if (now <= minute) return;
        if (now - minute >= Size)
        {
            Array.Clear(counts);
            Array.Clear(totals);
            minute = now;
        }
        else
        {
            while (minute < now)
            {
                minute++;
                for (var index = 0; index < Windows.Length; index++)
                    totals[index] -= counts[Index(minute - Windows[index])];
                counts[Index(minute)] = 0;
            }
        }
        dirty = true;
    }

    private static void EnsurePlain(string file)
    {
        for (var current = new FileInfo(file) as FileSystemInfo; current is not null;
             current = current is FileInfo info ? info.Directory : ((DirectoryInfo)current).Parent)
            if ((current.Exists || current.LinkTarget is not null)
                && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Request counter path must not contain symbolic links.");
    }

    private void Save()
    {
        if (!dirty) return;
        EnsurePlain(path);
        var staging = path + ".new";
        EnsurePlain(staging);
        using (var stream = new FileStream(staging, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, new Snapshot(1, minute, counts));
            DiskPersistence.FlushFile(stream);
        }
        File.Move(staging, path, overwrite: true);
        DiskPersistence.FlushDirectory(Path.GetDirectoryName(path)!);
        dirty = false;
        storageHealthy = true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                lock (gate)
                {
                    try { Advance(); Save(); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                        storageHealthy = false;
                        logger.LogError(error, "Cannot persist request counts; requests are temporarily blocked.");
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        lock (gate) { Advance(); Save(); }
    }

    private sealed record Snapshot(int Version, long Minute, long[] Counts);
}
