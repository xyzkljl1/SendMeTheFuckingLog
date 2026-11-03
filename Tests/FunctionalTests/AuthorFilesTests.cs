namespace FunctionalTests;

public sealed class AuthorFilesTests
{
    [Fact]
    public async Task Domain_refresh_replaces_addresses_and_dns_failure_or_expiry_revokes_access()
    {
        await using var world = new TestWorld();
        world.Resolver.Addresses["first.example"] = [IPAddress.Loopback];
        world.Resolver.Addresses["second.example"] = [IPAddress.Parse("2001:db8::1")];
        var app = world.Start(new()
        {
            ["AuthorAccess:AllowedClients:0"] = "first.example",
            ["AuthorAccess:AllowedClients:1"] = "second.example"
        });
        using var client = app.Client();
        await app.RefreshAccess();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/files", TestContext.Current.CancellationToken)).StatusCode);
        world.Resolver.Addresses["first.example"] = [IPAddress.Parse("192.0.2.10")];
        await app.RefreshAccess();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/files", TestContext.Current.CancellationToken)).StatusCode);
        app.ClientAddress = IPAddress.Parse("192.0.2.10");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/files", TestContext.Current.CancellationToken)).StatusCode);
        world.Resolver.Addresses.Remove("first.example");
        await app.RefreshAccess();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/files", TestContext.Current.CancellationToken)).StatusCode);
        app.ClientAddress = IPAddress.Parse("2001:db8::1");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/files", TestContext.Current.CancellationToken)).StatusCode);
        world.Clock.Advance(TimeSpan.FromMinutes(12));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/files", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Theory]
    [InlineData("127.0.0.1", "::ffff:127.0.0.1", 200)]
    [InlineData("2001:db8::2", "2001:db8::2", 200)]
    [InlineData("*", "203.0.113.10", 200)]
    [InlineData("*", "2001:db8::2", 200)]
    [InlineData("*", null, 403)]
    [InlineData("192.0.2.10", "127.0.0.1", 403)]
    [InlineData("https://invalid.example/path", "127.0.0.1", 403)]
    public async Task Literal_ips_wildcard_and_invalid_settings_are_checked(string allowed, string? peer, int status)
    {
        await using var world = new TestWorld();
        var app = world.Start(new()
        {
            ["AuthorAccess:AllowedClients:0"] = allowed,
            ["AuthorAccess:AllowedClients:1"] = "failed.example"
        });
        app.ClientAddress = peer is null ? null : IPAddress.Parse(peer);
        using var client = app.Client();
        await app.RefreshAccess();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "192.0.2.10");
        client.DefaultRequestHeaders.Add("CF-Connecting-IP", "192.0.2.10");
        using var response = await client.GetAsync("/files", TestContext.Current.CancellationToken);
        Assert.Equal(status, (int)response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var config = app.Services.GetRequiredService<IConfiguration>();
        config["AuthorAccess:AllowedClients:0"] = "*";
        config["AuthorAccess:AllowedClients:1"] = "https://invalid.example/path";
        await app.RefreshAccess();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/files", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task All_author_handlers_require_access_and_revocation_takes_effect()
    {
        await using var world = new TestWorld();
        var id = world.SeedSubmission();
        var app = world.Start();
        using var client = app.Client();
        await app.RefreshAccess();
        foreach (var handler in new[] { "", "View", "Download" })
        {
            app.ClientAddress = IPAddress.Loopback;
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/Files/?handler={handler}&submission={id}&name=UE4SS.log", TestContext.Current.CancellationToken)).StatusCode);
            app.ClientAddress = IPAddress.Parse("192.0.2.99");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/Files/?handler={handler}&submission={id}&name=UE4SS.log", TestContext.Current.CancellationToken)).StatusCode);
        }
    }

    [Fact]
    public async Task Author_can_list_preview_and_download_safely_in_both_languages()
    {
        await using var world = new TestWorld();
        const string text = "<script>alert('unsafe')</script>\nhello log";
        var id = world.SeedSubmission(content: text, nickname: "<Author & User>");
        world.Write($"data/{id}/large.log", new string('a', 70000));
        world.Write($"data/{id}/binary.dmp", "binary\0data");
        var app = world.Start();
        using var client = app.Client();
        await app.RefreshAccess();
        world.Write($"data/{id}.uploading/secret.log", "unfinished");
        foreach (var language in new[] { "zh", "en" })
        {
            var html = await client.GetStringAsync("/files?lang=" + language, TestContext.Current.CancellationToken);
            Assert.Contains("UE4SS.log", html);
            Assert.Contains("&lt;Author &amp; User&gt;", html);
            Assert.DoesNotContain("secret.log", html);
            Assert.DoesNotContain(".uploading", html);
            Assert.Contains("html lang=\"" + (language == "zh" ? "zh-CN" : "en"), html);
        }
        using var preview = await client.GetAsync($"/files?handler=View&submission={id}&name=UE4SS.log", TestContext.Current.CancellationToken);
        var previewText = await preview.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("&lt;script&gt;", previewText);
        Assert.DoesNotContain("<script>", previewText);
        Assert.True(preview.Headers.Contains("Content-Security-Policy"));
        var large = await client.GetStringAsync($"/files?handler=View&submission={id}&name=large.log&lang=en", TestContext.Current.CancellationToken);
        Assert.Contains("first 64K", large);
        var binary = WebUtility.HtmlDecode(await client.GetStringAsync($"/files?handler=View&submission={id}&name=binary.dmp&lang=en", TestContext.Current.CancellationToken));
        Assert.Contains("can't be shown as text", binary);
        using var download = await client.GetAsync($"/files?handler=Download&submission={id}&name=UE4SS.log", TestContext.Current.CancellationToken);
        Assert.Equal(text, await download.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
        Assert.True(download.Headers.CacheControl!.NoStore);
        world.Write($"data/{id}/submission.json", "broken json");
        Assert.Contains("UE4SS.log", await client.GetStringAsync("/files", TestContext.Current.CancellationToken));
        File.Delete(Path.Combine(world.Data, id, "UE4SS.log"));
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/files?handler=Download&submission={id}&name=UE4SS.log", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Theory]
    [InlineData("../submission.json")]
    [InlineData("..\\submission.json")]
    [InlineData("UE4SS.log:stream")]
    [InlineData("missing.log")]
    public async Task Download_rejects_invalid_paths_and_missing_files(string name)
    {
        await using var world = new TestWorld();
        var id = world.SeedSubmission();
        var app = world.Start();
        using var client = app.Client();
        await app.RefreshAccess();
        foreach (var submission in new[] { id, "..", id + ".uploading" })
        {
            using var response = await client.GetAsync($"/files?handler=Download&submission={Uri.EscapeDataString(submission)}&name={Uri.EscapeDataString(name)}", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}
