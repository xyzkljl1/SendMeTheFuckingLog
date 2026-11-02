using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.DataProtection;
using SendMeTheFuckingLog.Hosting;
using SendMeTheFuckingLog.Models;
using SendMeTheFuckingLog.Pages;
using SendMeTheFuckingLog.Storage;

try
{
    if (args.Length != 1) throw new ArgumentException("Pass the project root; build the test output under project obj.");
    var project = Path.GetFullPath(args[0]);
    var allowed = Path.Combine(project, "obj") + Path.DirectorySeparatorChar;
    if (!Path.GetFullPath(AppContext.BaseDirectory).StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Tests must run from project obj.");
    var data = Path.Combine(AppContext.BaseDirectory, "data");
    if (Directory.Exists(data)) throw new InvalidOperationException("Use a fresh test output directory.");

    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = project });
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Logging.ClearProviders();
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
        ["AuthorAccess:AllowedClients:0"] = "first.example", ["AuthorAccess:AllowedClients:1"] = "second.example"
    });
    var resolver = new FakeResolver();
    var clock = new FakeClock();
    builder.Services.AddSingleton<IDomainAddressResolver>(resolver);
    builder.Services.AddSingleton<TimeProvider>(clock);
    builder.Services.AddSingleton<AuthorAccess>();
    builder.Services.Configure<UploadPageOptions>(builder.Configuration.GetSection("UploadPage"));
    builder.Services.AddSingleton<HostedImageUrls>();
    builder.Services.AddSingleton<GameCatalog>();
    builder.Services.AddSingleton(new StorageLimits());
    builder.Services.AddSingleton<SubmissionStore>();
    builder.Services.AddRazorPages().AddApplicationPart(typeof(FilesModel).Assembly);
    builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
    await using var app = builder.Build();
    var store = app.Services.GetRequiredService<SubmissionStore>();
    store.Initialize();
    var access = app.Services.GetRequiredService<AuthorAccess>();
    async Task Refresh() => await (Task)typeof(AuthorAccess).GetMethod("RefreshAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
        .Invoke(access, [CancellationToken.None])!;
    void Check(bool condition, string label) { if (!condition) throw new Exception(label); }
    var loopback = IPAddress.Loopback;
    Check(!access.Allows(loopback), "Deny before initial DNS resolution");
    resolver.Addresses["first.example"] = [loopback, IPAddress.Parse("2001:db8::1")];
    resolver.Addresses["second.example"] = [IPAddress.Parse("192.0.2.10")];
    await Refresh();
    Check(access.Allows(IPAddress.Parse("::ffff:127.0.0.1")), "Mapped IPv4");
    Check(access.Allows(IPAddress.Parse("2001:db8::1")), "IPv6");
    Check(!access.Allows(null) && !access.Allows(IPAddress.Parse("192.0.2.99")), "Unknown client");
    resolver.Addresses["first.example"] = [IPAddress.Parse("192.0.2.11")];
    await Refresh();
    Check(!access.Allows(loopback) && access.Allows(IPAddress.Parse("192.0.2.11")), "Dynamic IP replacement");
    resolver.Addresses.Remove("first.example");
    await Refresh();
    Check(!access.Allows(IPAddress.Parse("192.0.2.11")) && access.Allows(IPAddress.Parse("192.0.2.10")), "Per-domain DNS failure");
    clock.Now += TimeSpan.FromMinutes(12);
    Check(!access.Allows(IPAddress.Parse("192.0.2.10")), "Expired snapshot");

    const string id = "2026-11-03_120000_phantom-blade-zero_0123456789abcdef";
    var folder = Path.Combine(data, id);
    Directory.CreateDirectory(folder);
    const string content = "<script>alert('unsafe')</script>\nhello log";
    File.WriteAllText(Path.Combine(folder, "UE4SS.log"), content);
    File.WriteAllText(Path.Combine(folder, "large.log"), new string('a', 70000));
    File.WriteAllText(Path.Combine(folder, "submission.json"), JsonSerializer.Serialize(
        new SubmissionInfo(1, id, "phantom-blade-zero", "<Author & User>", DateTimeOffset.UtcNow, []), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    Directory.CreateDirectory(Path.Combine(data, id + ".uploading"));
    File.WriteAllText(Path.Combine(data, id + ".uploading", "secret.log"), "incomplete");
    app.MapRazorPages();
    await app.StartAsync();
    try
    {
        var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(url) };
        async Task<HttpResponseMessage> Get(string path) => await client.GetAsync(path);
        var fileQuery = $"submission={id}&name=UE4SS.log";
        foreach (var path in new[] { "/files", "/Files/", $"/files?handler=View&{fileQuery}", $"/files?handler=Download&{fileQuery}" })
        {
            using var denied = await Get(path);
            Check(denied.StatusCode == HttpStatusCode.Forbidden, "Denied handler: " + path);
            Check(denied.Headers.CacheControl?.NoStore == true, "Denied caching");
        }
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "192.0.2.10");
        client.DefaultRequestHeaders.Add("CF-Connecting-IP", "192.0.2.10");
        using (var spoof = await Get("/files")) Check(spoof.StatusCode == HttpStatusCode.Forbidden, "Spoofed proxy headers");
        resolver.Addresses["first.example"] = [loopback]; await Refresh();
        foreach (var language in new[] { "zh", "en" })
        {
            using var list = await Get("/files?lang=" + language);
            var html = await list.Content.ReadAsStringAsync();
            Check(list.StatusCode == HttpStatusCode.OK && html.Contains("UE4SS.log") && html.Contains("&lt;Author &amp; User&gt;"), "Listing / encoded nickname");
            Check(!html.Contains("secret.log") && !html.Contains(".uploading"), "No unfinished uploads");
            Check(html.Contains("html lang=\"" + (language == "zh" ? "zh-CN" : "en")), "Bilingual page");
        }
        using (var preview = await Get($"/files?handler=View&{fileQuery}"))
        {
            var html = await preview.Content.ReadAsStringAsync();
            Check(preview.StatusCode == HttpStatusCode.OK && html.Contains("&lt;script&gt;") && !html.Contains("<script>"), "Escaped preview");
            Check(preview.Headers.Contains("Content-Security-Policy"), "Preview CSP");
        }
        using (var large = await Get($"/files?handler=View&submission={id}&name=large.log&lang=en"))
            Check((await large.Content.ReadAsStringAsync()).Contains("first 64K"), "Bounded preview");
        using (var download = await Get($"/files?handler=Download&{fileQuery}"))
        {
            Check(await download.Content.ReadAsStringAsync() == content, "Download bytes");
            Check(download.Content.Headers.ContentDisposition?.DispositionType == "attachment", "Download disposition");
            Check(download.Headers.CacheControl?.NoStore == true, "Download caching");
        }
        foreach (var query in new[] { "submission=..&name=appsettings.json", $"submission={id}&name=../submission.json",
            $"submission={id}&name=..%5Csubmission.json", $"submission={id}&name=UE4SS.log:stream",
            $"submission={id}.uploading&name=secret.log", $"submission={id}&name=missing.log" })
        {
            using var invalid = await Get("/files?handler=Download&" + query);
            Check(invalid.StatusCode == HttpStatusCode.NotFound, "Reject invalid path: " + query);
        }
        var listing = await store.BrowseAsync(int.MaxValue, CancellationToken.None);
        Check(listing.Page == 1 && listing.Submissions.Length == 1, "Pagination clamps");
        for (var index = 1; index <= 50; index++)
            Directory.CreateDirectory(Path.Combine(data, $"2026-11-03_130000_phantom-blade-zero_{index:x16}"));
        var firstPage = await store.BrowseAsync(1, CancellationToken.None);
        var lastPage = await store.BrowseAsync(int.MaxValue, CancellationToken.None);
        Check(firstPage.Submissions.Length == 50 && lastPage.Page == 2 && lastPage.Submissions.Single().Id == id,
            "All submissions reachable through pagination, newest first");
        var metadataPath = Path.Combine(folder, "submission.json");
        File.WriteAllText(metadataPath, "broken json");
        Check((await store.BrowseAsync(2, CancellationToken.None)).Submissions[0].Files.Length == 3, "Damaged metadata keeps files accessible");
        File.Delete(Path.Combine(folder, "UE4SS.log"));
        using (var removed = await Get($"/files?handler=Download&{fileQuery}")) Check(removed.StatusCode == HttpStatusCode.NotFound, "Retention removed file");
        resolver.Addresses.Clear(); await Refresh();
        using (var revoked = await Get("/files")) Check(revoked.StatusCode == HttpStatusCode.Forbidden, "Revoke when DNS fails");
        builder.Configuration["AuthorAccess:AllowedClients:0"] = " 127.0.0.1 ";
        builder.Configuration["AuthorAccess:AllowedClients:1"] = "2001:db8::2";
        await Refresh();
        Check(access.Allows(IPAddress.Parse("::ffff:127.0.0.1")) && access.Allows(IPAddress.Parse("2001:db8::2")), "Literal IPv4 and IPv6");
        using (var literal = await Get("/files")) Check(literal.StatusCode == HttpStatusCode.OK, "Literal IP HTTP access");
        builder.Configuration["AuthorAccess:AllowedClients:1"] = "failed.example";
        await Refresh();
        Check(access.Allows(loopback), "Literal IP survives domain DNS failure");
        builder.Configuration["AuthorAccess:AllowedClients:0"] = "*";
        await Refresh();
        Check(access.Allows(IPAddress.Parse("203.0.113.100")) && access.Allows(IPAddress.Parse("2001:db8::99")) && !access.Allows(null), "Explicit any-IP entry");
        using (var any = await Get($"/files?handler=Download&submission={id}&name=large.log"))
            Check(any.StatusCode == HttpStatusCode.OK, "Wildcard also permits downloads");
        builder.Configuration["AuthorAccess:AllowedClients:0"] = "192.0.2.40";
        await Refresh();
        using (var restricted = await Get("/files")) Check(restricted.StatusCode == HttpStatusCode.Forbidden, "Removing wildcard revokes access");
        builder.Configuration["AuthorAccess:AllowedClients:0"] = "*";
        builder.Configuration["AuthorAccess:AllowedClients:1"] = "https://invalid.example/path";
        await Refresh();
        Check(!access.Allows(loopback), "Invalid configuration fails closed even with wildcard");
        Console.WriteLine("PASS: DNS changes/failures/expiry, IPv4/IPv6, all HTTP handlers, spoofed headers, bilingual listing, safe preview, download, traversal, retention and metadata failure.");
    }
    finally { await app.StopAsync(); }
}
catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }

sealed class FakeResolver : IDomainAddressResolver
{
    public Dictionary<string, IPAddress[]> Addresses { get; } = new();
    public Task<IPAddress[]> ResolveAsync(string domain, CancellationToken token) => Addresses.TryGetValue(domain, out var result)
        ? Task.FromResult(result) : Task.FromException<IPAddress[]>(new System.Net.Sockets.SocketException());
}
sealed class FakeClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => Now;
}
