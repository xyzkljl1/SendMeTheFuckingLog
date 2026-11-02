using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SendMeTheFuckingLog.Hosting;

public sealed class HttpsTransport : IDisposable
{
    private readonly string challengeRoot = Path.Combine(AppContext.BaseDirectory, "acme", ".well-known", "acme-challenge");
    private readonly X509Certificate2? certificate;
    private readonly X509Certificate2Collection chain = [];
    public bool Enabled { get; }
    public bool Ready => certificate is not null;
    public string Domain { get; } = "";

    public HttpsTransport(WebApplicationBuilder builder)
    {
        Enabled = builder.Configuration.GetValue<bool>("Tls:Enabled");
        if (!Enabled) return; // Local development and pre-publish smoke checks remain HTTP.
        Domain = builder.Configuration["Tls:Domain"] ?? "";
        if (Domain.Length > 253 || !Regex.IsMatch(Domain, @"^(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$"))
            throw new InvalidOperationException("Tls:Domain must be a DNS hostname without a scheme or port.");
        var root = Path.Combine(AppContext.BaseDirectory, "tls");
        var manifestPath = Path.Combine(root, "active.json");
        EnsurePlain(manifestPath);
        if (File.Exists(manifestPath))
        {
            if (new FileInfo(manifestPath).Length > 4096) throw new InvalidDataException("Invalid TLS manifest size.");
            var manifest = JsonSerializer.Deserialize<CertificateManifest>(File.ReadAllText(manifestPath),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (manifest is null || manifest.Version != 1 || manifest.Generation is null
                || !Regex.IsMatch(manifest.Generation, "^[a-f0-9]{64}$"))
                throw new InvalidDataException("Invalid TLS certificate manifest.");
            var fullchain = Path.Combine(root, manifest.Generation, "fullchain.pem");
            var key = Path.Combine(root, manifest.Generation, "privkey.pem");
            EnsurePlain(fullchain);
            EnsurePlain(key);
            certificate = X509Certificate2.CreateFromPemFile(fullchain, key);
            if (!certificate.MatchesHostname(Domain, allowWildcards: false, allowCommonName: false)
                || certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow)
                throw new InvalidDataException("TLS certificate does not match the configured domain or is not yet valid.");
            // Keep an expired certificate loadable so HTTP-01 can repair it after a long shutdown.
            chain.ImportFromPem(File.ReadAllText(fullchain));
        }
        var httpPort = builder.Configuration.GetValue("Tls:HttpPort", 8080);
        var httpsPort = builder.Configuration.GetValue("Tls:HttpsPort", 8443);
        if (httpPort is < 1 or > 65535 || httpsPort is < 1 or > 65535 || httpPort == httpsPort)
            throw new InvalidOperationException("Invalid TLS listening ports.");
        builder.WebHost.ConfigureKestrel(server =>
        {
            server.ListenAnyIP(httpPort);
            if (certificate is not null)
                server.ListenAnyIP(httpsPort, endpoint => endpoint.UseHttps(https =>
                {
                    https.ServerCertificate = certificate;
                    https.ServerCertificateChain = chain;
                }));
        });
    }

    public async Task<bool> HandleChallengeAsync(HttpContext context)
    {
        if (!Enabled || !context.Request.Path.StartsWithSegments("/.well-known/acme-challenge", out var remaining))
            return false;
        var token = remaining.Value?.TrimStart('/') ?? "";
        if (!(HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
            || !Regex.IsMatch(token, "^[A-Za-z0-9_-]{1,128}$"))
            return false;
        var file = Path.Combine(challengeRoot, token);
        try
        {
            EnsurePlain(file);
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, 4096, FileOptions.Asynchronous);
            if (stream.Length > 4096) throw new InvalidDataException("Invalid challenge size.");
            context.Response.Headers.CacheControl = "no-store";
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.ContentLength = stream.Length;
            if (!HttpMethods.IsHead(context.Request.Method))
                await stream.CopyToAsync(context.Response.Body, context.RequestAborted);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            if (context.Response.HasStarted) throw;
            context.Response.Clear();
            return false;
        }
        return true;
    }

    public bool IsChallengePath(PathString path) => Enabled && path.StartsWithSegments("/.well-known/acme-challenge");

    public async Task<bool> HandleTransportAsync(HttpContext context)
    {
        if (!Enabled) return false;
        if (!Ready)
        {
            context.Response.StatusCode = 503;
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsync("HTTPS is being configured. Please try again later.");
            return true;
        }
        if (!context.Request.IsHttps)
        {
            // Never construct redirects from an untrusted Host or forwarded header.
            context.Response.StatusCode = 308;
            context.Response.Headers.Location = "https://" + Domain + context.Request.PathBase.ToUriComponent()
                + context.Request.Path.ToUriComponent() + context.Request.QueryString.ToUriComponent();
            return true;
        }
        if (!string.Equals(context.Request.Host.Host, Domain, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = 421;
            return true;
        }
        context.Response.Headers.StrictTransportSecurity = "max-age=86400";
        return false;
    }

    private static void EnsurePlain(string path)
    {
        for (var current = new FileInfo(path) as FileSystemInfo; current is not null;
             current = current is FileInfo file ? file.Directory : ((DirectoryInfo)current).Parent)
            if (current.LinkTarget is not null || (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0))
                throw new IOException("TLS paths must not contain symbolic links.");
    }

    public void Dispose()
    {
        certificate?.Dispose();
        foreach (var item in chain) item.Dispose();
    }

    private sealed record CertificateManifest(int Version, string Generation);
}
