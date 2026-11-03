namespace FunctionalTests;

public sealed class RateLimitTests
{
    private static Dictionary<string, string?> Limits(int hour, int day, int week) => new()
    {
        ["GlobalRateLimit:MaxRequestsPerHour"] = hour.ToString(),
        ["GlobalRateLimit:MaxRequestsPerDay"] = day.ToString(),
        ["GlobalRateLimit:MaxRequestsPerWeek"] = week.ToString()
    };

    [Fact]
    public async Task Requests_share_hour_day_week_windows_and_restart_keeps_counts()
    {
        await using var world = new TestWorld();
        var saved = world.SeedSubmission();
        var settings = Limits(2, 3, 4);
        var app = world.Start(settings);
        using var client = app.Client();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/css/site.css", TestContext.Current.CancellationToken)).StatusCode);
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/missing", TestContext.Current.CancellationToken)).StatusCode);
        using var api = await client.PostAsync("/api/uploads", new StringContent(""), TestContext.Current.CancellationToken);
        await Upload.AssertError(api, 429, "siteBusy");
        await app.DisposeAsync();
        var restarted = world.Start(settings);
        using var other = restarted.Client();
        Assert.Equal(HttpStatusCode.TooManyRequests, (await other.GetAsync("/", TestContext.Current.CancellationToken)).StatusCode);
        world.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await other.GetAsync("/", TestContext.Current.CancellationToken)).StatusCode);
        world.Clock.Advance(TimeSpan.FromHours(24));
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await other.GetAsync("/", TestContext.Current.CancellationToken)).StatusCode);
        world.Clock.Advance(TimeSpan.FromHours(168));
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/", TestContext.Current.CancellationToken)).StatusCode);
        await restarted.DisposeAsync();
        File.Delete(Path.Combine(world.Data, ".request-counts.json"));
        using var reset = world.Start(settings).Client();
        Assert.Equal(HttpStatusCode.OK, (await reset.GetAsync("/", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reset.GetAsync("/", TestContext.Current.CancellationToken)).StatusCode);
        Assert.True(Directory.Exists(Path.Combine(world.Data, saved)));
    }

    [Fact]
    public async Task Concurrent_requests_do_not_exceed_quota_and_clock_rollback_does_not_reset_it()
    {
        await using var world = new TestWorld();
        var app = world.Start(Limits(37, 100, 200));
        using var client = app.Client();
        var responses = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => client.GetAsync("/missing", TestContext.Current.CancellationToken)));
        Assert.Equal(37, responses.Count(response => response.StatusCode == HttpStatusCode.NotFound));
        Assert.Equal(63, responses.Count(response => response.StatusCode == HttpStatusCode.TooManyRequests));
        foreach (var response in responses) response.Dispose();
        world.Clock.Advance(TimeSpan.FromHours(-1));
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz", TestContext.Current.CancellationToken)).StatusCode);
        using var status = await client.GetAsync("/sTaTus/?lang=en", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.TooManyRequests, status.StatusCode);
        Assert.Contains("Site-wide request limits", await status.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(status.Headers.CacheControl!.NoStore);
        using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/status"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.TooManyRequests, head.StatusCode);
        // TestServer exposes the generated body; real HEAD body suppression is checked with Kestrel in HttpsTests.
    }

    [Fact]
    public async Task Corrupted_counter_is_not_silently_reset()
    {
        await using var world = new TestWorld();
        world.Write("data/.request-counts.json", "corrupt");
        using var counter = new RequestCounter(new RequestLimits(), world.Clock,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RequestCounter>.Instance, new ApplicationPaths(world.Root));
        Assert.Throws<JsonException>(counter.Initialize);
        Assert.Equal("corrupt", File.ReadAllText(Path.Combine(world.Data, ".request-counts.json")));
    }
}
