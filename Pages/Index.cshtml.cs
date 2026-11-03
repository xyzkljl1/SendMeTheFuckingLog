using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using SendMeTheFuckingLog.Models;
using SendMeTheFuckingLog.Storage;

namespace SendMeTheFuckingLog.Pages;

public sealed class IndexModel(GameCatalog catalog, IOptions<UploadPageOptions> options, StorageLimits limits,
    HostedImageUrls images) : PageModel
{
    public string ImageOrigin => images.Origin;
    public string WindowsKeyImage => images.Resolve("icons/windows-key.svg");
    public IEnumerable<string> PreloadedImages => catalog.Games.Select(game => game.Image)
        .OfType<string>().Where(url => !new Uri(url).AbsolutePath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
        .Distinct(StringComparer.Ordinal).Take(2);
    public object PageConfiguration => new
    {
        authorName = options.Value.AuthorName, games = catalog.Games,
        recognition = catalog.Recognition,
        automaticGuideImages = catalog.AutomaticGuideImages,
        preloadedImages = PreloadedImages,
        maxFileBytes = limits.FileBytes, maxSubmissionBytes = limits.SubmissionBytes,
        maxScreenshotFiles = SubmissionStore.MaxFiles
    };
    public void OnGet() { }
}
