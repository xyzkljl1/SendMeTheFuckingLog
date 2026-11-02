using System.Security.Cryptography;

namespace SendMeTheFuckingLog.Models;

public sealed class HostedImageUrls
{
    private readonly Uri origin;
    private readonly string imageRoot;
    public string Origin => origin.GetLeftPart(UriPartial.Authority);

    public HostedImageUrls(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var value = configuration["ImageHosting:BaseUrl"];
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl) || value.Contains('\\')
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host)
            || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0
            || uri.AbsolutePath != "/")
            throw new InvalidOperationException("ImageHosting:BaseUrl must be an HTTPS origin without credentials, a path, query or fragment.");
        origin = uri;
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
        var url = new Uri(origin, string.Join('/', path.Split('/').Select(Uri.EscapeDataString))).AbsoluteUri;
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
