using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SendMeTheFuckingLog.Monitoring;
using SendMeTheFuckingLog.Storage;

namespace SendMeTheFuckingLog.Pages;

public sealed class StatusModel(RequestCounter counter, SubmissionStore store, TrafficStatusReader traffic,
    ILogger<StatusModel> logger) : PageModel
{
    public bool English { get; private set; }
    public RequestStatus Requests { get; private set; } = null!;
    public StorageStatus? Storage { get; private set; }
    public TrafficStatus? Traffic { get; private set; }
    public bool TrafficReadFailed { get; private set; }
    public int Number { get; private set; }
    public int TotalPages => Math.Max(1, ((Storage?.Folders.Length ?? 0) + 49) / 50);
    public IEnumerable<SubmissionFolder> Folders => Storage?.Folders.Skip((Number - 1) * 50).Take(50) ?? [];
    public DateTimeOffset CheckedAt { get; } = DateTimeOffset.UtcNow;
    public string T(string zh, string en) => English ? en : zh;
    public string Link(int number, bool? english = null) => $"/Status?lang={((english ?? English) ? "en" : "zh")}&number={number}";
    public static string MiB(long bytes) => (bytes / 1048576d).ToString("N2", CultureInfo.InvariantCulture) + " MiB";
    public static string GB(long bytes) => (bytes / 1_000_000_000d).ToString("N3", CultureInfo.InvariantCulture) + " GB";

    public async Task OnGetAsync(string? lang, int number = 1)
    {
        English = lang == "en";
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        Requests = counter.GetStatus();
        try { Storage = await store.GetStatusAsync(HttpContext.RequestAborted); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { logger.LogWarning(error, "Unable to read storage status."); }
        try { Traffic = traffic.Read(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            TrafficReadFailed = true;
            logger.LogWarning(error, "Unable to read traffic status.");
        }
        Number = Math.Clamp(number, 1, TotalPages);
    }
}
