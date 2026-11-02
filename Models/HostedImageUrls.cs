namespace SendMeTheFuckingLog.Models;

public sealed class HostedImageUrls
{
    private readonly Uri origin;

    public HostedImageUrls(IConfiguration configuration)
    {
        var value = configuration["ImageHosting:BaseUrl"];
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl) || value.Contains('\\')
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host)
            || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0
            || uri.AbsolutePath != "/")
            throw new InvalidOperationException("ImageHosting:BaseUrl must be an HTTPS origin without credentials, a path, query or fragment.");
        origin = uri;
    }

    public string Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Any(char.IsControl)
            || path.IndexOfAny(['\\', ':', '?', '#', '%']) >= 0
            || path.Split('/').Any(part => part is "" or "." or "..")
            || !new[] { ".svg", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".avif" }
                .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Image paths must be relative to ImageHosting:BaseUrl, for example games/cover.gif.");
        return new Uri(origin, string.Join('/', path.Split('/').Select(Uri.EscapeDataString))).AbsoluteUri;
    }
}
