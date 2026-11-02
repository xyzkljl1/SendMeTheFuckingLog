using System.Text.Json;

namespace SendMeTheFuckingLog.Models;

public sealed class UploadPageOptions
{
    public string AuthorName { get; set; } = "xyzkljl1";
}

public sealed record LocalizedText(string Zh, string En);
public sealed record DiagnosticFile(string Id, LocalizedText Name, string Kind, string[] Paths);
public sealed record SupportedGame(
    string Id, LocalizedText Name, LocalizedText[] Instructions, DiagnosticFile[] Files, string[] ExecutablePaths,
    string? Image = null, bool IsDemo = false);

public sealed class GameCatalog
{
    public IReadOnlyList<SupportedGame> Games { get; }

    public GameCatalog(IWebHostEnvironment environment)
    {
        var directory = Path.Combine(environment.ContentRootPath, "Configuration", "Games");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var games = new List<SupportedGame>();

        foreach (var path in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal))
        {
            var game = JsonSerializer.Deserialize<SupportedGame>(File.ReadAllText(path), options)
                ?? throw new InvalidOperationException($"Invalid game configuration: {Path.GetFileName(path)}");
            if (string.IsNullOrWhiteSpace(game.Id) || game.Id.Length > 80
                || !System.Text.RegularExpressions.Regex.IsMatch(game.Id, "^[a-z0-9]+(?:-[a-z0-9]+)*$")
                || !HasBothLanguages(game.Name)
                || (game.Image is not null && !IsImageLocation(game.Image))
                || game.Instructions is not { Length: > 0 } || game.Instructions.Any(text => !HasBothLanguages(text))
                || game.ExecutablePaths is null || (!game.IsDemo && game.ExecutablePaths.Length == 0)
                || game.ExecutablePaths.Any(value => !IsRelativePath(value) || !value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                || game.Files is null || (!game.IsDemo && game.Files.Length == 0)
                || game.Files.Any(file => string.IsNullOrWhiteSpace(file.Id) || !HasBothLanguages(file.Name)
                    || file.Kind is not ("log" or "config") || file.Paths is not { Length: > 0 }
                    || file.Paths.Any(value => !IsRelativePath(value)))
                || game.Files.Select(file => file.Id).Distinct(StringComparer.Ordinal).Count() != game.Files.Length
                || games.Any(existing => existing.Id == game.Id))
                throw new InvalidOperationException($"Invalid game configuration: {Path.GetFileName(path)}");
            games.Add(game);
        }

        Games = games.AsReadOnly();
    }

    private static bool HasBothLanguages(LocalizedText? text) =>
        text is not null && !string.IsNullOrWhiteSpace(text.Zh) && !string.IsNullOrWhiteSpace(text.En);

    private static bool IsImageLocation(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl) || value.Contains('\\')) return false;
        string path;
        if (value.StartsWith("/images/", StringComparison.Ordinal))
        {
            if (!IsRelativePath(value[1..]) || value.Contains('?') || value.Contains('#') || value.Contains('%')) return false;
            path = value;
        }
        else
        {
            if (!value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host)
                || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0) return false;
            path = uri.AbsolutePath;
        }
        return new[] { ".svg", ".png", ".jpg", ".jpeg", ".webp", ".avif" }
            .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsRelativePath(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !value.Contains('\\') && !value.Contains(':')
        && !value.Split('/').Any(part => part is "" or "." or "..")
        && !value.Any(char.IsControl);
}
