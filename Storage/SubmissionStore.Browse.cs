using System.Text.Json;

namespace SendMeTheFuckingLog.Storage;

public sealed record AuthorFile(string Name, long Size);
public sealed record AuthorSubmission(string Id, string? Nickname, string? GameId, AuthorFile[] Files, string Kind = "logs");
public sealed record AuthorListing(AuthorSubmission[] Submissions, int Page, int TotalPages);

public sealed partial class SubmissionStore
{
    public async Task<AuthorListing> BrowseAsync(int page, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            EnsurePlain(root);
            var directories = Directory.EnumerateDirectories(root)
                .Where(path => IsOwned(path) && !path.EndsWith(".uploading", StringComparison.Ordinal))
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal).ToArray();
            var totalPages = Math.Max(1, (directories.Length + 49) / 50);
            page = Math.Clamp(page, 1, totalPages);
            var submissions = new List<AuthorSubmission>();
            foreach (var directory in directories.Skip((page - 1) * 50).Take(50))
            {
                token.ThrowIfCancellationRequested();
                EnsurePlain(directory);
                var files = new List<AuthorFile>();
                foreach (var file in Directory.EnumerateFiles(directory).OrderBy(Path.GetFileName, StringComparer.Ordinal))
                {
                    EnsurePlain(file);
                    files.Add(new(Path.GetFileName(file), new FileInfo(file).Length));
                }
                SubmissionInfo? information = null;
                var metadata = Path.Combine(directory, "submission.json");
                if (files.Any(file => file.Name == "submission.json" && file.Size <= MetadataLimit))
                {
                    try { information = JsonSerializer.Deserialize<SubmissionInfo>(await File.ReadAllBytesAsync(metadata, token), JsonOptions); }
                    catch (JsonException) { /* Files remain accessible even if their metadata is damaged. */ }
                }
                var id = Path.GetFileName(directory);
                if (information?.SubmissionId != id) information = null;
                submissions.Add(new(id, information?.Nickname, information?.GameId, files.ToArray(), information?.Kind ?? "logs"));
            }
            return new(submissions.ToArray(), page, totalPages);
        }
        finally { gate.Release(); }
    }

    public async Task<FileStream> OpenAuthorFileAsync(string? submission, string? name, CancellationToken token)
    {
        // Validate on both OS families: in particular, never allow Windows ADS or separators.
        if (string.IsNullOrEmpty(submission) || submission.Length > 160 || !DirectoryName.IsMatch(submission)
            || submission.EndsWith(".uploading", StringComparison.Ordinal) || !PlainFileName(submission)
            || !PlainFileName(name)) throw new FileNotFoundException();
        await gate.WaitAsync(token);
        try
        {
            EnsurePlain(root);
            var directory = Child(submission);
            EnsurePlain(directory);
            var path = Path.Combine(directory, name!);
            EnsurePlain(path);
            // Opening under the same gate as cleanup avoids a path-check/delete race.
            // Delete sharing lets retention remove a submission while this stream finishes.
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                16384, FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        finally { gate.Release(); }
    }

    private static bool PlainFileName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 255
        && name is not ("." or "..") && !name.EndsWith('.') && !name.EndsWith(' ')
        && !name.Any(character => char.IsControl(character) || "\\/:*?\"<>|".Contains(character));
}
