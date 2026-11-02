using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Repositories;
using System.Xml.Linq;
using SendMeTheFuckingLog.Models;
using SendMeTheFuckingLog.Storage;
using SendMeTheFuckingLog.Monitoring;
using System.Net;
using SendMeTheFuckingLog.Hosting;

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
        .AddJsonFile("appsettings.Runtime.json", optional: true, reloadOnChange: true)
        .AddEnvironmentVariables().AddCommandLine(args);
    using var https = new HttpsTransport(builder);
    builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(140));
    var storageLimits = builder.Configuration.GetSection("Storage").Get<StorageLimits>() ?? new StorageLimits();
    storageLimits.Validate();
    var requestLimits = builder.Configuration.GetSection("GlobalRateLimit").Get<RequestLimits>() ?? new RequestLimits();
    requestLimits.Validate();
    builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = storageLimits.RequestBytes);

    builder.Services.AddRazorPages();
    builder.Services.Configure<UploadPageOptions>(builder.Configuration.GetSection("UploadPage"));
    builder.Services.AddSingleton<GameCatalog>();
    builder.Services.AddSingleton<HostedImageUrls>();
    builder.Services.AddSingleton(storageLimits);
    builder.Services.AddSingleton<SubmissionStore>();
    builder.Services.AddSingleton(requestLimits);
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<IDomainAddressResolver, DomainAddressResolver>();
    builder.Services.AddSingleton<AuthorAccess>();
    builder.Services.AddHostedService(services => services.GetRequiredService<AuthorAccess>());
    builder.Services.AddSingleton<RequestCounter>();
    builder.Services.AddSingleton<TrafficStatusReader>();
    builder.Services.AddHostedService(services => services.GetRequiredService<RequestCounter>());
    builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
    // Keep this starter app from persisting keys outside its directory.
    builder.Services.AddDataProtection()
        .AddKeyManagementOptions(options => options.XmlRepository = new InMemoryKeyRepository());

    var app = builder.Build();
    app.Services.GetRequiredService<SubmissionStore>().Initialize();
    var requestCounter = app.Services.GetRequiredService<RequestCounter>();
    requestCounter.Initialize();
    app.Use(async (context, next) =>
    {
        // Docker liveness probes are local only and must not consume public quotas.
        if (context.Request.Path == "/healthz" && HttpMethods.IsGet(context.Request.Method)
            && context.Connection.RemoteIpAddress is { } address && IPAddress.IsLoopback(address))
        {
            context.Response.StatusCode = 200;
            await context.Response.WriteAsync("OK");
            return;
        }
        // ACME must remain reachable even when the public request quota is exhausted.
        if (await https.HandleChallengeAsync(context)) return;
        var status = requestCounter.Admit();
        var statusPath = context.Request.Path.Equals("/status", StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.Equals("/status/", StringComparison.OrdinalIgnoreCase);
        var statusPage = (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
            && statusPath;
        if (statusPage)
        {
            context.Response.StatusCode = status;
            if (!await https.HandleTransportAsync(context)) await next(context);
            return;
        }
        if (status == 200)
        {
            if (https.IsChallengePath(context.Request.Path))
            {
                context.Response.StatusCode = 404;
                return;
            }
            if (!await https.HandleTransportAsync(context)) await next(context);
            return;
        }
        context.Response.StatusCode = status;
        context.Response.Headers.CacheControl = "no-store";
        if (context.Request.Path.StartsWithSegments("/api"))
            await context.Response.WriteAsJsonAsync(new { error = "siteBusy" });
        else
        {
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync("网站暂时繁忙，请稍后重试。 / The website is busy. Please try again later.");
        }
    });
    app.UseStaticFiles();
    app.MapRazorPages();
    app.MapPost("/api/uploads", UploadEndpoint.HandleAsync);
    app.Run();
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    Environment.ExitCode = 1;
}

sealed class InMemoryKeyRepository : IXmlRepository
{
    private readonly List<XElement> elements = [];

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        lock (elements)
            return elements.Select(element => new XElement(element)).ToArray();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        lock (elements)
            elements.Add(new XElement(element));
    }
}
