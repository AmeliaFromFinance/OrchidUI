using System.Buffers.Binary;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OrchidUI.Protocol;

namespace OrchidUI.Host.Views;

// Header images and image previews: bytes from the shim, or an http(s) URL fetched here (5 MB at
// most). A small file can unpack into a huge picture before any scaling, so the size in its header
// is checked first, and pictures over MaxPixels or with no readable size aren't drawn. The rest are
// decoded off the UI thread at the width they're shown. Logs never carry the URL (it's a setting's value).
internal static class Images
{
    private const long MaxBytes = 5L * 1024 * 1024;
    // 16 million pixels, 64 MB decoded: room for a 4000x4000 photo.
    internal const long MaxPixels = 16L * 1000 * 1000;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    // For tests: what a URL fetch returns, instead of the network.
    internal static Func<string, Task<byte[]?>>? FetchOverride;

    public static async Task LoadAsync(ImageData data, Action<Bitmap> show, Action fail, int maxWidth, string what)
    {
        try
        {
            byte[]? bytes = data.Base64 is not null ? Convert.FromBase64String(data.Base64) : null;
            if (bytes is null && Ui.IsWebUrl(data.Url)) bytes = await FetchAsync(data.Url!);
            var bmp = bytes is null ? null : await Task.Run(() => Decode(bytes, maxWidth));
            if (bmp is null) { Dispatcher.UIThread.Post(fail); return; }
            Dispatcher.UIThread.Post(() => show(bmp));
        }
        catch (Exception ex)
        {
            HostLog.Warn(what + " couldn't be shown (" + ex.GetType().Name + ")");
            Dispatcher.UIThread.Post(fail);
        }
    }

    private static async Task<byte[]?> FetchAsync(string url)
    {
        if (FetchOverride is not null) return await FetchOverride(url);
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxBytes) throw new InvalidDataException("over 5 MB");
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int n;
        while ((n = await stream.ReadAsync(buffer)) > 0)
        {
            if (ms.Length + n > MaxBytes) throw new InvalidDataException("over 5 MB");
            ms.Write(buffer, 0, n);
        }
        return ms.ToArray();
    }

    // Full size when it's no wider than maxWidth; otherwise scaled down. Null when it's too big or
    // its size can't be read.
    internal static Bitmap? Decode(byte[] bytes, int maxWidth)
    {
        if (PixelSize(bytes) is not { } s || s.Width <= 0 || s.Height <= 0 || s.Width * s.Height > MaxPixels) return null;
        return s.Width <= maxWidth
            ? new Bitmap(new MemoryStream(bytes))
            : Bitmap.DecodeToWidth(new MemoryStream(bytes), maxWidth, BitmapInterpolationMode.HighQuality);
    }

    // Width and height from the file's header, for the formats that say so up front.
    internal static (long Width, long Height)? PixelSize(byte[] b)
    {
        static bool At(byte[] b, int i, string ascii) =>
            b.Length >= i + ascii.Length && System.Text.Encoding.ASCII.GetString(b, i, ascii.Length) == ascii;

        if (b.Length >= 24 && b[0] == 0x89 && At(b, 1, "PNG") && At(b, 12, "IHDR"))
            return (BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(16)), BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(20)));
        if (b.Length >= 10 && (At(b, 0, "GIF87a") || At(b, 0, "GIF89a")))
            return (BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(6)), BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(8)));
        if (b.Length >= 26 && At(b, 0, "BM"))
            return (Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(18))), Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(22))));
        if (b.Length >= 30 && At(b, 0, "RIFF") && At(b, 8, "WEBP"))
        {
            if (At(b, 12, "VP8X")) return (1 + (b[24] | b[25] << 8 | b[26] << 16), 1 + (b[27] | b[28] << 8 | b[29] << 16));
            if (At(b, 12, "VP8L") && b.Length >= 25)
            {
                uint bits = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(21));
                return (1 + (bits & 0x3FFF), 1 + ((bits >> 14) & 0x3FFF));
            }
            if (At(b, 12, "VP8 ")) return (BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(26)) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(28)) & 0x3FFF);
        }
        if (b.Length >= 6 && b[0] == 0 && b[1] == 0 && b[2] == 1 && b[3] == 0)
        {
            // ICO: the largest entry (an embedded PNG says its own size).
            int count = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(4));
            if (count == 0 || b.Length < 6 + 16 * count) return null;
            (long Width, long Height) largest = (0, 0);
            for (int e = 0; e < count; e++)
            {
                int at = 6 + 16 * e;
                (long, long) size = (b[at] == 0 ? 256 : b[at], b[at + 1] == 0 ? 256 : b[at + 1]);
                long offset = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at + 12));
                if (offset < b.Length && offset + 24 <= b.Length && b[offset] == 0x89 && At(b, (int)offset + 1, "PNG"))
                {
                    if (PixelSize(b[(int)offset..]) is not { } png) return null;
                    size = png;
                }
                if (size.Item1 * size.Item2 > largest.Width * largest.Height) largest = size;
            }
            return largest;
        }
        if (b.Length >= 4 && b[0] == 0xFF && b[1] == 0xD8)
        {
            // JPEG: walk the segments to the frame header (SOF0–SOF15, not DHT/JPG/DAC).
            int i = 2;
            while (i + 9 < b.Length)
            {
                if (b[i] != 0xFF) return null;
                byte marker = b[i + 1];
                if (marker == 0xFF) { i++; continue; }
                int length = b[i + 2] << 8 | b[i + 3];
                if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                    return (b[i + 7] << 8 | b[i + 8], b[i + 5] << 8 | b[i + 6]);
                if (length < 2) return null;
                i += 2 + length;
            }
        }
        return null;
    }
}
