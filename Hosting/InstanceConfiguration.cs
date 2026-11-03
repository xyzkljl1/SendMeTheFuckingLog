using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;

namespace SendMeTheFuckingLog.Hosting;

// JSON objects merge by key; arrays replace completely, including an empty array.
// Fetching a private repository is a deployment/synchronization task, never a web request.
public static class InstanceConfiguration
{
    public static void Apply(ConfigurationManager configuration, IWebHostEnvironment environment)
    {
        var root = environment.ContentRootPath;
        var settings = Read(Path.Combine(root, "appsettings.json"));
        var runtimeDirectory = Path.Combine(root, "config");
        var privateDirectory = Path.Combine(root, ".private-config");
        if (Directory.Exists(runtimeDirectory))
            Merge(settings, Read(Path.Combine(runtimeDirectory, "instance.json")));
        else if (environment.IsDevelopment() && Directory.Exists(privateDirectory))
            Merge(settings, Read(Path.Combine(privateDirectory, "instance.json")));

        var environmentFile = $"appsettings.{environment.EnvironmentName}.json";
        var environmentPath = Path.Combine(root, environmentFile);
        if (File.Exists(environmentPath)) Merge(settings, Read(environmentPath));

        // Replace the default JSON sources, rather than layering over their array indices.
        // Existing environment, command-line and test providers retain their precedence.
        var indices = configuration.Sources.Select((source, index) => (source, index))
            .Where(item => item.source is JsonConfigurationSource json
                && (string.Equals(json.Path, "appsettings.json", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(json.Path, environmentFile, StringComparison.OrdinalIgnoreCase)))
            .Select(item => item.index).ToArray();
        var insertAt = indices.Length == 0 ? 0 : indices[0];
        foreach (var index in indices.Reverse()) configuration.Sources.RemoveAt(index);
        configuration.Sources.Insert(insertAt, new MemoryConfigurationSource { InitialData = Flatten(settings) });
    }

    public static JsonObject Read(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length > 512 * 1024 || file.LinkTarget is not null)
            throw new InvalidDataException($"Missing, linked or oversized configuration file: {file.Name}");
        return Parse(File.ReadAllText(path));
    }

    public static JsonObject Parse(string json) => JsonNode.Parse(json,
        new JsonNodeOptions { PropertyNameCaseInsensitive = true },
        new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
        ?? throw new InvalidDataException("Configuration must be a JSON object.");

    public static void Merge(JsonObject target, JsonObject patch, string prefix = "")
    {
        foreach (var (key, value) in patch)
        {
            var path = prefix.Length == 0 ? key : $"{prefix}:{key}";
            if (!target.TryGetPropertyValue(key, out var previous))
            {
                if (!prefix.Equals("Logging:LogLevel", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Unknown configuration key: {path}");
            }
            if (previous is JsonObject existing && value is JsonObject nested)
                Merge(existing, nested, path);
            else
            {
                if (previous is not null && value is not null && previous.GetValueKind() != value.GetValueKind()
                    && !(previous.GetValueKind() is JsonValueKind.True or JsonValueKind.False
                        && value.GetValueKind() is JsonValueKind.True or JsonValueKind.False))
                    throw new InvalidDataException($"Invalid configuration type: {path}");
                target[key] = value?.DeepClone();
            }
        }
    }

    private static IEnumerable<KeyValuePair<string, string?>> Flatten(JsonNode? node, string path = "")
    {
        if (node is JsonObject obj && obj.Count > 0)
        {
            foreach (var (key, value) in obj)
                foreach (var pair in Flatten(value, path.Length == 0 ? key : $"{path}:{key}")) yield return pair;
        }
        else if (node is JsonArray array && array.Count > 0)
        {
            for (var i = 0; i < array.Count; i++)
                foreach (var pair in Flatten(array[i], $"{path}:{i.ToString(CultureInfo.InvariantCulture)}")) yield return pair;
        }
        else
        {
            var value = node is JsonValue scalar
                ? scalar.GetValueKind() == JsonValueKind.String ? scalar.GetValue<string>() : scalar.ToJsonString()
                : null;
            yield return new(path, value);
        }
    }
}
