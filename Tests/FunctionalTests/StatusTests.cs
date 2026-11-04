namespace FunctionalTests;

public sealed class StatusTests
{
    [Fact]
    public async Task Authorized_status_shows_folder_counts_without_private_file_names_or_contents_and_paginates()
    {
        await using var world = new TestWorld();
        for (var i = 1; i <= 51; i++) world.SeedSubmission(i, "private contents", "private nickname");
        var app = world.Start();
        using var client = app.Client();
        await app.RefreshAccess();
        var last = "2026-11-03_120000_phantom-blade-zero_0000000000000033";
        world.Write($"data/{last}.uploading/unfinished.log", "partial");
        using var response = await client.GetAsync("/status?lang=en", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("51 folders", html);
        Assert.Contains(last, html);
        Assert.Contains("1 / 2", html);
        foreach (var hidden in new[] { "private contents", "private nickname", "UE4SS.log", "submission.json", ".uploading" })
            Assert.DoesNotContain(hidden, html);
        Assert.Matches(@"</code></td><td>1</td>", html);
        var second = await client.GetStringAsync("/status?lang=en&number=999", TestContext.Current.CancellationToken);
        Assert.Contains("2 / 2", second);
        Assert.Contains("0000000000000001", second);
        Assert.DoesNotContain(last, second);
    }

    [Theory]
    [InlineData("GET", false)]
    [InlineData("HEAD", false)]
    [InlineData("GET", true)]
    [InlineData("HEAD", true)]
    public async Task Status_requires_author_access_even_when_quota_is_exhausted(string method, bool limited)
    {
        await using var world = new TestWorld();
        var id = world.SeedSubmission();
        var app = world.Start(new() { ["GlobalRateLimit:MaxRequestsPerHour"] = limited ? "1" : "100000" });
        using var client = app.Client();
        await app.RefreshAccess();
        using var home = await client.GetAsync("/", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.1");
        client.DefaultRequestHeaders.Add("CF-Connecting-IP", "127.0.0.1");
        foreach (var path in new[] { "/status", "/STATUS/", "/sTaTuS?lang=en&number=2" })
        {
            app.ClientAddress = IPAddress.Parse("192.0.2.99");
            using var denied = await client.SendAsync(new(new HttpMethod(method), path), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.True(denied.Headers.CacheControl!.NoStore);
            Assert.Equal("text/plain", denied.Content.Headers.ContentType!.MediaType);
            var body = await denied.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.DoesNotContain(id, body);
            Assert.DoesNotContain("<html", body);

            app.ClientAddress = IPAddress.Loopback;
            using var allowed = await client.SendAsync(new(new HttpMethod(method), path), TestContext.Current.CancellationToken);
            Assert.Equal(limited ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK, allowed.StatusCode);
            Assert.True(allowed.Headers.CacheControl!.NoStore);
        }
        app.ClientAddress = IPAddress.Parse("192.0.2.99");
        using var unknownHandler = await client.SendAsync(new(new HttpMethod(method), "/status?handler=unknown"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, unknownHandler.StatusCode);
    }

    [Fact]
    public async Task Author_listing_paginates_newest_first()
    {
        await using var world = new TestWorld();
        for (var i = 1; i <= 51; i++) world.SeedSubmission(i);
        var app = world.Start();
        using var client = app.Client();
        await app.RefreshAccess();
        var first = await client.GetStringAsync("/files?lang=en", TestContext.Current.CancellationToken);
        var last = await client.GetStringAsync("/files?lang=en&number=999", TestContext.Current.CancellationToken);
        Assert.Contains("0000000000000033", first);
        Assert.DoesNotContain("0000000000000001", first);
        Assert.Contains("0000000000000001", last);
        Assert.DoesNotContain("0000000000000033", last);
    }

    [Theory]
    [InlineData("missing", "No host monitoring data received")]
    [InlineData("healthy", "Recent check successful")]
    [InlineData("stale", "Stale data; monitor status unknown")]
    [InlineData("failed", "Last check failed")]
    [InlineData("limit", "Shutdown threshold reached")]
    [InlineData("invalid", "Unable to read monitor data")]
    public async Task Monitor_status_distinguishes_health_expiry_limits_and_invalid_data(string state, string expected)
    {
        await using var world = new TestWorld();
        if (state != "missing")
        {
            var sample = new TrafficStatus(1, state != "failed",
                DateTimeOffset.UtcNow.AddMinutes(state == "stale" ? -5 : 0),
                state == "limit" ? 25_000_000_000L : 500_000_000L, 1_000_000_000L,
                25_000_000_000L, 900_000_000_000L, "2026-11-03", "2026-11");
            world.Write("monitor-status/traffic.json", state == "invalid" ? "broken" : JsonSerializer.Serialize(sample));
        }
        var app = world.Start();
        using var client = app.Client();
        await app.RefreshAccess();
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/status?lang=en", TestContext.Current.CancellationToken));
        Assert.Contains(expected, html);
        Assert.Contains("1 hours", html);
        Assert.Contains("24 hours", html);
        Assert.Contains("168 hours", html);
    }
}
