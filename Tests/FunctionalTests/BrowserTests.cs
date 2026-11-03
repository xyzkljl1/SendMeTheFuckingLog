using Microsoft.Playwright;
using SendMeTheFuckingLog.Models;
using static Microsoft.Playwright.Assertions;

namespace FunctionalTests;

public sealed class BrowserTests
{
    internal static async Task InBrowser(string name, Func<TestWorld, TestApplication, IPage, Task> check,
        string locale = "en-US", int width = 1365)
    {
        await using var world = new TestWorld();
        var app = world.Start(browser: true);
        using var client = app.Client();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await browser.NewContextAsync(new()
        {
            BaseURL = client.BaseAddress!.ToString(), Locale = locale,
            ViewportSize = new() { Width = width, Height = 900 }, AcceptDownloads = true
        });
        // Exercise cross-origin asset URLs and real image decoding without a developer's CDN.
        await context.RouteAsync("https://assets.example.test/**", async route =>
        {
            var assetRoot = Path.GetFullPath(Path.Combine(TestWorld.Project, "wwwroot", "images")) + Path.DirectorySeparatorChar;
            var relative = Uri.UnescapeDataString(new Uri(route.Request.Url).AbsolutePath.TrimStart('/'));
            var asset = Path.GetFullPath(Path.Combine(assetRoot, relative));
            if (!asset.StartsWith(assetRoot, StringComparison.Ordinal) || !File.Exists(asset))
            {
                await route.FulfillAsync(new() { Status = 404 });
                return;
            }
            new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider().TryGetContentType(asset, out var contentType);
            await route.FulfillAsync(new() { Path = asset, ContentType = contentType ?? "application/octet-stream" });
        });
        await context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true, Sources = true });
        var page = await context.NewPageAsync();
        var scriptErrors = new List<string>();
        page.PageError += (_, error) => scriptErrors.Add(error);
        try
        {
            await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await Expect(page.Locator("#author-name")).Not.ToBeEmptyAsync();
            await check(world, app, page);
            Assert.Empty(scriptErrors);
            await context.Tracing.StopAsync();
        }
        catch
        {
            var directory = Path.Combine(TestWorld.Project, "obj", "test-results", "browser", name + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try { await page.ScreenshotAsync(new() { Path = Path.Combine(directory, "failure.png"), FullPage = true }); }
            catch (PlaywrightException) { /* A crashed page can still have a useful trace. */ }
            await context.Tracing.StopAsync(new() { Path = Path.Combine(directory, "trace.zip") });
            throw;
        }
    }

    private static string GameDirectory(TestWorld world, GameCatalog catalog, SupportedGame game)
    {
        const string folder = "game/Game";
        var ue = catalog.Recognition.Ue.FirstOrDefault(pair => pair.Value == game.Id);
        var re = catalog.Recognition.Re.FirstOrDefault(pair => pair.Value == game.Id);
        if (ue.Key is not null) world.Write($"{folder}/{ue.Key}/marker.txt", "folder marker");
        else if (re.Key is not null) world.Write($"{folder}/{re.Key}", "not a real executable");
        else world.Write($"{folder}/{game.ExecutablePaths.First()}", "not a real executable");
        foreach (var file in game.Files) world.Write($"{folder}/{file.Paths[0]}", "fixture " + file.Id);
        world.Write(folder + "/unrelated-game-file.bin", "must never upload");
        return Path.Combine(world.Root, folder);
    }

    private static async Task SendAndVerify(IPage page, TestWorld world, TestFile[] expected)
    {
        await page.Locator("#nickname").FillAsync("Browser Player");
        await Expect(page.Locator("#send-files")).ToBeEnabledAsync();
        var pending = page.WaitForResponseAsync(response => response.Url.EndsWith("/api/uploads", StringComparison.Ordinal)
            && response.Request.Method == "POST");
        await page.Locator("#send-files").ClickAsync();
        var result = await pending;
        using var response = new HttpResponseMessage((HttpStatusCode)result.Status) { Content = new StringContent(await result.TextAsync()) };
        var id = await Upload.AssertSaved(response, world, expected);
        Assert.Equal(expected.Length + 1, Directory.GetFiles(Path.Combine(world.Data, id)).Length);
        await Expect(page.Locator("#upload-receipt")).ToBeVisibleAsync();
        await Expect(page.Locator("#receipt-nickname")).ToHaveTextAsync("Browser Player");
        await Expect(page.Locator("#send-files")).ToBeDisabledAsync();
    }

    [Theory]
    [InlineData("phantom-blade-zero")]
    [InlineData("palworld")]
    [InlineData("dragons-dogma-2")]
    [InlineData("onimusha-way-of-the-sword")]
    public Task Automatic_folder_selection_recognizes_game_and_only_uploads_configured_files(string gameId) =>
        InBrowser(nameof(Automatic_folder_selection_recognizes_game_and_only_uploads_configured_files) + gameId, async (world, app, page) =>
        {
            var catalog = app.Services.GetRequiredService<GameCatalog>();
            var game = catalog.Games.Single(game => game.Id == gameId);
            await page.Locator("#folder-input").SetInputFilesAsync(GameDirectory(world, catalog, game));
            await Expect(page.Locator("#folder-status")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("recognition-success"));
            await Expect(page.Locator("#detected-game-info")).ToContainTextAsync(game.Name.En);
            await Expect(page.Locator("#selected-files li")).ToHaveCountAsync(game.Files.Length);
            await Expect(page.Locator("#automatic-guide")).ToBeVisibleAsync();
            await Expect(page.Locator("#send-files")).ToBeDisabledAsync();
            await SendAndVerify(page, world, game.Files.Select(file =>
                new TestFile(file.Id, Path.GetFileName(file.Paths[0]), Encoding.UTF8.GetBytes("fixture " + file.Id))).ToArray());
        });

    [Theory]
    [InlineData("zh-CN", 1365)]
    [InlineData("en-US", 390)]
    public Task Manual_selection_requires_log_and_accepts_files_added_in_separate_steps(string locale, int width) =>
        InBrowser(nameof(Manual_selection_requires_log_and_accepts_files_added_in_separate_steps) + locale, async (world, app, page) =>
        {
            await page.Locator("#route-manual").ClickAsync();
            await page.Locator("#game-cards input[value='phantom-blade-zero']").CheckAsync();
            await page.Locator("#nickname").FillAsync("Player");
            var settings = world.Write("manual/UE4SS-settings.ini", Encoding.UTF8.GetString(TestFile.Settings().Bytes));
            await page.Locator("#file-input").SetInputFilesAsync(settings);
            await Expect(page.Locator("#send-files")).ToBeDisabledAsync();
            await Expect(page.Locator("#missing-files")).ToContainTextAsync("UE4SS.log");
            var log = world.Write("manual/UE4SS.log", Encoding.UTF8.GetString(TestFile.Log().Bytes));
            var ignored = world.Write("manual/game.exe", "must never upload");
            await page.Locator("#file-input").SetInputFilesAsync([log, ignored]);
            await Expect(page.Locator("#selected-files li")).ToHaveCountAsync(2);
            await SendAndVerify(page, world, [TestFile.Settings(), TestFile.Log()]);
            Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth + 1"), "The narrow page must not overflow horizontally.");
        }, locale, width);

    [Fact]
    public Task Failed_save_keeps_selection_and_allows_retry_without_showing_success() =>
        InBrowser(nameof(Failed_save_keeps_selection_and_allows_retry_without_showing_success), async (world, _, page) =>
        {
            await page.Locator("#route-manual").ClickAsync();
            await page.Locator("#game-cards input[value='phantom-blade-zero']").CheckAsync();
            await page.Locator("#file-input").SetInputFilesAsync(world.Write("manual/UE4SS.log", "retry log"));
            await page.Locator("#nickname").FillAsync("Browser Player");
            world.Write("outside-data/secret.txt", "test only");
            var link = Path.Combine(world.Data, "bad-link");
            File.CreateSymbolicLink(link, Path.Combine(world.Root, "outside-data/secret.txt"));
            var pending = page.WaitForResponseAsync(response => response.Url.EndsWith("/api/uploads", StringComparison.Ordinal));
            await page.Locator("#send-files").ClickAsync();
            Assert.Equal(503, (await pending).Status);
            await Expect(page.Locator("#upload-status")).ToContainTextAsync("could not confirm");
            await Expect(page.Locator("#upload-receipt")).ToBeHiddenAsync();
            await Expect(page.Locator("#selected-files li")).ToHaveCountAsync(1);
            await Expect(page.Locator("#send-files")).ToBeEnabledAsync();
            Assert.Empty(Directory.GetDirectories(world.Data));
            File.Delete(link);
            await SendAndVerify(page, world, [TestFile.Log("retry log")]);
        });

    [Fact]
    public Task Language_nickname_game_and_route_survive_reload_but_selected_files_do_not() =>
        InBrowser(nameof(Language_nickname_game_and_route_survive_reload_but_selected_files_do_not), async (world, _, page) =>
        {
            await Expect(page.Locator("html")).ToHaveAttributeAsync("lang", "en");
            await page.Locator("[data-language='zh']").ClickAsync();
            await page.Locator("#route-manual").ClickAsync();
            await page.Locator("#game-cards input[value='phantom-blade-zero']").CheckAsync();
            await page.Locator("#nickname").FillAsync("Saved player");
            await page.Locator("#file-input").SetInputFilesAsync(world.Write("manual/UE4SS.log", "log"));
            await page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await Expect(page.Locator("html")).ToHaveAttributeAsync("lang", "zh-CN");
            await Expect(page.Locator("#route-manual")).ToHaveAttributeAsync("aria-pressed", "true");
            await Expect(page.Locator("#game-cards input[value='phantom-blade-zero']")).ToBeCheckedAsync();
            await Expect(page.Locator("#nickname")).ToHaveValueAsync("Saved player");
            await Expect(page.Locator("#selected-files li")).ToHaveCountAsync(0);
            await Expect(page.Locator("#send-files")).ToBeDisabledAsync();
            await page.Locator("#route-auto").ClickAsync();
            await page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await Expect(page.Locator("#route-auto")).ToHaveAttributeAsync("aria-pressed", "true");
            await page.Locator("[data-language='en']").ClickAsync();
            await Expect(page.Locator("html")).ToHaveAttributeAsync("lang", "en");
        });

    [Theory]
    [InlineData("unknown")]
    [InlineData("ambiguous")]
    [InlineData("no-log")]
    public Task Unrecognized_ambiguous_or_logless_folders_cannot_be_sent(string scenario) =>
        InBrowser(nameof(Unrecognized_ambiguous_or_logless_folders_cannot_be_sent) + scenario, async (world, _, page) =>
        {
            world.Write("game/Game/readme.txt", "fixture");
            if (scenario == "ambiguous")
            {
                world.Write("game/Game/Pal/marker.txt", "fixture");
                world.Write("game/Game/DD2.exe", "fixture");
            }
            if (scenario == "no-log") world.Write("game/Game/platform/steam/PBZ.exe", "fixture");
            await page.Locator("#folder-input").SetInputFilesAsync(Path.Combine(world.Root, "game/Game"));
            await Expect(page.Locator("#auto-help")).ToBeVisibleAsync();
            await page.Locator("#nickname").FillAsync("Player");
            await Expect(page.Locator("#send-files")).ToBeDisabledAsync();
            Assert.Empty(Directory.GetDirectories(world.Data));
        });

    [Fact]
    public Task Dropping_multiple_root_items_uses_the_same_recognition_and_upload_flow() =>
        InBrowser(nameof(Dropping_multiple_root_items_uses_the_same_recognition_and_upload_flow), async (world, _, page) =>
        {
            // Browser automation cannot drag OS folders. Model FileSystemEntry objects at the DOM drop boundary.
            await page.EvaluateAsync("""
                () => {
                    const file = (name, text) => ({ name, isFile: true, isDirectory: false,
                        file: callback => callback(new File([text], name)) });
                    const dir = (name, children) => ({ name, isDirectory: true, isFile: false,
                        createReader: () => { let sent = false; return { readEntries: callback => {
                            callback(sent ? [] : children); sent = true;
                        } }; } });
                    const entries = [dir('Engine', []), dir('platform', [dir('steam', [
                        file('PBZ.exe', 'not a real executable'), dir('ue4ss', [file('UE4SS.log', 'dropped log')])
                    ])])];
                    const event = new Event('drop', { bubbles: true, cancelable: true });
                    Object.defineProperty(event, 'dataTransfer', { value: { items: entries.map(entry => ({
                        kind: 'file', webkitGetAsEntry: () => entry
                    })) } });
                    document.querySelector('#folder-drop').dispatchEvent(event);
                }
                """);
            await Expect(page.Locator("#detected-game-info")).ToContainTextAsync("Phantom Blade Zero");
            await Expect(page.Locator("#selected-files li")).ToHaveCountAsync(1);
            await SendAndVerify(page, world, [TestFile.Log("dropped log")]);
        });

    [Theory]
    [InlineData("zh")]
    [InlineData("en")]
    public Task Hosted_covers_and_guide_images_load_as_images_in_both_languages(string language) =>
        InBrowser(nameof(Hosted_covers_and_guide_images_load_as_images_in_both_languages) + language, async (_, app, page) =>
        {
            await page.Locator($"[data-language='{language}']").ClickAsync();
            var catalog = app.Services.GetRequiredService<GameCatalog>();
            var automaticGuide = Assert.Single(catalog.AutomaticGuideImages);
            Assert.Contains($"-{language}.gif?v=", language == "zh" ? automaticGuide.Zh : automaticGuide.En);
            var urls = catalog.Games.Select(game => game.Image)
                .Concat(catalog.AutomaticGuideImages.Select(image => language == "zh" ? image.Zh : image.En))
                .Concat(catalog.Games.SelectMany(game => game.Instructions).SelectMany(step => step.ImageUrls)
                    .Select(image => language == "zh" ? image.Zh : image.En))
                .OfType<string>().Where(url => url.Length > 0).Distinct().ToArray();
            Assert.NotEmpty(urls);
            foreach (var url in urls)
            {
                Assert.StartsWith("https://", url);
                // Decode the real asset bytes served through the controlled hosted origin.
                var loaded = await page.EvaluateAsync<bool>("""
                    async url => {
                        for (let attempt = 0; attempt < 3; attempt++) {
                            const ok = await new Promise(resolve => {
                                const image = new Image();
                                const timer = setTimeout(() => { image.src = ''; resolve(false); }, 15000);
                                image.referrerPolicy = 'no-referrer';
                                image.onload = () => { clearTimeout(timer); resolve(image.naturalWidth > 0); };
                                image.onerror = () => { clearTimeout(timer); resolve(false); };
                                image.src = url;
                            });
                            if (ok) return true;
                        }
                        return false;
                    }
                    """, url);
                Assert.True(loaded, "Hosted image failed to load: " + url);
            }
            await page.Locator("#route-manual").ClickAsync();
            await page.Locator("#game-cards input[value='phantom-blade-zero']").CheckAsync();
            var guideSources = await page.Locator("#guide-steps img").EvaluateAllAsync<string[]>("images => images.map(image => image.src)");
            Assert.NotEmpty(guideSources);
            Assert.All(guideSources, url => Assert.Contains("-" + language + ".gif", url));
        });
}
