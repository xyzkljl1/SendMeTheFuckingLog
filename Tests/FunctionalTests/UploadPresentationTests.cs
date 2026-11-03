using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace FunctionalTests;

public sealed class UploadPresentationTests
{
    private static Task InBrowser(Func<IPage, Task> check, [System.Runtime.CompilerServices.CallerMemberName] string name = "") =>
        BrowserTests.InBrowser(name, (_, _, page) => check(page));

    private static FilePayload File(string name, int bytes) => new()
    {
        Name = name, MimeType = "application/octet-stream", Buffer = new byte[bytes]
    };

    [Fact]
    public Task Supported_games_use_spare_space_without_shrinking_the_workspace() => InBrowser(async page =>
    {
        var workspace = page.Locator(".workspace");
        var original = (await workspace.BoundingBoxAsync())!;
        await Expect(page.Locator("#supported-games")).ToBeHiddenAsync();
        await page.Locator("#supported-games-panel summary").ClickAsync();
        await Expect(page.Locator("#supported-games")).ToBeVisibleAsync();
        var expanded = (await workspace.BoundingBoxAsync())!;
        Assert.Equal(original.Width, expanded.Width);
        Assert.Equal(original.Y, expanded.Y);

        await page.SetViewportSizeAsync(1800, 900);
        await Expect(page.Locator("#supported-games")).ToBeVisibleAsync();
        var wide = (await workspace.BoundingBoxAsync())!;
        var sidebar = (await page.Locator(".supported-sidebar").BoundingBoxAsync())!;
        Assert.Equal(original.Width, wide.Width);
        Assert.True(sidebar.X >= wide.X + wide.Width);
        Assert.InRange(Math.Abs(wide.X + wide.Width / 2 - 900), 0, 1);
        await Expect(page.Locator("#supported-games .game-name-secondary").First).ToBeHiddenAsync();
        await page.Locator("[data-language='zh']").ClickAsync();
        await Expect(page.Locator("#supported-games .game-name-secondary").First).ToBeVisibleAsync();

        await page.SetViewportSizeAsync(390, 844);
        await Expect(page.Locator("#supported-games")).ToBeHiddenAsync();
        await page.Locator("#supported-games-panel summary").ClickAsync();
        await Expect(page.Locator("#supported-games")).ToBeVisibleAsync();
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth + 1"));
    });

    [Fact]
    public Task Optional_files_are_collapsed_and_upload_limits_appear_only_on_error() => InBrowser(async page =>
    {
        await page.Locator("#route-manual").ClickAsync();
        await page.Locator("#game-cards input[value='phantom-blade-zero']").CheckAsync();
        await Expect(page.Locator("#optional-guide-steps")).ToBeHiddenAsync();
        await Expect(page.Locator("#guide-steps")).Not.ToContainTextAsync("UE4SS-settings.ini");
        await Expect(page.Locator("#manual-size-error")).ToBeHiddenAsync();
        await Expect(page.GetByLabel("Enter your nickname", new() { Exact = true })).ToHaveCountAsync(1);
        await page.Locator("#optional-files summary").ClickAsync();
        await Expect(page.Locator("#optional-guide-steps")).ToContainTextAsync("UE4SS-settings.ini");
        await page.Locator("[data-language='zh']").ClickAsync();
        await Expect(page.Locator("#optional-guide-steps")).ToContainTextAsync("只有作者要求时才需要上传");
        await page.Locator("#nickname").FillAsync("Player");

        // TestWorld configures 128 KiB per file, independently of production's 20 MiB limit.
        await page.Locator("#file-input").SetInputFilesAsync(File("UE4SS.log", 129 * 1024));
        await Expect(page.Locator("#manual-size-error")).ToContainTextAsync("UE4SS.log");
        await Expect(page.Locator("#manual-size-error")).ToContainTextAsync("129.0 KB");
        await Expect(page.Locator("#manual-size-error")).ToContainTextAsync("128.0 KB");
        await Expect(page.Locator("#send-files")).ToBeDisabledAsync();
        await Expect(page.Locator("#upload-status")).ToBeEmptyAsync();
        await page.Locator("[data-language='en']").ClickAsync();
        await Expect(page.Locator("#manual-size-error")).ToContainTextAsync("per-file limit");

        await page.Locator("#file-input").SetInputFilesAsync(File("UE4SS.log", 1024));
        await Expect(page.Locator("#manual-size-error")).ToBeHiddenAsync();
        await Expect(page.Locator("#send-files")).ToBeEnabledAsync();
        await Expect(page.Locator("#selected-files li")).ToHaveCountAsync(1);
        await Expect(page.Locator("#selected-files")).Not.ToContainTextAsync("Phantom Blade Zero");
    });

    [Fact]
    public Task Combined_file_size_and_server_rejections_are_shown_beside_the_drop_zone() => InBrowser(async page =>
    {
        await page.Locator("#route-manual").ClickAsync();
        await page.Locator("#game-cards input[value='dragons-dogma-2']").CheckAsync();
        await page.Locator("#nickname").FillAsync("Player");
        await page.Locator("#file-input").SetInputFilesAsync(new[]
        {
            File("re2_framework_log.txt", 96 * 1024), File("re2_fw_config.txt", 96 * 1024),
            File("reframework_crash.dmp", 96 * 1024)
        });
        await Expect(page.Locator("#manual-size-error")).ToContainTextAsync("288.0 KB");
        await Expect(page.Locator("#manual-size-error")).ToContainTextAsync("256.0 KB");
        await Expect(page.Locator("#send-files")).ToBeDisabledAsync();
        await page.Locator("#file-input").SetInputFilesAsync(File("reframework_crash.dmp", 1024));
        await Expect(page.Locator("#manual-size-error")).ToBeHiddenAsync();
        await Expect(page.Locator("#send-files")).ToBeEnabledAsync();

        await page.RouteAsync("**/api/uploads", route => route.FulfillAsync(new()
        {
            Status = 413, ContentType = "application/json", Body = "{\"error\":\"tooLarge\"}"
        }));
        await page.Locator("#send-files").ClickAsync();
        await Expect(page.Locator("#manual-size-error")).ToContainTextAsync("128.0 KB");
        await Expect(page.Locator("#manual-size-error")).ToContainTextAsync("256.0 KB");
        await Expect(page.Locator("#upload-status")).ToBeEmptyAsync();
        await Expect(page.Locator("#upload-receipt")).ToBeHiddenAsync();
    });
}
