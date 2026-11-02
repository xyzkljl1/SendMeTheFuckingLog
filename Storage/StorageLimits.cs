namespace SendMeTheFuckingLog.Storage;

public sealed class StorageLimits
{
    public decimal MaxStorageMiB { get; init; } = 1024;
    public decimal MaxFileMiB { get; init; } = 20;
    public decimal MaxSubmissionMiB { get; init; } = 100;

    public long StorageBytes => Bytes(MaxStorageMiB);
    public long FileBytes => Bytes(MaxFileMiB);
    public long SubmissionBytes => Bytes(MaxSubmissionMiB);
    public long RequestBytes => checked(SubmissionBytes + 1024 * 1024);

    public void Validate()
    {
        if (StorageBytes <= 0 || FileBytes <= 0 || SubmissionBytes <= 0
            || FileBytes > SubmissionBytes || SubmissionBytes > StorageBytes)
            throw new InvalidOperationException("Storage limits must satisfy 0 < MaxFileMiB <= MaxSubmissionMiB <= MaxStorageMiB.");
        _ = RequestBytes;
    }

    private static long Bytes(decimal value) => checked((long)(value * 1024 * 1024));
}

public sealed class UploadFailure(string code, int statusCode = 400) : Exception(code)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public sealed record UploadDescription(string GameId, string Nickname, UploadFileDescription[] Files, string SelectionMode = "auto");
public sealed record UploadFileDescription(string ItemId, string FileName, long Size, string Sha256);
public sealed record StoredFile(string ItemId, string OriginalName, string StoredName, long Size, string Sha256);
public sealed record SubmissionInfo(int FormatVersion, string SubmissionId, string GameId,
    string Nickname, DateTimeOffset ReceivedAt, StoredFile[] Files);
public sealed record UploadReceipt(bool Saved, string SubmissionId, int FileCount);
