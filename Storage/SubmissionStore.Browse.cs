using System.Text.Json;

namespace SendMeTheFuckingLog.Storage;

public sealed record AuthorFile(string Name, long Size);
public sealed record AuthorSubmission(string Id, string? Nickname, string? GameId, AuthorFile[] Files,
    string Kind = "logs", bool IsRead = false);
public sealed record AuthorListing(AuthorSubmission[] Submissions, int Page, int TotalPages);

public sealed partial class SubmissionStore
{
    private const string ReadMarker = ".read";

    public async Task<AuthorListing> BrowseAsync(int page, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            EnsurePlain(root);
            var directories = Directory.EnumerateDirectories(root)
                .Where(path => IsOwned(path) && !path.EndsWith(".uploading", StringComparison.Ordinal))
                .Select(path =>
                {
                    token.ThrowIfCancellationRequested();
                    EnsurePlain(path);
                    return (Path: path, IsRead: HasReadMarker(path));
                })
                .OrderBy(entry => entry.IsRead)
                .ThenByDescending(entry => Path.GetFileName(entry.Path), StringComparer.Ordinal).ToArray();
            var totalPages = Math.Max(1, (directories.Length + 49) / 50);
            page = Math.Clamp(page, 1, totalPages);
            var submissions = new List<AuthorSubmission>();
            foreach (var entry in directories.Skip((page - 1) * 50).Take(50))
            {
                token.ThrowIfCancellationRequested();
                var directory = entry.Path;
                var files = new List<AuthorFile>();
                foreach (var file in Directory.EnumerateFiles(directory).OrderBy(Path.GetFileName, StringComparer.Ordinal))
                {
                    EnsurePlain(file);
                    if (IsReadMarker(Path.GetFileName(file))) continue;
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
                submissions.Add(new(id, information?.Nickname, information?.GameId, files.ToArray(),
                    information?.Kind ?? "logs", entry.IsRead));
            }
            return new(submissions.ToArray(), page, totalPages);
        }
        finally { gate.Release(); }
    }

    public async Task SetReadAsync(string? submission, bool isRead, CancellationToken token)
    {
        ValidateSubmissionName(submission);
        await gate.WaitAsync(token);
        try
        {
            var directory = Child(submission!);
            EnsurePlain(directory);
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException();
            var wasRead = HasReadMarker(directory);
            var marker = Path.Combine(directory, ReadMarker);
            token.ThrowIfCancellationRequested();
            // Finish the durable write even if the client disconnects after the mutation.
            if (isRead)
            {
                using var stream = new FileStream(marker, wasRead ? FileMode.Open : FileMode.CreateNew,
                    FileAccess.Write, FileShare.None);
                DiskPersistence.FlushFile(stream);
            }
            else if (wasRead) File.Delete(marker);
            DiskPersistence.FlushDirectory(directory);
        }
        finally { gate.Release(); }
    }

    private static bool HasReadMarker(string directory)
    {
        var marker = Path.Combine(directory, ReadMarker);
        FileAttributes attributes;
        try { attributes = File.GetAttributes(marker); }
        catch (FileNotFoundException) { return false; }
        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0
            || new FileInfo(marker).Length != 0)
            throw new IOException("Invalid submission read marker.");
        return true;
    }

    private static bool IsReadMarker(string? name) => string.Equals(name, ReadMarker, StringComparison.OrdinalIgnoreCase);

    private static void ValidateSubmissionName(string? submission)
    {
        if (string.IsNullOrEmpty(submission) || submission.Length > 160 || !DirectoryName.IsMatch(submission)
            || submission.EndsWith(".uploading", StringComparison.Ordinal) || !PlainFileName(submission))
            throw new FileNotFoundException();
    }

    public async Task<FileStream> OpenAuthorFileAsync(string? submission, string? name, CancellationToken token)
    {
        // Validate on both OS families: in particular, never allow Windows ADS or separators.
        ValidateSubmissionName(submission);
        if (!PlainFileName(name) || IsReadMarker(name)) throw new FileNotFoundException();
        await gate.WaitAsync(token);
        try
        {
            EnsurePlain(root);
            var directory = Child(submission!);
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
