using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using LeeyesViewer.Models;

namespace LeeyesViewer.Services;

public class ThumbnailCacheService
{
    private readonly string _cacheDir;
    private const int MaxThumbnailDim = 250;

    public ThumbnailCacheService()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _cacheDir = Path.Combine(localAppData, "LeeyesViewer", "Thumbnails");

        if (!Directory.Exists(_cacheDir))
        {
            Directory.CreateDirectory(_cacheDir);
        }
    }

    public string ComputeHash(string sourceType, string path, string? entryName, DateTime? mtime)
    {
        var rawKey = $"{sourceType}|{path}|{entryName ?? ""}|{(mtime?.Ticks ?? 0)}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToHexString(hashBytes).ToLowerInvariant()[..24];
    }

    public BitmapSource? GetCachedThumbnail(string hashKey)
    {
        var filePath = Path.Combine(_cacheDir, $"{hashKey}.jpg");
        if (File.Exists(filePath))
        {
            try
            {
                var bytes = File.ReadAllBytes(filePath);
                if (bytes.Length > 0)
                {
                    return CreateFrozenBitmapFromBytes(bytes, 0);
                }
            }
            catch
            {
                // Fallback on corrupt file
            }
        }
        return null;
    }

    public BitmapSource GenerateAndSaveThumbnail(string hashKey, byte[] rawBytes)
    {
        // 1. Decode frame directly with WIC downscaled via DecodePixelWidth/Height
        var thumbBitmap = CreateFrozenBitmapFromBytes(rawBytes, MaxThumbnailDim);

        // 2. Save to disk cache as JPEG (Fast WIC native encoder)
        var filePath = Path.Combine(_cacheDir, $"{hashKey}.jpg");
        var tempPath = Path.Combine(_cacheDir, $"{hashKey}.tmp.{Environment.ProcessId}");

        try
        {
            using (var outStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
            {
                var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
                encoder.Frames.Add(BitmapFrame.Create(thumbBitmap));
                encoder.Save(outStream);
            }

            if (File.Exists(tempPath))
            {
                File.Move(tempPath, filePath, overwrite: true);
            }
        }
        catch
        {
            // Ignore write errors
        }

        return thumbBitmap;
    }

    private static BitmapSource CreateFrozenBitmapFromBytes(byte[] bytes, int decodeMaxDim)
    {
        using var ms = new MemoryStream(bytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        if (decodeMaxDim > 0)
        {
            bitmap.DecodePixelWidth = decodeMaxDim;
        }
        bitmap.StreamSource = ms;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    public ThumbnailCacheStats GetCacheStats()
    {
        int fileCount = 0;
        long totalBytes = 0;

        if (Directory.Exists(_cacheDir))
        {
            var dirInfo = new DirectoryInfo(_cacheDir);
            foreach (var file in dirInfo.EnumerateFiles("*.jpg"))
            {
                fileCount++;
                totalBytes += file.Length;
            }
        }

        return new ThumbnailCacheStats
        {
            FileCount = fileCount,
            TotalBytes = totalBytes,
            CacheDir = _cacheDir
        };
    }

    public ThumbnailCacheStats ClearCache()
    {
        if (Directory.Exists(_cacheDir))
        {
            var dirInfo = new DirectoryInfo(_cacheDir);
            foreach (var file in dirInfo.EnumerateFiles())
            {
                try
                {
                    file.Delete();
                }
                catch
                {
                    // Ignore locked files
                }
            }
        }

        return GetCacheStats();
    }
}
