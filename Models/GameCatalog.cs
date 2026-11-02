using System.Text.Json;
using Microsoft.Extensions.Options;

namespace SendMeTheFuckingLog.Models;

public sealed class UploadPageOptions
{
    public string AuthorName { get; set; } = "xyzkljl1";
    public string[] AutomaticGuideImages { get; set; } = [];
}

public sealed record LocalizedText(string Zh, string En);
public sealed record GuideInstruction(string Zh, string En, string[]? Images = null)
{
    public LocalizedText[] ImageUrls { get; init; } = [];
}
public sealed record DiagnosticFile(string Id, LocalizedText Name, string Kind, string[] Paths, bool IsPrimary = false);
public sealed record SupportedGame(
    string Id, LocalizedText Name, GuideInstruction[] Instructions, DiagnosticFile[] Files, string[] ExecutablePaths,
    string? Image = null, bool IsDemo = false);
public sealed record GameRecognition(Dictionary<string, string> Ue, Dictionary<string, string> Re);

public sealed class GameCatalog
{
    public IReadOnlyList<SupportedGame> Games { get; }
    public GameRecognition Recognition { get; }
    public LocalizedText[] AutomaticGuideImages { get; }

    public GameCatalog(IWebHostEnvironment environment, HostedImageUrls images, IOptions<UploadPageOptions> uploadPage)
    {
        var directory = Path.Combine(environment.ContentRootPath, "Configuration", "Games");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var games = new List<SupportedGame>();
        AutomaticGuideImages = (uploadPage.Value.AutomaticGuideImages ?? []).Select(images.ResolveLocalized).ToArray();

        foreach (var path in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal))
        {
            var game = JsonSerializer.Deserialize<SupportedGame>(File.ReadAllText(path), options)
                ?? throw new InvalidOperationException($"Invalid game configuration: {Path.GetFileName(path)}");
            if (string.IsNullOrWhiteSpace(game.Id) || game.Id.Length > 80
                || !System.Text.RegularExpressions.Regex.IsMatch(game.Id, "^[a-z0-9]+(?:-[a-z0-9]+)*$")
                || !HasBothLanguages(game.Name)
                || game.Instructions is not { Length: > 0 } || game.Instructions.Any(text =>
                    text is null || string.IsNullOrWhiteSpace(text.Zh) || string.IsNullOrWhiteSpace(text.En))
                || game.ExecutablePaths is null
                || game.ExecutablePaths.Any(value => !IsRelativePath(value) || !value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                || game.Files is null || (!game.IsDemo && game.Files.Length == 0)
                || game.Files.Any(file => string.IsNullOrWhiteSpace(file.Id) || !HasBothLanguages(file.Name)
                    || file.Kind is not ("log" or "config") || file.Paths is not { Length: > 0 }
                    || file.Paths.Any(value => !IsRelativePath(value)))
                || game.Files.Select(file => file.Id).Distinct(StringComparer.Ordinal).Count() != game.Files.Length
                || games.Any(existing => existing.Id == game.Id))
                throw new InvalidOperationException($"Invalid game configuration: {Path.GetFileName(path)}");
            games.Add(game with
            {
                Image = game.Image is null ? null : images.Resolve(game.Image),
                Instructions = game.Instructions.Select(instruction => instruction with
                {
                    ImageUrls = (instruction.Images ?? []).Select(images.ResolveLocalized).ToArray()
                }).ToArray()
            });
        }

        Recognition = JsonSerializer.Deserialize<GameRecognition>(File.ReadAllText(
            Path.Combine(environment.ContentRootPath, "Configuration", "GameRecognition.json")), options)
            ?? throw new InvalidOperationException("Invalid game recognition configuration.");
        var genericGames = new HashSet<string>(StringComparer.Ordinal);
        var excludedFolders = new HashSet<string>(["engine", "mods", "platform", "steam"], StringComparer.OrdinalIgnoreCase);
        ValidateMappings(Recognition.Ue, false);
        ValidateMappings(Recognition.Re, true);
        if (games.Any(game => !game.IsDemo && !genericGames.Contains(game.Id) && game.ExecutablePaths.Length == 0))
            throw new InvalidOperationException("Games without generic recognition need executablePaths.");
        Games = games.AsReadOnly();

        void ValidateMappings(Dictionary<string, string>? mappings, bool executable)
        {
            if (mappings is null) throw new InvalidOperationException("Game recognition requires ue and re tables.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, gameId) in mappings)
            {
                if (!IsRelativePath(name) || name.Contains('/') || !names.Add(name)
                    || (executable ? !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) : excludedFolders.Contains(name))
                    || !games.Any(game => !game.IsDemo && game.Id == gameId))
                    throw new InvalidOperationException($"Invalid game recognition mapping: {name}");
                genericGames.Add(gameId);
            }
        }
    }

    private static bool HasBothLanguages(LocalizedText? text) =>
        text is not null && !string.IsNullOrWhiteSpace(text.Zh) && !string.IsNullOrWhiteSpace(text.En);

    private static bool IsRelativePath(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !value.Contains('\\') && !value.Contains(':')
        && !value.Split('/').Any(part => part is "" or "." or "..")
        && !value.Any(char.IsControl);
}
