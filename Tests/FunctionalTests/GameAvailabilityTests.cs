using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FunctionalTests;

public sealed class GameAvailabilityTests
{
    [Theory]
    [InlineData("palworld")]
    [InlineData("dragons-dogma-2")]
    [InlineData("phantom-blade-zero")]
    [InlineData("all")]
    public async Task Disabled_games_are_absent_from_page_and_recognition_and_cannot_receive_uploads(string disabled)
    {
        await using var world = new TestWorld();
        world.Write("site/appsettings.json", File.ReadAllText(Path.Combine(TestWorld.Project, "appsettings.json")));
        world.Write("site/Configuration/GameRecognition.json",
            File.ReadAllText(Path.Combine(TestWorld.Project, "Configuration", "GameRecognition.json")));
        var disabledIds = new HashSet<string>();
        var enabledIds = new HashSet<string>();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(TestWorld.Project, "Configuration", "Games"), "*.json"))
        {
            var game = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            var id = game["id"]!.GetValue<string>();
            if (disabled == "all" || id == disabled)
            {
                game["enable"] = false;
                disabledIds.Add(id);
            }
            else
            {
                // Older configurations without the switch remain enabled.
                game.Remove("enable");
                enabledIds.Add(id);
            }
            world.Write("site/Configuration/Games/" + Path.GetFileName(path), game.ToJsonString());
        }

        var app = world.Start();
        app.ContentRoot = Path.Combine(world.Root, "site");
        using var client = app.Client();
        using var page = await client.GetAsync("/", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var match = Regex.Match(html, "<script id=\"page-configuration\"[^>]*>(.*?)</script>", RegexOptions.Singleline);
        Assert.True(match.Success);
        using var config = JsonDocument.Parse(match.Groups[1].Value);
        var shownIds = config.RootElement.GetProperty("games").EnumerateArray()
            .Select(game => game.GetProperty("id").GetString()!).ToHashSet();
        Assert.True(enabledIds.SetEquals(shownIds));
        foreach (var table in config.RootElement.GetProperty("recognition").EnumerateObject())
            foreach (var mapping in table.Value.EnumerateObject())
                Assert.DoesNotContain(mapping.Value.GetString()!, disabledIds);

        foreach (var id in disabledIds)
            foreach (var mode in new[] { "manual", "auto" })
            {
                using var response = await Upload.Send(client, [TestFile.Log()], game: id, mode: mode);
                await Upload.AssertError(response, 400, "unsupportedGame");
            }
        Assert.Empty(Directory.GetDirectories(world.Data));
    }
}
