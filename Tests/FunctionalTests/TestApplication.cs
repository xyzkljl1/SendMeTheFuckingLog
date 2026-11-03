using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace FunctionalTests;

// Each test owns a directory and boots the actual Program entry point.
// No test is allowed to use a developer's uploads or run outside a GitHub runner.
public sealed class TestWorld : IAsyncDisposable
{
    public static string Project => Environment.GetEnvironmentVariable("GITHUB_WORKSPACE")
        ?? throw new InvalidOperationException("Run this suite in GitHub Actions.");
    public string Root { get; }
    public string Data => Path.Combine(Root, "data");
    public TestClock Clock { get; } = new();
    public FakeResolver Resolver { get; } = new();
    private readonly List<TestApplication> applications = [];

    public TestWorld()
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true"
            || !File.Exists(Path.Combine(Project, "SendMeTheFuckingLog.csproj")))
            throw new InvalidOperationException("Tests require a GitHub runner checkout.");
        Root = Path.Combine(Project, "obj", "functional-data", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public TestApplication Start(Dictionary<string, string?>? settings = null, bool browser = false)
    {
        var app = new TestApplication(this, settings);
        if (browser) app.UseKestrel(0);
        applications.Add(app);
        return app;
    }

    public string Write(string relative, string content)
    {
        var path = Path.GetFullPath(Path.Combine(Root, relative));
        if (!path.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Test file escaped its directory.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    public string SeedSubmission(int index = 1, string content = "private log", string nickname = "Test player")
    {
        var id = $"2026-11-03_120000_phantom-blade-zero_{index:x16}";
        Write($"data/{id}/UE4SS.log", content);
        Write($"data/{id}/submission.json", JsonSerializer.Serialize(
            new SubmissionInfo(1, id, "phantom-blade-zero", nickname, DateTimeOffset.UtcNow, []), Upload.Json));
        return id;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var app in applications) await app.DisposeAsync();
        var allowed = Path.GetFullPath(Path.Combine(Project, "obj", "functional-data")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(Root).StartsWith(allowed, StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid cleanup directory.");
        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }
}

public sealed class TestApplication(TestWorld world, Dictionary<string, string?>? settings) : WebApplicationFactory<Program>
{
    public string ContentRoot { get; set; } = TestWorld.Project;
    public IPAddress? ClientAddress { get; set; } = IPAddress.Loopback;
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(ContentRoot);
        builder.UseWebRoot(Path.Combine(TestWorld.Project, "wwwroot"));
        builder.UseEnvironment("Testing");
        var values = new Dictionary<string, string?>
        {
            ["Tls:Enabled"] = "false",
            ["UploadPage:AuthorName"] = "Test author",
            ["ImageHosting:BaseUrl"] = "https://assets.example.test",
            ["Storage:MaxStorageMiB"] = "1", ["Storage:MaxFileMiB"] = "0.125",
            ["Storage:MaxSubmissionMiB"] = "0.25",
            ["GlobalRateLimit:MaxRequestsPerHour"] = "100000",
            ["GlobalRateLimit:MaxRequestsPerDay"] = "200000",
            ["GlobalRateLimit:MaxRequestsPerWeek"] = "300000",
            ["AuthorAccess:AllowedClients:0"] = "127.0.0.1", ["AuthorAccess:AllowedClients:1"] = "::1"
        };
        foreach (var pair in settings ?? []) values[pair.Key] = pair.Value;
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(values));
        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        builder.ConfigureServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton(new ApplicationPaths(world.Root)));
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(world.Clock));
            services.Replace(ServiceDescriptor.Singleton<IDomainAddressResolver>(world.Resolver));
            services.AddSingleton<IStartupFilter>(new ClientAddressFilter(this));
        });
    }

    public HttpClient Client()
    {
        // Start first: UseKestrel(0) resolves its real port into ClientOptions during startup.
        StartServer();
        return CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false, BaseAddress = ClientOptions.BaseAddress
        });
    }
    public Task RefreshAccess() => Services.GetRequiredService<AuthorAccess>().RefreshAsync(TestContext.Current.CancellationToken);

    private sealed class ClientAddressFilter(TestApplication app) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => builder =>
        {
            builder.Use((context, continuation) =>
            {
                // TestServer has no network peer. Tests set the peer here, never through a trusted header.
                context.Connection.RemoteIpAddress = app.ClientAddress;
                return continuation(context);
            });
            next(builder);
        };
    }
}

public sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(TimeSpan duration) => Now += duration;
}

public sealed class FakeResolver : IDomainAddressResolver
{
    public Dictionary<string, IPAddress[]> Addresses { get; } = [];
    public Task<IPAddress[]> ResolveAsync(string domain, CancellationToken token) => Addresses.TryGetValue(domain, out var result)
        ? Task.FromResult(result) : Task.FromException<IPAddress[]>(new SocketException());
}

public sealed record TestFile(string ItemId, string FileName, byte[] Bytes)
{
    public static TestFile Log(string text = "test log\n你好") => new("ue4ss-log", "UE4SS.log", Encoding.UTF8.GetBytes(text));
    public static TestFile Settings() => new("ue4ss-settings", "UE4SS-settings.ini", Encoding.UTF8.GetBytes("[General]\nEnabled=true"));
}

public static class Upload
{
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);
    public static async Task<string> Token(HttpClient client)
    {
        using var page = await client.GetAsync("/", TestContext.Current.CancellationToken);
        page.EnsureSuccessStatusCode();
        var match = Regex.Match(await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), "name=\"request-verification-token\" content=\"([^\"]+)\"");
        Assert.True(match.Success, "The homepage must supply a CSRF token.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    public static async Task<HttpResponseMessage> Send(HttpClient client, TestFile[] files,
        string mode = "manual", string nickname = "Test player", string game = "phantom-blade-zero",
        bool wrongHash = false, bool truncate = false, string? token = null)
    {
        token ??= await Token(client);
        using var body = new MultipartFormDataContent();
        body.Add(new StringContent(JsonSerializer.Serialize(new
        {
            gameId = game, nickname, selectionMode = mode,
            files = files.Select(file => new
            {
                itemId = file.ItemId, fileName = file.FileName, size = file.Bytes.Length,
                sha256 = wrongHash ? new string('0', 64) : Convert.ToHexStringLower(SHA256.HashData(file.Bytes))
            })
        }, Json)), "metadata");
        for (var i = 0; i < files.Length; i++)
            body.Add(new ByteArrayContent(truncate ? files[i].Bytes[..^1] : files[i].Bytes), $"file{i}", files[i].FileName);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/uploads") { Content = body };
        request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public static async Task AssertError(HttpResponseMessage response, int status, string code)
    {
        Assert.Equal(status, (int)response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(code, json.RootElement.GetProperty("error").GetString());
    }

    public static async Task<string> AssertSaved(HttpResponseMessage response, TestWorld world, params TestFile[] expected)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var receipt = JsonSerializer.Deserialize<UploadReceipt>(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), Json)!;
        Assert.True(receipt.Saved);
        Assert.Equal(expected.Length, receipt.FileCount);
        var directory = Path.Combine(world.Data, receipt.SubmissionId);
        Assert.True(Directory.Exists(directory), "Success must mean that a final directory already exists.");
        using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "submission.json"), TestContext.Current.CancellationToken));
        foreach (var file in expected)
        {
            var entry = metadata.RootElement.GetProperty("files").EnumerateArray()
                .Single(item => item.GetProperty("originalName").GetString() == file.FileName);
            Assert.Equal(file.Bytes, await File.ReadAllBytesAsync(Path.Combine(directory, entry.GetProperty("storedName").GetString()!), TestContext.Current.CancellationToken));
        }
        Assert.Empty(Directory.GetDirectories(world.Data, "*.uploading"));
        return receipt.SubmissionId;
    }
}
