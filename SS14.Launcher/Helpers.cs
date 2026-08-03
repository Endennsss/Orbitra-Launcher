using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mono.Unix;
using Serilog;
using TerraFX.Interop.Windows;
using Splat;
using SS14.Launcher.Models.Data;
using Win = TerraFX.Interop.Windows.Windows;

namespace SS14.Launcher;

public static class Helpers
{
    public static readonly JsonSerializerOptions JsonWebOptions = new(JsonSerializerDefaults.Web);

    public delegate void DownloadProgressCallback(long downloaded, long total);

    public static void ExtractZipToDirectory(string directory, Stream zipStream)
    {
        using var zipArchive = new ZipArchive(zipStream);
        zipArchive.ExtractToDirectory(directory);
    }

    public static void ClearDirectory(string directory)
    {
        var dirInfo = new DirectoryInfo(directory);
        foreach (var fileInfo in dirInfo.EnumerateFiles())
        {
            fileInfo.Delete();
        }

        foreach (var childDirInfo in dirInfo.EnumerateDirectories())
        {
            childDirInfo.Delete(true);
        }
    }

    public static void EnsureDirectoryExists(string dir)
    {
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    public static async Task DownloadToStream(this HttpClient client, string uri, Stream stream,
        DownloadProgressCallback? progress = null, CancellationToken cancel = default)
    {
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancel);
        response.EnsureSuccessStatusCode();
        if (stream.CanSeek) { stream.SetLength(0); stream.Position = 0; }

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancel);
        var totalLength = response.Content.Headers.ContentLength;
        if (totalLength.HasValue) progress?.Invoke(0, totalLength.Value);

        var totalRead = 0L;
        var reads = 0L;
        const int bufferLength = 32 * 1024;
        var buffer = ArrayPool<byte>.Shared.Rent(bufferLength);
        try
        {
            while (true)
            {
                var read = await contentStream.ReadAsync(buffer.AsMemory(0, bufferLength), cancel);
                if (read == 0) break;
                await DownloadBandwidthLimiter.ThrottleAsync(read, cancel);
                await stream.WriteAsync(buffer.AsMemory(0, read), cancel);

                reads++;
                totalRead += read;
                if (totalLength.HasValue && reads % 8 == 0) progress?.Invoke(totalRead, totalLength.Value);
            }
            if (totalLength.HasValue) progress?.Invoke(totalRead, totalLength.Value);
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    /// <summary>
    /// Open a URI provided by a game server in the user's browser. Refuse to open anything other than http/https.
    /// </summary>
    /// <param name="uri">The URI to open.</param>
    public static void SafeOpenServerUri(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsedUri))
        {
            Log.Error("Unable to parse URI in server-provided link: {Link}", uri);
            return;
        }

        if (parsedUri.Scheme is not ("http" or "https"))
        {
            Log.Error("Refusing to open server-provided link {Link}, only http/https are allowed", parsedUri);
            return;
        }

        OpenUri(parsedUri.ToString());
    }

    public static void OpenUri(Uri uri)
    {
        OpenUri(uri.ToString());
    }

    public static void OpenUri(string uri)
    {
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    }

    private static readonly string[] ByteSuffixes =
    {
        "B",
        "KiB",
        "MiB",
        "GiB",
        "TiB",
        "PiB",
        "EiB",
        "ZiB",
        "YiB"
    };

    public static string FormatBytes(long bytes)
    {
        double d = bytes;
        var i = 0;
        for (; i < ByteSuffixes.Length && d >= 1024; i++)
        {
            d /= 1024;
        }

        return $"{Math.Round(d, 2)} {ByteSuffixes[i]}";
    }

    public static async Task<T> AsJson<T>(this HttpContent content) where T : notnull
    {
        var str = await content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(str, JsonWebOptions) ??
               throw new JsonException("AsJson: did not expect null response");
    }

    public static unsafe void MarkDirectoryCompress(string path)
    {
        // TODO: Linux: chattr +c
        if (!OperatingSystem.IsWindows())
            return;

        fixed (char* pPath = path)
        {
            var handle = Win.CreateFileW(
                pPath,
                Win.GENERIC_ALL,
                FILE.FILE_SHARE_READ,
                null,
                OPEN.OPEN_EXISTING,
                FILE.FILE_FLAG_BACKUP_SEMANTICS,
                HANDLE.NULL);

            var lpBytesReturned = 0u;
            var lpInBuffer = (short)Win.COMPRESSION_FORMAT_DEFAULT;

            Win.DeviceIoControl(
                handle,
                FSCTL.FSCTL_SET_COMPRESSION,
                &lpInBuffer,
                sizeof(short),
                null,
                0,
                &lpBytesReturned,
                null);

            Win.CloseHandle(handle);
        }
    }

    public static void ChmodPlusX(string path)
    {
        var f = new UnixFileInfo(path);
        f.FileAccessPermissions |=
            FileAccessPermissions.UserExecute | FileAccessPermissions.GroupExecute |
            FileAccessPermissions.OtherExecute;
    }
    public static unsafe int MessageBoxHelper(string text, string caption, uint type)
    {
        fixed (char* pText = text)
        fixed (char* pCaption = caption)
        {
            return Win.MessageBoxW(HWND.NULL, pText, pCaption, type);
        }
    }
}
