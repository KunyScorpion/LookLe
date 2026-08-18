using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media.Imaging;
using LeeyesViewer.Models;

namespace LeeyesViewer.Services;

public class ImagePipelineService
{
    private readonly ArchiveManager _archiveManager;
    private readonly ThumbnailCacheService _cacheService;

    private readonly ConcurrentDictionary<string, BitmapSource> _fullImageCache = new();
    private readonly ConcurrentDictionary<string, BitmapSource> _thumbnailCache = new();
    private readonly ConcurrentDictionary<string, BitmapSource> _coverCache = new();

    private CancellationTokenSource? _prefetchCts;
    private readonly object _prefetchLock = new();

    public ImagePipelineService(ArchiveManager archiveManager, ThumbnailCacheService cacheService)
    {
        _archiveManager = archiveManager;
        _cacheService = cacheService;
    }

    private string GetFullKey(ViewerPage page) =>
        $"{page.SourceType}|{page.Path}|{page.EntryName ?? page.Index.ToString()}";

    private string GetThumbKey(ViewerPage page) =>
        $"thumb|{page.SourceType}|{page.Path}|{page.EntryName ?? page.Index.ToString()}";

    public async Task<byte[]> GetImageBytesAsync(ViewerPage page, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (page.SourceType == "archive")
            {
                return _archiveManager.GetImageBytes(page.Path, page.EntryName, page.EntryIndex);
            }
            return File.ReadAllBytes(page.Path);
        }, cancellationToken);
    }

    public async Task<BitmapSource> LoadFullImageAsync(ViewerPage page, CancellationToken cancellationToken = default)
    {
        var key = GetFullKey(page);
        if (_fullImageCache.TryGetValue(key, out var cached))
        {
            page.Width = cached.PixelWidth;
            page.Height = cached.PixelHeight;
            page.IsLandscape = cached.PixelWidth > cached.PixelHeight;
            return cached;
        }

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            byte[] bytes;
            if (page.SourceType == "archive")
            {
                bytes = _archiveManager.GetImageBytes(page.Path, page.EntryName, page.EntryIndex);
            }
            else
            {
                bytes = File.ReadAllBytes(page.Path);
            }

            cancellationToken.ThrowIfCancellationRequested();

            using var ms = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze(); // Free-thread for fast GPU composition

            // Store dimensions
            page.Width = bitmap.PixelWidth;
            page.Height = bitmap.PixelHeight;
            page.IsLandscape = bitmap.PixelWidth > bitmap.PixelHeight;

            // Trim cache if too large
            if (_fullImageCache.Count >= 40)
            {
                var firstKey = _fullImageCache.Keys.FirstOrDefault();
                if (firstKey != null) _fullImageCache.TryRemove(firstKey, out _);
            }

            _fullImageCache[key] = bitmap;
            return (BitmapSource)bitmap;
        }, cancellationToken);
    }

    public async Task<BitmapSource?> LoadThumbnailAsync(ViewerPage page)
    {
        var key = GetThumbKey(page);
        if (_thumbnailCache.TryGetValue(key, out var cached))
        {
            page.IsLandscape = cached.PixelWidth > cached.PixelHeight;
            return cached;
        }

        return await Task.Run(() =>
        {
            DateTime? mtime = page.SourceType == "file" && File.Exists(page.Path)
                ? File.GetLastWriteTimeUtc(page.Path)
                : null;

            var hash = _cacheService.ComputeHash(page.SourceType, page.Path, page.EntryName, mtime);

            // 1. Check disk cache
            var diskCached = _cacheService.GetCachedThumbnail(hash);
            if (diskCached != null)
            {
                page.IsLandscape = diskCached.PixelWidth > diskCached.PixelHeight;
                _thumbnailCache[key] = diskCached;
                return diskCached;
            }

            // 2. Decode & generate
            byte[] bytes;
            if (page.SourceType == "archive")
            {
                bytes = _archiveManager.GetImageBytes(page.Path, page.EntryName, page.EntryIndex);
            }
            else
            {
                bytes = File.ReadAllBytes(page.Path);
            }

            var generated = _cacheService.GenerateAndSaveThumbnail(hash, bytes);
            if (generated != null)
            {
                page.IsLandscape = generated.PixelWidth > generated.PixelHeight;
                _thumbnailCache[key] = generated;
            }
            return generated;
        });
    }

    public async Task<BitmapSource?> LoadContainerCoverAsync(string path, bool isArchive)
    {
        var key = $"cover|{(isArchive ? "zip" : "dir")}|{path}";
        if (_coverCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        return await Task.Run(() =>
        {
            try
            {
                if (isArchive)
                {
                    var firstEntry = _archiveManager.GetFirstImageEntry(path);
                    if (firstEntry == null) return null;

                    var mtime = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : (DateTime?)null;
                    var hash = _cacheService.ComputeHash("archive", path, firstEntry.Name, mtime);

                    var diskCached = _cacheService.GetCachedThumbnail(hash);
                    if (diskCached != null)
                    {
                        _coverCache[key] = diskCached;
                        return diskCached;
                    }

                    var bytes = _archiveManager.GetImageBytes(path, firstEntry.Name, firstEntry.EntryIndex);
                    var generated = _cacheService.GenerateAndSaveThumbnail(hash, bytes);
                    if (generated != null)
                    {
                        _coverCache[key] = generated;
                    }
                    return generated;
                }
                else
                {
                    var firstImage = Directory.EnumerateFiles(path)
                        .Where(f => ArchiveManager.IsSupportedImageExtension(Path.GetExtension(f)))
                        .OrderBy(f => f, NaturalStringComparer.Instance)
                        .FirstOrDefault();

                    if (firstImage == null) return null;

                    var mtime = File.GetLastWriteTimeUtc(firstImage);
                    var hash = _cacheService.ComputeHash("file", firstImage, null, mtime);

                    var diskCached = _cacheService.GetCachedThumbnail(hash);
                    if (diskCached != null)
                    {
                        _coverCache[key] = diskCached;
                        return diskCached;
                    }

                    var bytes = File.ReadAllBytes(firstImage);
                    var generated = _cacheService.GenerateAndSaveThumbnail(hash, bytes);
                    _coverCache[key] = generated;
                    return generated;
                }
            }
            catch
            {
                return null;
            }
        });
    }

    public void PrefetchPages(IList<ViewerPage> pages, int currentIndex)
    {
        if (pages == null || pages.Count == 0) return;

        lock (_prefetchLock)
        {
            _prefetchCts?.Cancel();
            _prefetchCts?.Dispose();
            _prefetchCts = new CancellationTokenSource();
            var ct = _prefetchCts.Token;

            var targetIndices = new List<int>();
            // Next +1 .. +6
            for (int i = 1; i <= 6; i++)
            {
                if (currentIndex + i < pages.Count) targetIndices.Add(currentIndex + i);
            }
            // Prev -1 .. -2
            for (int i = 1; i <= 2; i++)
            {
                if (currentIndex - i >= 0) targetIndices.Add(currentIndex - i);
            }

            Task.Run(async () =>
            {
                foreach (var idx in targetIndices)
                {
                    if (ct.IsCancellationRequested) break;
                    var page = pages[idx];
                    try
                    {
                        await LoadFullImageAsync(page, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                        // Ignore prefetch errors
                    }
                }
            }, ct);
        }
    }

    public void ClearMemoryCache()
    {
        lock (_prefetchLock)
        {
            _prefetchCts?.Cancel();
        }
        _fullImageCache.Clear();
        _thumbnailCache.Clear();
        _coverCache.Clear();
    }
}
