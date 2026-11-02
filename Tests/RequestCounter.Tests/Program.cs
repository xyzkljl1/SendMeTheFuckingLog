using Microsoft.Extensions.Logging.Abstractions;
using SendMeTheFuckingLog.Monitoring;

try
{
    if (args.Length != 1) throw new ArgumentException("Pass an isolated directory under the project's obj directory.");
    var project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    var root = Path.GetFullPath(args[0]);
    var allowed = Path.Combine(project, "obj") + Path.DirectorySeparatorChar;
    if (!root.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || Directory.Exists(root))
        throw new ArgumentException("The test directory must be new and inside project obj.");
    Directory.CreateDirectory(root);
    try
    {
        var clock = new TestClock();
        var logger = NullLogger<RequestCounter>.Instance;
        var path = Path.Combine(root, "counts.json");
        RequestCounter New(RequestLimits limits) { var result = new RequestCounter(limits, clock, logger, path); result.Initialize(); return result; }
        void Equal(int actual, int expected) { if (actual != expected) throw new Exception($"Expected {expected}; received {actual}."); }

        var limits = new RequestLimits { MaxRequestsPerHour = 2, MaxRequestsPerDay = 3, MaxRequestsPerWeek = 4 };
        using (var counter = New(limits))
        {
            Equal(counter.Admit(), 200); Equal(counter.Admit(), 200);
            for (var index = 0; index < 100; index++) Equal(counter.Admit(), 429);
            await counter.StopAsync(CancellationToken.None);
        }
        using (var counter = New(limits))
        {
            Equal(counter.Admit(), 429); // restart keeps hourly usage
            clock.Advance(TimeSpan.FromHours(1));
            Equal(counter.Admit(), 200); Equal(counter.Admit(), 429); // daily cap
            clock.Advance(TimeSpan.FromHours(24));
            Equal(counter.Admit(), 200); Equal(counter.Admit(), 429); // weekly cap
            clock.Advance(TimeSpan.FromHours(168));
            Equal(counter.Admit(), 200);
            await counter.StopAsync(CancellationToken.None);
        }
        File.Delete(path); // deployment removes only request statistics after stopping the app
        clock = new TestClock();
        using (var counter = New(new RequestLimits { MaxRequestsPerHour = 37 }))
        {
            var admitted = 0;
            Parallel.For(0, 2000, _ => { if (counter.Admit() == 200) Interlocked.Increment(ref admitted); });
            Equal(admitted, 37);
            clock.Advance(TimeSpan.FromHours(-1));
            Equal(counter.Admit(), 429); // clock rollback must not reset quotas
            await counter.StopAsync(CancellationToken.None);
        }
        File.WriteAllText(path, "corrupt");
        var rejectedCorruption = false;
        try { using var unused = New(limits); }
        catch (System.Text.Json.JsonException) { rejectedCorruption = true; }
        if (!rejectedCorruption) throw new Exception("Corrupt state was silently reset.");
        Console.WriteLine("PASS: hour/day/week limits, rejected requests, expiry, restart, deployment reset, concurrency, clock rollback, corrupt state.");
    }
    finally { Directory.Delete(root, recursive: true); }
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    Environment.ExitCode = 1;
}

sealed class TestClock : TimeProvider
{
    private DateTimeOffset now = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan duration) => now += duration;
}
