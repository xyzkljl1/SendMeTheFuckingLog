using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SendMeTheFuckingLog.Hosting;
using SendMeTheFuckingLog.Models;
using SendMeTheFuckingLog.Storage;

namespace SendMeTheFuckingLog.Pages;

public sealed class FilesModel(AuthorAccess access, SubmissionStore store, GameCatalog catalog,
    ILogger<FilesModel> logger) : PageModel
{
    public bool English { get; private set; }
    public AuthorListing? Listing { get; private set; }
    public string? Error { get; private set; }
    public string? Submission { get; private set; }
    public string? FileName { get; private set; }
    public string? Preview { get; private set; }
    public bool Truncated { get; private set; }
    public bool IsImage { get; private set; }
    public int Number { get; private set; } = 1;
    public string T(string zh, string en) => English ? en : zh;
    public string Link(int number, bool? english = null) => $"/files?number={number}&lang={((english ?? English) ? "en" : "zh")}";
    public string FileLink(string handler, string id, string name, bool? english = null) =>
        $"/files?handler={handler}&submission={Uri.EscapeDataString(id)}&name={Uri.EscapeDataString(name)}&number={Number}&lang={((english ?? English) ? "en" : "zh")}";
    public string LanguageLink(bool english) => FileName is null ? Link(Number, english) : FileLink("View", Submission!, FileName, english);
    public string GameName(string? id)
    {
        var game = catalog.Games.FirstOrDefault(game => game.Id == id);
        return game is null ? id ?? T("未知游戏", "Unknown game") : English ? game.Name.En : game.Name.Zh;
    }
    public static string Size(long bytes) => (bytes / 1024d).ToString("N1", CultureInfo.InvariantCulture) + " KB";

    public override async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'self'; img-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
        // The entry pipeline restores this address only for explicitly trusted proxies.
        // Never read visitor-supplied forwarding headers directly as proof of author access.
        if (!access.Allows(HttpContext.Connection.RemoteIpAddress))
        {
            context.Result = new ContentResult { StatusCode = 403, ContentType = "text/plain; charset=utf-8",
                Content = "无权访问。 / Access denied." };
            return;
        }
        await next();
    }

    public async Task OnGetAsync(string? lang, int number = 1)
    {
        English = lang == "en";
        try
        {
            Listing = await store.BrowseAsync(number, HttpContext.RequestAborted);
            Number = Listing.Page;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(error, "Unable to browse submissions.");
            Response.StatusCode = 503;
            Error = T("暂时无法读取文件，请稍后刷新。", "Can't load the files right now. Please try again shortly.");
        }
    }

    public async Task<IActionResult> OnGetViewAsync(string? submission, string? name, string? lang, int number = 1)
    {
        English = lang == "en";
        Number = Math.Max(1, number);
        try
        {
            await using var stream = await store.OpenAuthorFileAsync(submission, name, HttpContext.RequestAborted);
            Submission = submission; FileName = name;
            if (ScreenshotFormat.ContentType(name!) is not null)
            {
                IsImage = await ScreenshotFormat.MatchesAsync(name!, stream, HttpContext.RequestAborted);
                if (!IsImage) Error = T("图片格式无效。", "Invalid image format.");
                return Page();
            }
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var buffer = new char[65537];
            var count = await reader.ReadBlockAsync(buffer.AsMemory(), HttpContext.RequestAborted);
            Truncated = count > 65536;
            if (buffer.AsSpan(0, count).Contains('\0'))
                Error = T("这个文件无法以文字预览，请下载查看。", "This file can't be shown as text. Download it to view it.");
            else Preview = new string(buffer, 0, Math.Min(count, 65536));
            return Page();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return Missing(); }
    }

    public async Task<IActionResult> OnGetDownloadAsync(string? submission, string? name)
    {
        try
        {
            var stream = await store.OpenAuthorFileAsync(submission, name, HttpContext.RequestAborted);
            return File(stream, "application/octet-stream", name!);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return Missing(); }
    }

    public async Task<IActionResult> OnGetImageAsync(string? submission, string? name)
    {
        var contentType = name is null ? null : ScreenshotFormat.ContentType(name);
        if (contentType is null) return Missing();
        try
        {
            var stream = await store.OpenAuthorFileAsync(submission, name, HttpContext.RequestAborted);
            try
            {
                if (await ScreenshotFormat.MatchesAsync(name!, stream, HttpContext.RequestAborted))
                    return File(stream, contentType);
                await stream.DisposeAsync();
                return Missing();
            }
            catch { await stream.DisposeAsync(); throw; }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return Missing(); }
    }

    private static ContentResult Missing() => new() { StatusCode = 404, ContentType = "text/plain; charset=utf-8",
        Content = "文件不存在或已清理。 / This file is missing or has been removed." };
}
