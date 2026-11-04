using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace FunctionalTests;

public sealed class SubmissionReadTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<HttpResponseMessage> Mark(HttpClient client, string id, string? state = "true",
        string? csrf = null, int page = 1, string lang = "en")
    {
        csrf ??= await Upload.Token(client);
        var fields = new Dictionary<string, string> { ["submission"] = id };
        if (state is not null) fields["isRead"] = state;
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/files?handler=Read&number={page}&lang={lang}")
        { Content = new FormUrlEncodedContent(fields) };
        if (csrf.Length != 0) request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await client.SendAsync(request, Token);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Whole_submission_state_is_explicit_persistent_and_hidden_from_uploaded_files(bool screenshots)
    {
        await using var world = new TestWorld();
        var legacy = world.SeedSubmission();
        var app = world.Start();
        using var client = app.Client();
        await app.RefreshAccess();
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        TestFile[] files = screenshots
            ? [new("first", "first.png", png), new("second", "second.png", png)]
            : [TestFile.Log(), TestFile.Settings()];
        async Task<string> Send()
        {
            using var response = await Upload.Send(client, files, mode: screenshots ? "screenshots" : "manual",
                game: screenshots ? null : "phantom-blade-zero");
            return await Upload.AssertSaved(response, world, files);
        }
        var id = await Send();
        var store = app.Services.GetRequiredService<SubmissionStore>();
        var before = await store.GetStatusAsync(Token);
        var metadataPath = Path.Combine(world.Data, id, "submission.json");
        var originalMetadata = await File.ReadAllBytesAsync(metadataPath, Token);
        var name = screenshots ? "screenshot-01.png" : "UE4SS.log";
        foreach (var handler in screenshots ? new[] { "View", "Download", "Image" } : new[] { "View", "Download" })
        {
            using var response = await client.GetAsync($"/files?handler={handler}&submission={id}&name={name}", Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        Assert.All((await store.BrowseAsync(1, Token)).Submissions, item => Assert.False(item.IsRead));
        for (var repeat = 0; repeat < 2; repeat++)
        {
            using var marked = await Mark(client, id, lang: "zh");
            Assert.Equal(HttpStatusCode.Redirect, marked.StatusCode);
            Assert.Equal("/files?number=1&lang=zh", marked.Headers.Location!.OriginalString);
        }
        var marker = Path.Combine(world.Data, id, ".read");
        Assert.True(File.Exists(marker));
        Assert.Equal(0, new FileInfo(marker).Length);
        Assert.Equal(originalMetadata, await File.ReadAllBytesAsync(metadataPath, Token));
        Assert.Equal(before.UsedBytes, (await store.GetStatusAsync(Token)).UsedBytes);
        var markedSubmission = Assert.Single((await store.BrowseAsync(1, Token)).Submissions, item => item.Id == id);
        Assert.True(markedSubmission.IsRead);
        Assert.Equal(3, markedSubmission.Files.Length); // Two uploads and the existing submission metadata.
        Assert.DoesNotContain(markedSubmission.Files, file => file.Name == ".read");
        foreach (var handler in new[] { "View", "Download", "Image" })
        {
            using var response = await client.GetAsync($"/files?handler={handler}&submission={id}&name=.read", Token);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        var another = await Send(); // The same nickname starts a separate unread submission.
        await app.DisposeAsync();
        var restartedApp = world.Start();
        using var restarted = restartedApp.Client();
        await restartedApp.RefreshAccess();
        store = restartedApp.Services.GetRequiredService<SubmissionStore>();
        var listing = await store.BrowseAsync(1, Token);
        Assert.True(Assert.Single(listing.Submissions, item => item.Id == id).IsRead);
        Assert.False(Assert.Single(listing.Submissions, item => item.Id == legacy).IsRead);
        Assert.False(Assert.Single(listing.Submissions, item => item.Id == another).IsRead);
        Assert.Equal(id, listing.Submissions.Last().Id);
        Assert.Equal(2, Assert.Single((await store.GetStatusAsync(Token)).Folders, folder => folder.Name == id).FileCount);
        for (var repeat = 0; repeat < 2; repeat++)
        {
            using var unread = await Mark(restarted, id, "false");
            Assert.Equal(HttpStatusCode.Redirect, unread.StatusCode);
        }
        Assert.False(File.Exists(marker));
        await restartedApp.DisposeAsync();
        var next = world.Start();
        using var nextClient = next.Client();
        Assert.All((await next.Services.GetRequiredService<SubmissionStore>().BrowseAsync(1, Token)).Submissions,
            item => Assert.False(item.IsRead));
    }

    [Fact]
    public async Task Unread_sorting_happens_before_pagination_and_preserves_newest_order_in_each_group()
    {
        await using var world = new TestWorld();
        var ids = Enumerable.Range(1, 52).Select(index => world.SeedSubmission(index)).ToArray();
        foreach (var id in ids.Skip(2)) world.Write($"data/{id}/.read", "");
        var app = world.Start();
        using var client = app.Client();
        await app.RefreshAccess();
        var store = app.Services.GetRequiredService<SubmissionStore>();
        var first = await store.BrowseAsync(1, Token);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal(new[] { ids[1], ids[0] }.Concat(ids.Skip(4).Reverse()), first.Submissions.Select(item => item.Id));
        Assert.Equal(new[] { ids[3], ids[2] }, (await store.BrowseAsync(2, Token)).Submissions.Select(item => item.Id));
        using var marked = await Mark(client, ids[0], page: 2);
        Assert.Equal(HttpStatusCode.Redirect, marked.StatusCode);
        Assert.Equal("/files?number=2&lang=en", marked.Headers.Location!.OriginalString);
        Assert.Equal(new[] { ids[1] }.Concat(ids.Skip(3).Reverse()),
            (await store.BrowseAsync(1, Token)).Submissions.Select(item => item.Id));
        Assert.Equal(new[] { ids[2], ids[0] }, (await store.BrowseAsync(2, Token)).Submissions.Select(item => item.Id));
    }

    [Theory]
    [InlineData("no-token", 400)]
    [InlineData("bad-token", 400)]
    [InlineData("denied", 403)]
    [InlineData("no-state", 400)]
    [InlineData("bad-state", 400)]
    public async Task Read_changes_require_whitelist_csrf_and_an_explicit_valid_state(string scenario, int status)
    {
        await using var world = new TestWorld();
        var id = world.SeedSubmission();
        var app = world.Start();
        using var client = app.Client();
        await app.RefreshAccess();
        var csrf = await Upload.Token(client);
        if (scenario == "denied") app.ClientAddress = IPAddress.Parse("192.0.2.99");
        using var response = await Mark(client, id,
            scenario == "no-state" ? null : scenario == "bad-state" ? "invalid" : "true",
            scenario == "no-token" ? "" : scenario == "bad-token" ? "invalid" : csrf);
        Assert.Equal(status, (int)response.StatusCode);
        Assert.False(File.Exists(Path.Combine(world.Data, id, ".read")));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unfinished")]
    [InlineData("traversal")]
    public async Task Removed_unfinished_and_invalid_submissions_cannot_be_marked_or_recreated(string scenario)
    {
        await using var world = new TestWorld();
        var id = world.SeedSubmission();
        var app = world.Start();
        using var client = app.Client();
        await app.RefreshAccess();
        Directory.Delete(Path.Combine(world.Data, id), recursive: true);
        if (scenario == "unfinished") world.Write($"data/{id}.uploading/log.txt", "unfinished");
        using var response = await Mark(client, scenario == "traversal" ? "../" + id
            : scenario == "unfinished" ? id + ".uploading" : id);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(Directory.Exists(Path.Combine(world.Data, id)));
        Assert.Empty(Directory.EnumerateFiles(world.Data, ".read", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("directory")]
    [InlineData("file")]
    [InlineData("symlink")]
    public async Task Marker_write_failures_report_error_and_do_not_overwrite_existing_content(string scenario)
    {
        await using var world = new TestWorld();
        var id = world.SeedSubmission();
        var app = world.Start();
        using var client = app.Client();
        await app.RefreshAccess();
        var marker = Path.Combine(world.Data, id, ".read");
        var preserved = scenario switch
        {
            "directory" => world.Write($"data/{id}/.read/keep.txt", "preserve"),
            "file" => world.Write($"data/{id}/.read", "preserve"),
            _ => world.Write("target.txt", "preserve")
        };
        if (scenario == "symlink") File.CreateSymbolicLink(marker, preserved);
        foreach (var state in new[] { "true", "false" })
        {
            using var response = await Mark(client, id, state);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Null(response.Headers.Location);
            Assert.Contains("Couldn't save the read status.",
                WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token)));
            Assert.Equal("preserve", await File.ReadAllTextAsync(preserved, Token));
        }
    }

    [Theory]
    [InlineData("zh", 1365)]
    [InlineData("en", 390)]
    public Task Read_buttons_submit_real_forms_and_reorder_the_list_in_both_languages(string lang, int width) =>
        BrowserTests.InBrowser(nameof(Read_buttons_submit_real_forms_and_reorder_the_list_in_both_languages) + lang,
            async (world, app, page) =>
            {
                var older = world.SeedSubmission(1);
                var newer = world.SeedSubmission(2);
                await app.RefreshAccess();
                await page.GotoAsync("/files?lang=" + lang);
                var cards = page.Locator(".submission-card");
                var card = page.Locator($"[data-submission='{newer}']");
                await Expect(cards.First).ToHaveAttributeAsync("data-submission", newer);
                await Expect(card.Locator(".read-state")).ToHaveTextAsync(lang == "zh" ? "未读" : "Unread");
                await card.GetByRole(AriaRole.Button, new() { Name = lang == "zh" ? "标记已读" : "Mark as read", Exact = true }).ClickAsync();
                await Expect(card.Locator(".read-state")).ToHaveTextAsync(lang == "zh" ? "已读" : "Read");
                await Expect(cards.First).ToHaveAttributeAsync("data-submission", older);
                await card.GetByRole(AriaRole.Button, new() { Name = lang == "zh" ? "标记未读" : "Mark as unread", Exact = true }).ClickAsync();
                await Expect(card.Locator(".read-state")).ToHaveTextAsync(lang == "zh" ? "未读" : "Unread");
                await Expect(cards.First).ToHaveAttributeAsync("data-submission", newer);
                Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
            }, width: width);
}
