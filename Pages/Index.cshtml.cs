using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using SendMeTheFuckingLog.Models;
using SendMeTheFuckingLog.Storage;

namespace SendMeTheFuckingLog.Pages;

public sealed class IndexModel(GameCatalog catalog, IOptions<UploadPageOptions> options, StorageLimits limits) : PageModel
{
    public object PageConfiguration => new
    {
        authorName = options.Value.AuthorName, games = catalog.Games,
        maxFileBytes = limits.FileBytes, maxSubmissionBytes = limits.SubmissionBytes
    };
    public void OnGet() { }
}
