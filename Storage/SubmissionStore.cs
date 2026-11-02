using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using SendMeTheFuckingLog.Models;

namespace SendMeTheFuckingLog.Storage;

public sealed class SubmissionStore : IDisposable
{
    private const int MetadataLimit = 32 * 1024;
    private const int MaxFiles = 32;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly Regex DirectoryName = new(
        @"^\d{4}-\d{2}-\d{2}_\d{6}_[a-z0-9]+(?:-[a-z0-9]+)*_[a-f0-9]{16}(?:\.uploading)?$",
        RegexOptions.CultureInvariant);
    private readonly StorageLimits limits;
    private readonly GameCatalog catalog;
    private readonly ILogger<SubmissionStore> logger;
    private readonly string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "data"));
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly SemaphoreSlim uploadSlots = new(3, 3);
    private readonly Dictionary<string, long> reservations = new(StringComparer.Ordinal);
    private FileStream? processLock;
    private long usedBytes;
    private StorageStatus? statusCache;
    private DateTimeOffset statusCachedAt;

    public async Task<StorageStatus> GetStatusAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            if (statusCache is not null && DateTimeOffset.UtcNow - statusCachedAt < TimeSpan.FromSeconds(15))
                return statusCache;
            EnsurePlain(root);
            var folders = new List<SubmissionFolder>();
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                token.ThrowIfCancellationRequested();
                if (!IsOwned(directory) || directory.EndsWith(".uploading", StringComparison.Ordinal)) continue;
                EnsurePlain(directory);
                var count = 0;
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    EnsurePlain(file);
                    if (Path.GetFileName(file) != "submission.json") count++;
                }
                folders.Add(new SubmissionFolder(Path.GetFileName(directory), count));
            }
            statusCache = new StorageStatus(usedBytes, reservations.Values.Sum(), limits.StorageBytes,
                folders.OrderByDescending(folder => folder.Name, StringComparer.Ordinal).ToArray());
            statusCachedAt = DateTimeOffset.UtcNow;
            return statusCache;
        }
        finally { gate.Release(); }
    }

    public SubmissionStore(StorageLimits limits, GameCatalog catalog, ILogger<SubmissionStore> logger)
    {
        this.limits = limits;
        this.catalog = catalog;
        this.logger = logger;
    }

    public void Initialize()
    {
        Directory.CreateDirectory(root);
        EnsurePlain(root);
        var lockPath = Path.Combine(root, ".writer.lock");
        if (File.Exists(lockPath)) EnsurePlain(lockPath);
        // One writer process per data directory, including during startup cleanup.
        processLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(root))
                if (IsOwned(directory) && directory.EndsWith(".uploading", StringComparison.Ordinal))
                    TryDelete(directory);
            Audit();
            MakeRoom(0);
        }
        catch { processLock.Dispose(); processLock = null; throw; }
    }

    public async Task<UploadReceipt> ReceiveAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!await uploadSlots.WaitAsync(0, cancellationToken)) throw new UploadFailure("busy", 429);
        string? temporaryPath = null;
        string? finalPath = null;
        bool published = false;
        long reservedBytes = 0;
        try
        {
            if (request.ContentLength > limits.RequestBytes) throw new UploadFailure("tooLarge", 413);
            if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var type)
                || !string.Equals(type.MediaType.Value, "multipart/form-data", StringComparison.OrdinalIgnoreCase))
                throw new UploadFailure("invalidSubmission");
            var boundary = HeaderUtilities.RemoveQuotes(type.Boundary).Value;
            if (string.IsNullOrWhiteSpace(boundary) || boundary.Length > 128) throw new UploadFailure("invalidSubmission");
            var reader = new MultipartReader(boundary, request.Body)
            {
                HeadersCountLimit = 8,
                HeadersLengthLimit = 4096,
                BodyLengthLimit = Math.Max(MetadataLimit, limits.FileBytes)
            };
            var first = await reader.ReadNextSectionAsync(cancellationToken);
            if (first is null || GetDisposition(first).Name != "metadata" || GetDisposition(first).FileName is not null)
                throw new UploadFailure("invalidSubmission");
            var description = JsonSerializer.Deserialize<UploadDescription>(
                await ReadMetadataAsync(first.Body, cancellationToken), JsonOptions)
                ?? throw new UploadFailure("invalidSubmission");
            var game = Validate(description);
            var receivedAt = DateTimeOffset.UtcNow;
            var id = $"{receivedAt.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture)}_{game.Id}_{Guid.NewGuid():N}";
            id = id[..^16]; // Keep a random 64-bit suffix; collisions are also checked below.
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "submission.json", ".writer.lock" };
            var files = description.Files.Select(file => new StoredFile(file.ItemId, file.FileName,
                UniqueName(file.FileName, names), file.Size, file.Sha256.ToLowerInvariant())).ToArray();
            var information = new SubmissionInfo(1, id, game.Id, description.Nickname.Trim(), receivedAt, files);
            var metadata = JsonSerializer.SerializeToUtf8Bytes(information, JsonOptions);
            reservedBytes = checked(description.Files.Sum(file => file.Size) + metadata.Length);
            if (reservedBytes > limits.StorageBytes) throw new UploadFailure("storageFull", 507);

            await gate.WaitAsync(cancellationToken);
            try
            {
                Audit(); // Only file lengths, never log contents; also catches externally changed files.
                MakeRoom(reservedBytes);
                finalPath = Child(id);
                var candidate = Child(id + ".uploading");
                if (Directory.Exists(finalPath) || File.Exists(finalPath) || Directory.Exists(candidate) || File.Exists(candidate))
                    throw new UploadFailure("storageFailed", 503);
                Directory.CreateDirectory(candidate);
                temporaryPath = candidate;
                reservations.Add(candidate, reservedBytes);
            }
            finally { gate.Release(); }

            // Metadata lives inside the provisional directory and becomes visible only with the files.
            await using (var metadataStream = NewFile(Path.Combine(temporaryPath, "submission.json")))
            {
                await metadataStream.WriteAsync(metadata, cancellationToken);
                DiskPersistence.FlushFile(metadataStream);
            }
            for (var index = 0; index < files.Length; index++)
            {
                var section = await reader.ReadNextSectionAsync(cancellationToken)
                    ?? throw new UploadFailure("incompleteUpload");
                var disposition = GetDisposition(section);
                if (disposition.Name != $"file{index}" || disposition.FileName != files[index].OriginalName)
                    throw new UploadFailure("invalidSubmission");
                await WriteFileAsync(section.Body, Path.Combine(temporaryPath, files[index].StoredName), files[index], cancellationToken);
            }
            if (await reader.ReadNextSectionAsync(cancellationToken) is not null) throw new UploadFailure("invalidSubmission");
            cancellationToken.ThrowIfCancellationRequested();
            await gate.WaitAsync(cancellationToken);
            try
            {
                EnsurePlain(root);
                EnsurePlain(temporaryPath);
                if (DirectorySize(temporaryPath) != reservedBytes) throw new UploadFailure("storageFailed", 503);
                DiskPersistence.FlushDirectory(temporaryPath);
                Directory.Move(temporaryPath, finalPath!);
                published = true;
                DiskPersistence.FlushDirectory(root);
            }
            finally { gate.Release(); }
            return new UploadReceipt(true, id, files.Length);
        }
        finally
        {
            try
            {
                if (temporaryPath is not null)
                {
                    await gate.WaitAsync(CancellationToken.None);
                    try
                    {
                        if (!published) TryDelete(temporaryPath);
                        reservations.Remove(temporaryPath);
                        // Reconcile actual leftovers on failures; never assume a failed deletion freed space.
                        Audit();
                    }
                    finally { gate.Release(); }
                }
            }
            finally { uploadSlots.Release(); }
        }
    }

    private SupportedGame Validate(UploadDescription description)
    {
        var game = catalog.Games.FirstOrDefault(game => game.Id == description.GameId && !game.IsDemo)
            ?? throw new UploadFailure("unsupportedGame");
        if (string.IsNullOrWhiteSpace(description.Nickname) || description.Nickname.Length > 60
            || description.Nickname.Any(char.IsControl)) throw new UploadFailure("invalidNickname");
        if (description.Files is not { Length: > 0 and <= MaxFiles }) throw new UploadFailure("invalidSubmission");
        var items = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        foreach (var file in description.Files)
        {
            if (file is null || string.IsNullOrEmpty(file.FileName) || file.FileName.Length > 120
                || file.FileName.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '-' or '_'))
                || !char.IsAsciiLetterOrDigit(file.FileName[0]) || !items.Add(file.ItemId)
                || file.Sha256 is null || !Regex.IsMatch(file.Sha256, "^[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant))
                throw new UploadFailure("invalidSubmission");
            var item = game.Files.FirstOrDefault(item => item.Id == file.ItemId);
            if (item is null || !item.Paths.Any(path => string.Equals(path.Split('/')[^1], file.FileName, StringComparison.OrdinalIgnoreCase)))
                throw new UploadFailure("invalidFile");
            if (file.Size < 0) throw new UploadFailure("invalidSubmission");
            if (file.Size > limits.FileBytes || file.Size > limits.SubmissionBytes - total) throw new UploadFailure("tooLarge", 413);
            total += file.Size;
        }
        return game;
    }

    private static string UniqueName(string original, HashSet<string> names)
    {
        var candidate = original;
        for (var number = 2; !names.Add(candidate); number++)
            candidate = $"{Path.GetFileNameWithoutExtension(original)}_{number}{Path.GetExtension(original)}";
        return candidate;
    }

    private static (string? Name, string? FileName) GetDisposition(MultipartSection section)
    {
        if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var value)
            || !string.Equals(value.DispositionType.Value, "form-data", StringComparison.OrdinalIgnoreCase))
            throw new UploadFailure("invalidSubmission");
        return (HeaderUtilities.RemoveQuotes(value.Name).Value,
            HeaderUtilities.RemoveQuotes(value.FileNameStar.HasValue ? value.FileNameStar : value.FileName).Value);
    }

    private static async Task<byte[]> ReadMetadataAsync(Stream input, CancellationToken token)
    {
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        int length;
        while ((length = await input.ReadAsync(buffer, token)) != 0)
        {
            if (output.Length + length > MetadataLimit) throw new UploadFailure("invalidSubmission");
            output.Write(buffer, 0, length);
        }
        return output.ToArray();
    }

    private static FileStream NewFile(string path) => new(path, FileMode.CreateNew, FileAccess.Write,
        FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static async Task WriteFileAsync(Stream input, string path, StoredFile expected, CancellationToken token)
    {
        await using var output = NewFile(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            long received = 0;
            int length;
            while ((length = await input.ReadAsync(buffer.AsMemory(0, 64 * 1024), token)) != 0)
            {
                if (length > expected.Size - received) throw new UploadFailure("incompleteUpload");
                hash.AppendData(buffer, 0, length);
                await output.WriteAsync(buffer.AsMemory(0, length), token);
                received += length;
            }
            if (received != expected.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new UploadFailure("incompleteUpload");
            DiskPersistence.FlushFile(output);
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    private string Child(string name)
    {
        EnsurePlain(root);
        var path = Path.GetFullPath(Path.Combine(root, name));
        if (!string.Equals(Path.GetDirectoryName(path), root, StringComparison.Ordinal)) throw new IOException("Invalid storage path.");
        return path;
    }

    private bool IsOwned(string path) => DirectoryName.IsMatch(Path.GetFileName(path));

    private static void EnsurePlain(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Links are not allowed inside the upload storage directory.");
    }

    private static long DirectorySize(string path)
    {
        EnsurePlain(path);
        long total = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            EnsurePlain(entry);
            total = checked(total + (Directory.Exists(entry) ? DirectorySize(entry) : new FileInfo(entry).Length));
        }
        return total;
    }

    private void Audit()
    {
        EnsurePlain(root);
        long actual = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            // Internal counter checkpoints are not upload storage. The .new file may be
            // atomically renamed during an audit, so do not inspect it here.
            if (Path.GetFileName(entry) is ".request-counts.json" or ".request-counts.json.new") continue;
            EnsurePlain(entry);
            if (reservations.ContainsKey(entry) || reservations.ContainsKey(entry + ".uploading")) continue;
            actual = checked(actual + (Directory.Exists(entry) ? DirectorySize(entry) : new FileInfo(entry).Length));
        }
        usedBytes = actual;
    }

    private void MakeRoom(long incoming)
    {
        long Reserved() => reservations.Values.Sum();
        bool Fits() => incoming <= limits.StorageBytes - usedBytes - Reserved();
        if (incoming > limits.StorageBytes - Reserved()) throw new UploadFailure("storageFull", 507);
        if (Fits()) return;
        foreach (var directory in Directory.EnumerateDirectories(root)
            .Where(path => IsOwned(path) && !path.EndsWith(".uploading", StringComparison.Ordinal)
                && !reservations.ContainsKey(path + ".uploading"))
            .OrderBy(ReceivedAt).ThenBy(Path.GetFileName, StringComparer.Ordinal))
        {
            TryDelete(directory);
            Audit();
            if (Fits()) return;
        }
        throw new UploadFailure("storageFull", 507);
    }

    private DateTimeOffset ReceivedAt(string directory)
    {
        var path = Path.Combine(directory, "submission.json");
        if (File.Exists(path))
        {
            EnsurePlain(path);
            if (new FileInfo(path).Length <= MetadataLimit)
            {
                try
                {
                    var information = JsonSerializer.Deserialize<SubmissionInfo>(File.ReadAllBytes(path), JsonOptions);
                    if (information is not null && information.SubmissionId == Path.GetFileName(directory))
                        return information.ReceivedAt;
                }
                catch (JsonException) { /* A damaged record still counts toward capacity and can be cleaned. */ }
            }
        }
        return new DateTimeOffset(DateTime.ParseExact(Path.GetFileName(directory)[..17],
            "yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture), TimeSpan.FromHours(8));
    }

    private void TryDelete(string directory)
    {
        if (!Directory.Exists(directory)) return;
        try
        {
            if (Child(Path.GetFileName(directory)) != directory || !IsOwned(directory)) throw new IOException("Not an owned submission.");
            _ = DirectorySize(directory); // Reject any nested symbolic links before a recursive delete.
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Unable to remove submission directory {Directory}", Path.GetFileName(directory));
        }
    }

    public void Dispose()
    {
        processLock?.Dispose();
        gate.Dispose();
        uploadSlots.Dispose();
    }
}

public sealed record SubmissionFolder(string Name, int FileCount);
public sealed record StorageStatus(long UsedBytes, long ReservedBytes, long LimitBytes, SubmissionFolder[] Folders);
