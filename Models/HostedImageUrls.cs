using System.Security.Cryptography;

namespace SendMeTheFuckingLog.Models;

public sealed class HostedImageUrls
{
    private readonly string baseUrl;
    private readonly string imageRoot;
    public string? Origin { get; }
    public string? LocalPathPrefix { get; }

    public HostedImageUrls(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var value = configuration["ImageHosting:BaseUrl"];
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl) || value.Contains('\\')
            || value != value.Trim())
            throw new InvalidOperationException("ImageHosting:BaseUrl must be an HTTPS origin, or a site-relative path in Development.");
        if (value.StartsWith('/'))
        {
            if (!environment.IsDevelopment() || value.StartsWith("//", StringComparison.Ordinal)
                || value.IndexOfAny([':', '?', '#', '%', '{', '}']) >= 0)
                throw new InvalidOperationException("A site-relative ImageHosting:BaseUrl is only allowed in Development, for example /images.");
            var path = value[1..].TrimEnd('/');
            var parts = path.Length == 0 ? [] : path.Split('/');
            if (parts.Any(part => part is "" or "." or ".."))
                throw new InvalidOperationException("ImageHosting:BaseUrl must not contain empty, current or parent directory segments.");
            LocalPathPrefix = path.Length == 0 ? "" : "/" + path;
            baseUrl = (parts.Length == 0 ? "" : "/" + string.Join('/', parts.Select(Uri.EscapeDataString))) + "/";
            imageRoot = Path.Combine(environment.WebRootPath, Path.Combine(parts));
            return;
        }
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host)
            || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0
            || uri.AbsolutePath != "/")
            throw new InvalidOperationException("ImageHosting:BaseUrl must be an HTTPS origin without credentials, a path, query or fragment.");
        Origin = uri.GetLeftPart(UriPartial.Authority);
        baseUrl = uri.AbsoluteUri;
        imageRoot = Path.Combine(environment.WebRootPath, "images");
    }

    public LocalizedText ResolveLocalized(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Guide images need a relative image path, optionally containing {lang}.");
        // A single template selects each language before content hashing.
        var zh = Resolve(path.Replace("{lang}", "zh", StringComparison.Ordinal));
        var en = path.Contains("{lang}", StringComparison.Ordinal)
            ? Resolve(path.Replace("{lang}", "en", StringComparison.Ordinal)) : zh;
        return new(zh, en);
    }

    public string Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Any(char.IsControl)
            || path.IndexOfAny(['\\', ':', '?', '#', '%', '{', '}']) >= 0
            || path.Split('/').Any(part => part is "" or "." or "..")
            || !new[] { ".svg", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".avif" }
                .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Image paths must be relative to ImageHosting:BaseUrl, for example games/cover.gif.");
        var url = baseUrl + string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
        var file = Path.Combine(imageRoot, Path.Combine(path.Split('/')));
        if (File.Exists(file))
        {
            // GameCatalog resolves once at startup. Stream the hash without retaining image bytes.
            using var stream = File.OpenRead(file);
            url += "?v=" + Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        return url;
    }
}
