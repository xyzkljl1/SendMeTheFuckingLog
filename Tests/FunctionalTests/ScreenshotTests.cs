using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace FunctionalTests;

public sealed class ScreenshotTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    public async Task Screenshots_without_game_are_saved_and_only_the_author_can_view_or_download_them()
    {
        await using var world = new TestWorld();
        var app = world.Start();
        using var client = app.Client();
        TestFile[] images = [new("first", "游戏 截图.png", Png), new("second", "screen.png", Png)];
        using var response = await Upload.Send(client, images, mode: "screenshots", game: null, nickname: "Screenshot Player");
        var id = await Upload.AssertSaved(response, world, images);
        Assert.Contains("_screenshots_", id);
        var metadata = JsonSerializer.Deserialize<SubmissionInfo>(await File.ReadAllTextAsync(
            Path.Combine(world.Data, id, "submission.json"), TestContext.Current.CancellationToken), Upload.Json)!;
        Assert.Null(metadata.GameId);
        Assert.Equal("screenshots", metadata.Kind);
        Assert.Equal("Screenshot Player", metadata.Nickname);
        await app.RefreshAccess();
        foreach (var language in new[] { "zh", "en" })
        {
            var html = WebUtility.HtmlDecode(await client.GetStringAsync("/files?lang=" + language, TestContext.Current.CancellationToken));
            Assert.Contains(language == "zh" ? "截图" : "Screenshots", html);
            Assert.Contains("handler=Image", html);
        }
        foreach (var handler in new[] { "View", "Image", "Download" })
        {
            var url = $"/files?handler={handler}&submission={id}&name=screenshot-01.png";
            using var allowed = await client.GetAsync(url, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            Assert.True(allowed.Headers.CacheControl!.NoStore);
            if (handler != "View") Assert.Equal(Png, await allowed.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
            if (handler == "Image")
            {
                Assert.Equal("image/png", allowed.Content.Headers.ContentType!.MediaType);
                Assert.Equal("nosniff", allowed.Headers.GetValues("X-Content-Type-Options").Single());
            }
            app.ClientAddress = IPAddress.Parse("192.0.2.30");
            using var denied = await client.GetAsync(url, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            app.ClientAddress = IPAddress.Loopback;
        }
        using var nonImage = await client.GetAsync($"/files?handler=Image&submission={id}&name=submission.json", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, nonImage.StatusCode);
        await app.DisposeAsync();
        var restartedApp = world.Start();
        using var restarted = restartedApp.Client();
        await restartedApp.RefreshAccess();
        using var saved = await restarted.GetAsync($"/files?handler=Image&submission={id}&name=screenshot-01.png", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    [Theory]
    [InlineData("fake", "invalidImage")]
    [InlineData("extension", "invalidImage")]
    [InlineData("svg", "invalidImage")]
    [InlineData("path", "invalidSubmission")]
    [InlineData("game", "invalidSubmission")]
    [InlineData("count", "invalidSubmission")]
    [InlineData("hash", "incompleteUpload")]
    [InlineData("truncated", "incompleteUpload")]
    public async Task Invalid_screenshot_submissions_never_publish_partial_data(string scenario, string error)
    {
        await using var world = new TestWorld();
        using var client = world.Start().Client();
        var image = new TestFile("image", "screen.png", Png);
        var bad = scenario switch
        {
            "fake" => image with { Bytes = Encoding.UTF8.GetBytes("<script>not an image</script>") },
            "extension" => image with { FileName = "screen.jpg" },
            "svg" => image with { FileName = "screen.svg" },
            "path" => image with { FileName = "../screen.png" },
            _ => image
        };
        var files = scenario == "count" ? Enumerable.Range(0, 33).Select(index => image with { ItemId = "image-" + index }).ToArray()
            : new[] { image with { ItemId = "valid", FileName = "first.png" }, bad };
        using var response = await Upload.Send(client, files, mode: "screenshots",
            game: scenario == "game" ? "phantom-blade-zero" : null, wrongHash: scenario == "hash", truncate: scenario == "truncated");
        await Upload.AssertError(response, 400, error);
        Assert.Empty(Directory.GetDirectories(world.Data));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Screenshot_uploads_obey_file_and_submission_size_limits(bool total)
    {
        await using var world = new TestWorld();
        using var client = world.Start(new()
        {
            ["Storage:MaxFileMiB"] = "0.0625", ["Storage:MaxSubmissionMiB"] = "0.0625"
        }).Client();
        var image = new TestFile("first", "screen.png", new byte[total ? 40000 : 65537]);
        using var response = await Upload.Send(client, total ? [image, image with { ItemId = "second" }] : [image], mode: "screenshots", game: null);
        await Upload.AssertError(response, 413, "tooLarge");
        Assert.Empty(Directory.GetDirectories(world.Data));
    }

    [Theory]
    [InlineData("zh-CN", 1800)]
    [InlineData("en-US", 390)]
    public Task Picker_drag_and_paste_upload_multiple_formats_without_a_game(string locale, int width) =>
        BrowserTests.InBrowser(nameof(Picker_drag_and_paste_upload_multiple_formats_without_a_game) + locale, async (world, app, page) =>
        {
            await page.Locator("#route-screenshots").ClickAsync();
            await Expect(page.Locator("#manual-panel")).ToBeHiddenAsync();
            await Expect(page.Locator("#supported-games-sidebar")).ToBeHiddenAsync();
            await Expect(page.Locator("#upload-title")).ToHaveTextAsync(locale == "zh-CN" ? "上传截图给" : "Upload screenshots to");
            await Expect(page.Locator("#send-files")).ToBeDisabledAsync();
            var data = await page.EvaluateAsync<string[]>("""
                () => {
                    const canvas = document.createElement('canvas'); canvas.width = 4; canvas.height = 4;
                    canvas.getContext('2d').fillRect(0, 0, 4, 4);
                    return ['image/png', 'image/jpeg', 'image/webp'].map(type => canvas.toDataURL(type).split(',')[1]);
                }
                """);
            await page.Locator("#screenshots-input").SetInputFilesAsync(new FilePayload
            {
                Name = "截图.png", MimeType = "image/png", Buffer = Convert.FromBase64String(data[0])
            });
            await page.EvaluateAsync("""
                data => {
                    for (let i = 0; i < 2; i++) {
                        const transfer = new DataTransfer();
                        const type = i === 0 ? 'image/jpeg' : 'image/webp';
                        const name = i === 0 ? 'drag.jpg' : 'paste.webp';
                        transfer.items.add(new File([Uint8Array.from(atob(data[i + 1]), char => char.charCodeAt(0))], name, { type }));
                        if (i === 0) document.getElementById('screenshots-drop').dispatchEvent(new DragEvent('drop', { bubbles: true, dataTransfer: transfer }));
                        else document.dispatchEvent(new ClipboardEvent('paste', { bubbles: true, clipboardData: transfer }));
                    }
                }
                """, data);
            await Expect(page.Locator("#screenshot-files li")).ToHaveCountAsync(3);
            await page.WaitForFunctionAsync("Array.from(document.querySelectorAll('#screenshot-files img')).every(image => image.complete && image.naturalWidth > 0)");
            await page.Locator("#nickname").FillAsync("Screenshot Player");
            await Expect(page.Locator("#send-files")).ToBeEnabledAsync();
            await page.Locator("#route-manual").ClickAsync();
            await Expect(page.Locator("#supported-games-sidebar")).ToBeHiddenAsync();
            await Expect(page.Locator("#send-files")).ToBeDisabledAsync();
            await page.Locator("#route-screenshots").ClickAsync();
            await Expect(page.Locator("#screenshot-files li")).ToHaveCountAsync(3);
            await page.Locator("[data-language='en']").ClickAsync();
            await Expect(page.Locator("#choose-screenshots")).ToHaveTextAsync("Choose screenshots");
            var pending = page.WaitForResponseAsync(response => response.Url.EndsWith("/api/uploads", StringComparison.Ordinal));
            await page.Locator("#send-files").ClickAsync();
            var result = await pending;
            using var response = new HttpResponseMessage((HttpStatusCode)result.Status) { Content = new StringContent(await result.TextAsync()) };
            var images = new[] { "截图.png", "drag.jpg", "paste.webp" }.Select((name, i) => new TestFile("image-" + i, name, Convert.FromBase64String(data[i]))).ToArray();
            var id = await Upload.AssertSaved(response, world, images);
            await Expect(page.Locator("#upload-receipt")).ToBeVisibleAsync();
            Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth + 1"));
            await app.RefreshAccess();
            await page.GotoAsync($"/files?handler=View&submission={id}&name=screenshot-01.png&lang=en");
            await Expect(page.Locator(".image-preview")).ToBeVisibleAsync();
            await page.WaitForFunctionAsync("document.querySelector('.image-preview').naturalWidth === 4");
        }, locale, width);

    [Fact]
    public Task Invalid_files_and_size_errors_stay_near_screenshots_and_removing_a_file_releases_its_preview() =>
        BrowserTests.InBrowser(nameof(Invalid_files_and_size_errors_stay_near_screenshots_and_removing_a_file_releases_its_preview), async (_, _, page) =>
        {
            await page.Locator("#route-screenshots").ClickAsync();
            await page.Locator("#nickname").FillAsync("Player");
            await page.Locator("#screenshots-input").SetInputFilesAsync(new FilePayload { Name = "notes.txt", MimeType = "text/plain", Buffer = [1] });
            await Expect(page.Locator("#screenshots-status")).ToContainTextAsync("PNG");
            await Expect(page.Locator("#screenshot-files li")).ToHaveCountAsync(0);
            await page.Locator("#screenshots-input").SetInputFilesAsync(new FilePayload { Name = "large.png", MimeType = "image/png", Buffer = new byte[129 * 1024] });
            await Expect(page.Locator("#screenshots-size-error")).ToContainTextAsync("per-file limit");
            await Expect(page.Locator("#send-files")).ToBeDisabledAsync();
            var url = await page.Locator(".screenshot-preview").GetAttributeAsync("href");
            await page.Locator(".screenshot-remove").ClickAsync();
            await Expect(page.Locator("#screenshot-files li")).ToHaveCountAsync(0);
            await Expect(page.Locator("#screenshots-size-error")).ToBeHiddenAsync();
            Assert.False(await page.EvaluateAsync<bool>("async url => { try { await fetch(url); return true; } catch { return false; } }", url));
            await Expect(page.Locator("#send-files")).ToBeDisabledAsync();
        });
}
