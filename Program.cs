using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Repositories;
using System.Xml.Linq;
using SendMeTheFuckingLog.Models;
using SendMeTheFuckingLog.Storage;
using SendMeTheFuckingLog.Monitoring;
using SendMeTheFuckingLog.Hosting;

try
{
    var builder = WebApplication.CreateBuilder(args);
    InstanceConfiguration.Apply(builder.Configuration, builder.Environment);
    builder.Services.AddSingleton(new ApplicationPaths(AppContext.BaseDirectory));
    builder.Services.AddSingleton<ReverseProxyHeaders>();
    builder.Services.AddSingleton<HttpsTransport>();
    builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(140));
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.Limits.MaxRequestBodySize = options.ApplicationServices.GetRequiredService<StorageLimits>().RequestBytes;
        options.ApplicationServices.GetRequiredService<HttpsTransport>().ConfigureEndpoints(options);
    });

    builder.Services.AddRazorPages();
    builder.Services.Configure<UploadPageOptions>(builder.Configuration.GetSection("UploadPage"));
    builder.Services.AddSingleton<GameCatalog>();
    builder.Services.AddSingleton<HostedImageUrls>();
    builder.Services.AddSingleton(services =>
    {
        var limits = services.GetRequiredService<IConfiguration>().GetSection("Storage").Get<StorageLimits>() ?? new();
        limits.Validate();
        return limits;
    });
    builder.Services.AddSingleton<SubmissionStore>();
    builder.Services.AddSingleton(services =>
    {
        var limits = services.GetRequiredService<IConfiguration>().GetSection("GlobalRateLimit").Get<RequestLimits>() ?? new();
        limits.Validate();
        return limits;
    });
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
    var proxy = app.Services.GetRequiredService<ReverseProxyHeaders>();
    var https = app.Services.GetRequiredService<HttpsTransport>();
    app.Services.GetRequiredService<SubmissionStore>().Initialize();
    var requestCounter = app.Services.GetRequiredService<RequestCounter>();
    requestCounter.Initialize();
    app.Use(async (context, next) =>
    {
        // Docker liveness probes are local only and must not consume public quotas.
        if (proxy.IsLocalHealthRequest(context))
        {
            context.Response.StatusCode = 200;
            await context.Response.WriteAsync("OK");
            return;
        }
        if (proxy.Enabled)
        {
            var status = proxy.ValidateRequest(context);
            if (status != StatusCodes.Status200OK)
            {
                context.Response.StatusCode = status;
                context.Response.Headers.CacheControl = "no-store";
                await context.Response.WriteAsync("代理请求无效。 / Invalid proxy request.");
                return;
            }
        }
        await next(context);
    });
    if (proxy.Enabled) app.UseForwardedHeaders(proxy.Options);
    app.Use(async (context, next) =>
    {
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
catch (HostAbortedException) { throw; }
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    Environment.ExitCode = 1;
}

public partial class Program { }

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
