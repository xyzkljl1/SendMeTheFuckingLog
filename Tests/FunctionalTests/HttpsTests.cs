using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace FunctionalTests;

public sealed class HttpsTests
{
    private const string Domain = "logs.example.com";

    private static (Dictionary<string, string?> Settings, int Http, int Https) Ports(int limit = 1000)
    {
        using var first = new TcpListener(IPAddress.Loopback, 0);
        using var second = new TcpListener(IPAddress.Loopback, 0);
        first.Start(); second.Start();
        var http = ((IPEndPoint)first.LocalEndpoint).Port;
        var https = ((IPEndPoint)second.LocalEndpoint).Port;
        return (new()
        {
            ["Tls:Enabled"] = "true", ["Tls:Domain"] = Domain,
            ["Tls:HttpPort"] = http.ToString(), ["Tls:HttpsPort"] = https.ToString(),
            ["GlobalRateLimit:MaxRequestsPerHour"] = limit.ToString()
        }, http, https);
    }

    private static X509Certificate2 InstallCertificate(TestWorld world)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=" + Domain, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder(); names.AddDnsName(Domain);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(2));
        var generation = Convert.ToHexStringLower(SHA256.HashData(cert.RawData));
        world.Write($"tls/{generation}/fullchain.pem", cert.ExportCertificatePem());
        world.Write($"tls/{generation}/privkey.pem", rsa.ExportRSAPrivateKeyPem());
        world.Write("tls/active.json", JsonSerializer.Serialize(new { version = 1, generation }));
        return X509CertificateLoader.LoadCertificate(cert.RawData);
    }

    private static HttpClient Client(int port, X509Certificate2? expected = null)
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
        if (expected is not null)
            handler.ServerCertificateCustomValidationCallback = (_, certificate, _, _) => certificate is not null
                && certificate.RawData.AsSpan().SequenceEqual(expected.RawData)
                && certificate.MatchesHostname(Domain, allowCommonName: false)
                && certificate.NotAfter.ToUniversalTime() > DateTime.UtcNow;
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri($"{(expected is null ? "http" : "https")}://127.0.0.1:{port}"),
            Timeout = TimeSpan.FromSeconds(15)
        };
        client.DefaultRequestHeaders.Host = Domain;
        return client;
    }

    [Fact]
    public async Task Bootstrap_serves_acme_but_not_site_and_invalid_challenge_paths_are_rejected()
    {
        await using var world = new TestWorld();
        world.Write("acme/.well-known/acme-challenge/test-token", "challenge-response");
        world.Write("acme/private.txt", "private");
        var ports = Ports();
        var app = world.Start(ports.Settings);
        app.UseKestrel(); app.StartServer();
        using var client = Client(ports.Http);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal("challenge-response", await client.GetStringAsync("/.well-known/acme-challenge/test-token", TestContext.Current.CancellationToken));
        using var head = await client.SendAsync(new(HttpMethod.Head, "/.well-known/acme-challenge/test-token"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Empty(await head.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        foreach (var path in new[] { "/.well-known/acme-challenge/missing", "/.well-known/acme-challenge/../private.txt", "/.well-known/acme-challenge/test%2Ftoken" })
            Assert.NotEqual(HttpStatusCode.OK, (await client.GetAsync(path, TestContext.Current.CancellationToken)).StatusCode);
        using var post = await client.PostAsync("/.well-known/acme-challenge/test-token", new StringContent(""), TestContext.Current.CancellationToken);
        Assert.NotEqual(HttpStatusCode.OK, post.StatusCode);
    }

    [Fact]
    public async Task Real_https_serves_page_redirects_safely_and_reloads_new_certificate_after_restart()
    {
        await using var world = new TestWorld();
        using var cert = InstallCertificate(world);
        var ports = Ports();
        var app = world.Start(ports.Settings);
        app.UseKestrel(); app.StartServer();
        using var https = Client(ports.Https, cert);
        using var page = await https.GetAsync("/", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.True(page.Headers.Contains("Strict-Transport-Security"));
        Assert.Contains("id=\"folder-input\"", await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        using var http = Client(ports.Http);
        http.DefaultRequestHeaders.Host = "attacker.example";
        http.DefaultRequestHeaders.Add("X-Forwarded-Host", "attacker.example");
        http.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        http.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.1");
        using var redirect = await http.GetAsync("/status?lang=en", TestContext.Current.CancellationToken);
        Assert.Equal(308, (int)redirect.StatusCode);
        Assert.Equal("https://logs.example.com/status?lang=en", redirect.Headers.Location!.ToString());
        https.DefaultRequestHeaders.Host = "attacker.example";
        Assert.Equal(421, (int)(await https.GetAsync("/", TestContext.Current.CancellationToken)).StatusCode);
        https.DefaultRequestHeaders.Host = Domain;
        Assert.Equal(HttpStatusCode.NotFound, (await https.GetAsync("/tls/active.json", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await https.GetAsync("/privkey.pem", TestContext.Current.CancellationToken)).StatusCode);
        await app.DisposeAsync();
        using var replacement = InstallCertificate(world);
        Assert.NotEqual(cert.Thumbprint, replacement.Thumbprint);
        var restarted = world.Start(ports.Settings);
        restarted.UseKestrel(); restarted.StartServer();
        using var renewed = Client(ports.Https, replacement);
        Assert.Equal(HttpStatusCode.OK, (await renewed.GetAsync("/", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Https_quota_keeps_status_health_and_acme_accessible_and_persists_on_restart()
    {
        await using var world = new TestWorld();
        using var cert = InstallCertificate(world);
        world.Write("acme/.well-known/acme-challenge/test-token", "challenge");
        var ports = Ports(1);
        var app = world.Start(ports.Settings); app.UseKestrel(); app.StartServer();
        await app.RefreshAccess();
        using var https = Client(ports.Https, cert);
        Assert.Equal(HttpStatusCode.OK, (await https.GetAsync("/", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await https.GetAsync("/", TestContext.Current.CancellationToken)).StatusCode);
        using var status = await https.GetAsync("/sTaTus?lang=en", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.TooManyRequests, status.StatusCode);
        Assert.Contains("Website status", await status.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        using var head = await https.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/status"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.TooManyRequests, head.StatusCode);
        Assert.Empty(await head.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        using var http = Client(ports.Http);
        Assert.Equal("challenge", await http.GetStringAsync("/.well-known/acme-challenge/test-token", TestContext.Current.CancellationToken));
        Assert.Equal("OK", await http.GetStringAsync("/healthz", TestContext.Current.CancellationToken));
        await app.DisposeAsync();
        var restarted = world.Start(ports.Settings); restarted.UseKestrel(); restarted.StartServer();
        using var other = Client(ports.Https, cert);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await other.GetAsync("/", TestContext.Current.CancellationToken)).StatusCode);
    }
}
