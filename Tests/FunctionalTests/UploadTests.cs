namespace FunctionalTests;

public sealed class UploadTests
{
    [Theory]
    [InlineData("manual")]
    [InlineData("auto")]
    public async Task Successful_upload_is_already_saved_with_exact_bytes_and_metadata(string mode)
    {
        await using var world = new TestWorld();
        var app = world.Start();
        using var client = app.Client();
        var files = new[] { TestFile.Log(), TestFile.Settings() };
        using var response = await Upload.Send(client, files, mode, "测试 Player");
        var id = await Upload.AssertSaved(response, world, files);
        using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(world.Data, id, "submission.json"), TestContext.Current.CancellationToken));
        Assert.Equal("测试 Player", metadata.RootElement.GetProperty("nickname").GetString());
        Assert.Equal("phantom-blade-zero", metadata.RootElement.GetProperty("gameId").GetString());
        await app.DisposeAsync();
        var restarted = world.Start();
        using var afterRestart = restarted.Client();
        await restarted.RefreshAccess();
        using var download = await afterRestart.GetAsync($"/files?handler=Download&submission={id}&name=UE4SS.log", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(files[0].Bytes, await download.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Manual_upload_requires_primary_log_but_auto_can_send_available_secondary_file()
    {
        await using var world = new TestWorld();
        using var client = world.Start().Client();
        using var rejected = await Upload.Send(client, [TestFile.Settings()]);
        await Upload.AssertError(rejected, 400, "missingRequired");
        Assert.Empty(Directory.GetDirectories(world.Data));
        using var accepted = await Upload.Send(client, [TestFile.Settings()], mode: "auto");
        await Upload.AssertSaved(accepted, world, TestFile.Settings());
    }

    [Theory]
    [InlineData("nickname", "invalidNickname")]
    [InlineData("game", "unsupportedGame")]
    [InlineData("file", "invalidFile")]
    [InlineData("path", "invalidSubmission")]
    [InlineData("duplicate", "invalidSubmission")]
    [InlineData("mode", "invalidSubmission")]
    [InlineData("empty", "invalidSubmission")]
    public async Task Invalid_submissions_are_rejected_without_creating_files(string scenario, string error)
    {
        await using var world = new TestWorld();
        using var client = world.Start().Client();
        TestFile[] files = scenario switch
        {
            "file" => [TestFile.Log() with { FileName = "game.exe" }],
            "path" => [TestFile.Log() with { FileName = "../UE4SS.log" }],
            "duplicate" => [TestFile.Log(), TestFile.Log()],
            "empty" => [],
            _ => [TestFile.Log()]
        };
        using var response = await Upload.Send(client, files,
            nickname: scenario == "nickname" ? " " : "Player",
            game: scenario == "game" ? "unknown-game" : "phantom-blade-zero",
            mode: scenario == "mode" ? "unknown" : "manual");
        await Upload.AssertError(response, 400, error);
        Assert.Empty(Directory.GetDirectories(world.Data));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Hash_mismatch_or_short_file_never_reports_success_and_removes_partial_data(bool wrongHash)
    {
        await using var world = new TestWorld();
        using var client = world.Start().Client();
        using var response = await Upload.Send(client, [TestFile.Log()], wrongHash: wrongHash, truncate: !wrongHash);
        await Upload.AssertError(response, 400, "incompleteUpload");
        Assert.Empty(Directory.GetDirectories(world.Data));
        using var retry = await Upload.Send(client, [TestFile.Log()]);
        await Upload.AssertSaved(retry, world, TestFile.Log());
    }

    [Fact]
    public async Task Missing_invalid_or_previous_instance_csrf_token_requires_refresh()
    {
        await using var world = new TestWorld();
        var app = world.Start();
        using var client = app.Client();
        using var originalPage = await client.GetAsync("/", TestContext.Current.CancellationToken);
        var cookie = string.Join("; ", originalPage.Headers.GetValues("Set-Cookie").Select(value => value.Split(';')[0]));
        var token = await Upload.Token(client);
        using var missing = await client.PostAsync("/api/uploads", new StringContent(""), TestContext.Current.CancellationToken);
        await Upload.AssertError(missing, 400, "refreshPage");
        using var invalid = await Upload.Send(client, [TestFile.Log()], token: "invalid");
        await Upload.AssertError(invalid, 400, "refreshPage");
        await app.DisposeAsync();
        using var restarted = world.Start().Client();
        restarted.DefaultRequestHeaders.Add("Cookie", cookie);
        using var old = await Upload.Send(restarted, [TestFile.Log()], token: token);
        await Upload.AssertError(old, 400, "refreshPage");
        Assert.Empty(Directory.GetDirectories(world.Data));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task File_and_total_size_limits_are_enforced_by_server(bool total)
    {
        await using var world = new TestWorld();
        using var client = world.Start(new()
        {
            ["Storage:MaxFileMiB"] = "0.0625", ["Storage:MaxSubmissionMiB"] = "0.0625"
        }).Client();
        var log = TestFile.Log() with { Bytes = new byte[total ? 40000 : 65537] };
        var settings = TestFile.Settings() with { Bytes = new byte[40000] };
        using var response = await Upload.Send(client, total ? [log, settings] : [log]);
        await Upload.AssertError(response, 413, "tooLarge");
        Assert.Empty(Directory.GetDirectories(world.Data));
    }

    [Fact]
    public async Task Capacity_removes_oldest_submission_and_counts_metadata_and_retains_latest()
    {
        await using var world = new TestWorld();
        var old = world.SeedSubmission(1, new string('a', 40000));
        var newer = world.SeedSubmission(2, new string('b', 40000));
        world.Write($"data/{old}/.read", "");
        using var client = world.Start(new()
        {
            ["Storage:MaxStorageMiB"] = "0.125", ["Storage:MaxFileMiB"] = "0.0625",
            ["Storage:MaxSubmissionMiB"] = "0.0625"
        }).Client();
        var file = TestFile.Log() with { Bytes = new byte[60000] };
        using var response = await Upload.Send(client, [file]);
        await Upload.AssertSaved(response, world, file);
        Assert.False(Directory.Exists(Path.Combine(world.Data, old)));
        Assert.True(Directory.Exists(Path.Combine(world.Data, newer)));
        Assert.True(Directory.GetDirectories(world.Data).SelectMany(Directory.GetFiles).Sum(path => new FileInfo(path).Length) <= 131072);
    }

    [Fact]
    public async Task Storage_failure_and_insufficient_capacity_never_report_success()
    {
        await using var world = new TestWorld();
        using var client = world.Start(new()
        {
            ["Storage:MaxStorageMiB"] = "0.0625", ["Storage:MaxFileMiB"] = "0.0625",
            ["Storage:MaxSubmissionMiB"] = "0.0625"
        }).Client();
        using var full = await Upload.Send(client, [TestFile.Log() with { Bytes = new byte[65536] }]);
        await Upload.AssertError(full, 507, "storageFull"); // The metadata also occupies disk space.
        world.Write("outside-data/secret.txt", "test only");
        File.CreateSymbolicLink(Path.Combine(world.Data, "bad-link"), Path.Combine(world.Root, "outside-data/secret.txt"));
        using var failed = await Upload.Send(client, [TestFile.Log()]);
        await Upload.AssertError(failed, 503, "storageFailed");
        Assert.Empty(Directory.GetDirectories(world.Data));
    }

    [Fact]
    public async Task Startup_cleans_unfinished_uploads_and_three_concurrent_uploads_remain_separate()
    {
        await using var world = new TestWorld();
        var id = world.SeedSubmission();
        world.Write($"data/{id}.uploading/unfinished.log", "partial");
        using var client = world.Start().Client();
        var token = await Upload.Token(client);
        Assert.False(Directory.Exists(Path.Combine(world.Data, id + ".uploading")));
        Assert.True(Directory.Exists(Path.Combine(world.Data, id)));
        var responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(i =>
            Upload.Send(client, [TestFile.Log("parallel " + i)], nickname: "Player " + i, token: token)));
        var ids = new HashSet<string>();
        for (var i = 0; i < responses.Length; i++)
        {
            using var response = responses[i];
            Assert.True(ids.Add(await Upload.AssertSaved(response, world, TestFile.Log("parallel " + i))));
        }
    }
}
