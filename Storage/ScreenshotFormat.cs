using System.Buffers.Binary;

namespace SendMeTheFuckingLog.Storage;

// Identify allowed raster containers without decoding images into server memory.
public static class ScreenshotFormat
{
    public const int HeaderLength = 33;

    public static string? ContentType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        _ => null
    };

    public static bool Matches(string name, ReadOnlySpan<byte> header, long size) => ContentType(name) switch
    {
        "image/png" => header.Length >= 33 && size >= 45
            && header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            && BinaryPrimitives.ReadUInt32BigEndian(header[8..12]) == 13
            && header[12..16].SequenceEqual("IHDR"u8)
            && BinaryPrimitives.ReadUInt32BigEndian(header[16..20]) > 0
            && BinaryPrimitives.ReadUInt32BigEndian(header[20..24]) > 0,
        "image/jpeg" => header.Length >= 4 && size >= 4
            && header[0] == 255 && header[1] == 216 && header[2] == 255
            && header[3] is not (0 or 255),
        "image/webp" => header.Length >= 20 && size >= 20
            && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8)
            && (header[12..16].SequenceEqual("VP8 "u8) || header[12..16].SequenceEqual("VP8L"u8) || header[12..16].SequenceEqual("VP8X"u8))
            && (long)BinaryPrimitives.ReadUInt32LittleEndian(header[4..8]) + 8 == size,
        _ => false
    };

    public static async Task<bool> MatchesAsync(string name, FileStream stream, CancellationToken token)
    {
        var header = new byte[HeaderLength];
        var count = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken: token);
        stream.Position = 0;
        return Matches(name, header.AsSpan(0, count), stream.Length);
    }
}
