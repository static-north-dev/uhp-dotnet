using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace StaticNorth.Uhp.Reference;

// Only the reference host needs these Unix operations. The three SDK packages remain portable.
internal static partial class ReferenceFileSystem
{
    public static async Task<byte[]?> ReadAsync(string root, string relative, long maximum, CancellationToken ct)
    {
        await using var stream = OpenRead(root, relative);

        if (stream is null || !stream.CanSeek)
        {
            return null;
        }

        var length = stream.Length;

        if (length > maximum)
        {
            return null;
        }

        var bytes = GC.AllocateUninitializedArray<byte>(checked((int)length));
        var read = await stream
            .ReadAtLeastAsync(bytes, bytes.Length, false, ct)
            .ConfigureAwait(false);
        return read == bytes.Length ? bytes : null;
    }

    private static FileStream? OpenRead(string root, string relative)
    {
        if (!(OperatingSystem.IsMacOS() || OperatingSystem.IsLinux()) || Path.IsPathRooted(relative))
        {
            return null;
        }

        var mac = OperatingSystem.IsMacOS();
        var noFollow = mac ? 0x100 : 0x20000;
        var directory = mac ? 0x100000 : 0x10000;
        var closeOnExec = mac ? 0x1000000 : 0x80000;
        var nonBlocking = mac ? 4 : 0x800;
        using var rootHandle = new SafeFileHandle((IntPtr)Open(root, noFollow | directory | closeOnExec), true);

        if (rootHandle.IsInvalid)
        {
            return null;
        }

        var current = rootHandle;

        try
        {
            var start = 0;

            while (true)
            {
                var slash = relative.IndexOf('/', start);
                var component = slash < 0 ? relative[start..] : relative[start..slash];

                if (component is "" or "." or ".." || component.Contains('\0'))
                {
                    return null;
                }

                var handle = new SafeFileHandle((IntPtr)OpenAt(current, component, noFollow | closeOnExec | (slash < 0 ? nonBlocking : directory)), true);

                if (handle.IsInvalid)
                {
                    handle.Dispose();
                    return null;
                }

                if (slash < 0)
                {
                    try
                    {
                        return new FileStream(handle, FileAccess.Read, 16_384, isAsync: false);
                    }
                    catch
                    {
                        handle.Dispose();
                        throw;
                    }
                }

                if (!ReferenceEquals(current, rootHandle))
                {
                    current.Dispose();
                }

                current = handle;
                start = slash + 1;
            }
        }
        finally
        {
            if (!ReferenceEquals(current, rootHandle))
            {
                current.Dispose();
            }
        }
    }

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Open(string path, int flags);

    [LibraryImport("libc", EntryPoint = "openat", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int OpenAt(SafeFileHandle directory, string path, int flags);
}
