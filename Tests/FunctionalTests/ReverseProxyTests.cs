namespace FunctionalTests;

public sealed class ReverseProxyTests
{
    private const string Domain = "logs.example.com";
    private const string Proxy = "192.0.2.1";
    private const string Author = "203.0.113.10";

    private static Dictionary<string, string?> Settings() => new()
    {
        ["Tls:Enabled"] = "false", ["Tls:Domain"] = Domain,
        ["ReverseProxy:Enabled"] = "true", ["ReverseProxy:KnownProxies:0"] = Proxy,
        ["AuthorAccess:AllowedClients:0"] = Author, ["AuthorAccess:AllowedClients:1"] = "2001:db8::10"
    };

    private static HttpClient Client(TestApplication app, string visitor = Author)
    {
        app.ClientAddress = IPAddress.Parse(Proxy);
        var client = app.Client();
        client.DefaultRequestHeaders.Host = Domain;
        client.DefaultRequestHeaders.Add("X-Forwarded-For", visitor);
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        return client;
    }

    [Fact]
    public async Task External_https_serves_and_uploads_without_loading_a_local_certificate()
    {
        await using var world = new TestWorld();
        // External TLS must not depend on a previously deployed application's certificate files.
        world.Write("tls/active.json", "not a certificate manifest");
        var app = world.Start(Settings());
        using var client = Client(app);
        using var page = await client.GetAsync("/", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.True(page.Headers.Contains("Strict-Transport-Security"));
        Assert.False(app.Services.GetRequiredService<HttpsTransport>().Ready);
        var log = TestFile.Log();
        using var upload = await Upload.Send(client, [log]);
        await Upload.AssertSaved(upload, world, log);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/css/site.css", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task All_author_handlers_use_the_visitor_and_ignore_visitor_supplied_header_prefixes()
    {
        await using var world = new TestWorld();
        var id = world.SeedSubmission();
        var app = world.Start(Settings());
        using var client = Client(app);
        await app.RefreshAccess();
        client.DefaultRequestHeaders.Add("CF-Connecting-IP", Author);
        foreach (var handler in new[] { "", "View", "Download" })
        {
            var url = $"/files?handler={handler}&submission={id}&name=UE4SS.log";
            client.DefaultRequestHeaders.Remove("X-Forwarded-For");
            client.DefaultRequestHeaders.Add("X-Forwarded-For", Author);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(url, TestContext.Current.CancellationToken)).StatusCode);
            client.DefaultRequestHeaders.Remove("X-Forwarded-For");
            client.DefaultRequestHeaders.Add("X-Forwarded-For", Author + ", 203.0.113.99");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url, TestContext.Current.CancellationToken)).StatusCode);
        }
    }

    [Theory]
    [InlineData("192.0.2.99", Author, "https", 403)]
    [InlineData(null, Author, "https", 403)]
    [InlineData(Proxy, null, "https", 400)]
    [InlineData(Proxy, Author, null, 400)]
    [InlineData(Proxy, "invalid", "https", 400)]
    [InlineData(Proxy, Author, "https, http", 400)]
    [InlineData(Proxy, Author, "ftp", 400)]
    public async Task Unknown_peers_and_incomplete_or_invalid_headers_fail_closed(string? peer, string? visitor, string? scheme, int status)
    {
        await using var world = new TestWorld();
        var app = world.Start(Settings());
        using var client = Client(app);
        app.ClientAddress = peer is null ? null : IPAddress.Parse(peer);
        client.DefaultRequestHeaders.Remove("X-Forwarded-For");
        client.DefaultRequestHeaders.Remove("X-Forwarded-Proto");
        if (visitor is not null) client.DefaultRequestHeaders.Add("X-Forwarded-For", visitor);
        if (scheme is not null) client.DefaultRequestHeaders.Add("X-Forwarded-Proto", scheme);
        foreach (var path in new[] { "/", "/files", "/api/uploads", "/status", "/healthz" })
            Assert.Equal(status, (int)(await client.GetAsync(path, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Theory]
    [InlineData(Proxy, "::ffff:192.0.2.1", "::ffff:203.0.113.10")]
    [InlineData("2001:db8::1", "2001:db8::1", "2001:db8::10")]
    public async Task Explicit_ipv6_and_mapped_ipv4_proxies_preserve_author_access(string known, string peer, string visitor)
    {
        await using var world = new TestWorld();
        var settings = Settings(); settings["ReverseProxy:KnownProxies:0"] = known;
        var app = world.Start(settings);
        using var client = Client(app, visitor);
        app.ClientAddress = IPAddress.Parse(peer);
        await app.RefreshAccess();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/files", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Redirects_and_host_checks_use_the_configured_domain()
    {
        await using var world = new TestWorld();
        var app = world.Start(Settings());
        using var client = Client(app);
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", "attacker.example");
        client.DefaultRequestHeaders.Add("X-Forwarded-Prefix", "/attacker");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/", TestContext.Current.CancellationToken)).StatusCode);
        client.DefaultRequestHeaders.Host = "attacker.example";
        Assert.Equal(421, (int)(await client.GetAsync("/", TestContext.Current.CancellationToken)).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Forwarded-Proto");
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "http");
        using var redirect = await client.GetAsync("/status?lang=en", TestContext.Current.CancellationToken);
        Assert.Equal(308, (int)redirect.StatusCode);
        Assert.Equal("https://logs.example.com/status?lang=en", redirect.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Global_quota_and_status_work_and_forwarded_loopback_does_not_bypass_them()
    {
        await using var world = new TestWorld();
        var settings = Settings();
        settings["GlobalRateLimit:MaxRequestsPerHour"] = "1";
        settings["ReverseProxy:KnownProxies:0"] = "127.0.0.1";
        var app = world.Start(settings);
        using var client = Client(app, "127.0.0.1");
        app.ClientAddress = IPAddress.Loopback;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/healthz", TestContext.Current.CancellationToken)).StatusCode);
        using var status = await client.GetAsync("/sTaTus?lang=en", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.TooManyRequests, status.StatusCode);
        Assert.Contains("Website status", await status.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        client.DefaultRequestHeaders.Host = "localhost";
        client.DefaultRequestHeaders.Remove("X-Forwarded-For");
        client.DefaultRequestHeaders.Remove("X-Forwarded-Proto");
        Assert.Equal("OK", await client.GetStringAsync("/healthz", TestContext.Current.CancellationToken));
        await app.DisposeAsync();
        var restarted = world.Start(settings);
        using var afterRestart = Client(restarted);
        restarted.ClientAddress = IPAddress.Loopback;
        Assert.Equal(HttpStatusCode.TooManyRequests, (await afterRestart.GetAsync("/", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("::ffff:0.0.0.0")]
    [InlineData("192.0.2.0/24")]
    [InlineData("proxy.example")]
    [InlineData("ff02::1")]
    [InlineData("fe80::1%2")]
    public void Enabled_proxy_requires_explicit_individual_peer_addresses(string? peer)
    {
        var settings = new Dictionary<string, string?> { ["ReverseProxy:Enabled"] = "true" };
        if (peer is not null) settings["ReverseProxy:KnownProxies:0"] = peer;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        Assert.Throws<InvalidOperationException>(() => new ReverseProxyHeaders(configuration));
    }
}
