using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SendMeTheFuckingLog.Storage;

internal static class DiskPersistence
{
    public static void FlushFile(FileStream stream)
    {
        stream.Flush(flushToDisk: true);
        // Check fsync ourselves: some .NET 10 servicing versions suppress certain Linux fsync errors.
        if (OperatingSystem.IsLinux() && Fsync(stream.SafeFileHandle.DangerousGetHandle().ToInt32()) != 0)
            throw Error("Cannot synchronize uploaded file");
    }

    public static void FlushDirectory(string path)
    {
        if (!OperatingSystem.IsLinux()) return;
        var descriptor = Open(path, 0);
        if (descriptor < 0) throw Error("Cannot open directory for synchronization");
        try
        {
            if (Fsync(descriptor) != 0) throw Error("Cannot synchronize upload directory");
        }
        finally { Close(descriptor); }
    }

    private static IOException Error(string message) =>
        new(message, new Win32Exception(Marshal.GetLastPInvokeError()));

    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int Fsync(int descriptor);
    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int Close(int descriptor);
}
