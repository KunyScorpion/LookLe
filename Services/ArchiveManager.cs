using System.IO;
using System.IO.Compression;
using System.Text;
using LeeyesViewer.Models;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace LeeyesViewer.Services;

public class ArchiveManager : IDisposable
{
    private interface ICachedArchive : IDisposable
    {
        List<ViewerPage> ListEntries(string archivePath);
        byte[] GetImageBytes(string? entryName, int? entryIndex);
        DateTime LastAccess { get; set; }
    }

    private class ZipCachedArchive : ICachedArchive
    {
        private readonly System.IO.Compression.ZipArchive _archive;
        private readonly FileStream _stream;
        public DateTime LastAccess { get; set; } = DateTime.UtcNow;

        public ZipCachedArchive(string archivePath)
        {
            _stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            Encoding encoding;
            try
            {
                encoding = Encoding.GetEncoding("shift_jis");
            }
            catch
            {
                encoding = Encoding.UTF8;
            }
            _archive = new System.IO.Compression.ZipArchive(_stream, ZipArchiveMode.Read, leaveOpen: false, entryNameEncoding: encoding);
        }

        public List<ViewerPage> ListEntries(string archivePath)
        {
            var pages = new List<ViewerPage>();
            int index = 0;

            foreach (var entry in _archive.Entries)
            {
                var fullName = entry.FullName;
                if (string.IsNullOrEmpty(fullName) || fullName.EndsWith('/') || fullName.EndsWith('\\'))
                    continue;

                if (fullName.StartsWith("__MACOSX", StringComparison.OrdinalIgnoreCase) ||
                    fullName.Contains("/.") || fullName.StartsWith('.'))
                    continue;

                var ext = Path.GetExtension(fullName);
                // Load only image files, completely skip/ignore nested archives
                if (IsSupportedImageExtension(ext))
                {
                    pages.Add(new ViewerPage
                    {
                        Index = index++,
                        Name = fullName,
                        SourceType = "archive",
                        Path = archivePath,
                        EntryName = fullName,
                        EntryIndex = index - 1,
                        Size = entry.Length
                    });
                }
            }

            pages.Sort((a, b) => NaturalStringComparer.Instance.Compare(a.Name, b.Name));
            for (int i = 0; i < pages.Count; i++) pages[i].Index = i;
            return pages;
        }

        public byte[] GetImageBytes(string? entryName, int? entryIndex)
        {
            ZipArchiveEntry? entry = null;
            if (!string.IsNullOrEmpty(entryName))
            {
                entry = _archive.GetEntry(entryName);
            }
            if (entry == null && entryIndex.HasValue && entryIndex.Value < _archive.Entries.Count)
            {
                entry = _archive.Entries[entryIndex.Value];
            }
            if (entry == null)
            {
                throw new FileNotFoundException($"Entry '{entryName}' not found in archive");
            }

            using var entryStream = entry.Open();
            using var ms = new MemoryStream((int)entry.Length);
            entryStream.CopyTo(ms);
            return ms.ToArray();
        }

        public void Dispose()
        {
            _archive.Dispose();
            _stream.Dispose();
        }
    }

    private class SharpCompressCachedArchive : ICachedArchive
    {
        private readonly IArchive _archive;
        private readonly FileStream _stream;
        public DateTime LastAccess { get; set; } = DateTime.UtcNow;

        public SharpCompressCachedArchive(string archivePath)
        {
            _stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var readerOptions = new ReaderOptions
            {
                ArchiveEncoding = new ArchiveEncoding
                {
                    Default = Encoding.GetEncoding("shift_jis")
                }
            };
            _archive = ArchiveFactory.OpenArchive(_stream, readerOptions);
        }

        public List<ViewerPage> ListEntries(string archivePath)
        {
            var pages = new List<ViewerPage>();
            int index = 0;

            foreach (var entry in _archive.Entries)
            {
                if (entry.IsDirectory) continue;
                var key = entry.Key;
                if (string.IsNullOrEmpty(key)) continue;

                if (key.StartsWith("__MACOSX", StringComparison.OrdinalIgnoreCase) ||
                    key.Contains("/.") || key.StartsWith('.'))
                    continue;

                var ext = Path.GetExtension(key);
                // Load only image files, completely skip/ignore nested archives
                if (IsSupportedImageExtension(ext))
                {
                    pages.Add(new ViewerPage
                    {
                        Index = index++,
                        Name = key,
                        SourceType = "archive",
                        Path = archivePath,
                        EntryName = key,
                        EntryIndex = index - 1,
                        Size = entry.Size
                    });
                }
            }

            pages.Sort((a, b) => NaturalStringComparer.Instance.Compare(a.Name, b.Name));
            for (int i = 0; i < pages.Count; i++) pages[i].Index = i;
            return pages;
        }

        public byte[] GetImageBytes(string? entryName, int? entryIndex)
        {
            IArchiveEntry? entry = null;
            if (!string.IsNullOrEmpty(entryName))
            {
                entry = _archive.Entries.FirstOrDefault(e => e.Key == entryName);
            }
            if (entry == null && entryIndex.HasValue && entryIndex.Value < _archive.Entries.Count())
            {
                entry = _archive.Entries.ElementAt(entryIndex.Value);
            }
            if (entry == null)
            {
                throw new FileNotFoundException($"Entry '{entryName}' not found in archive");
            }

            using var entryStream = entry.OpenEntryStream();
            using var ms = new MemoryStream();
            entryStream.CopyTo(ms);
            return ms.ToArray();
        }

        public void Dispose()
        {
            _archive.Dispose();
            _stream.Dispose();
        }
    }

    private readonly Dictionary<string, ICachedArchive> _archiveCache = new();
    private readonly object _lock = new();
    private const int MaxCachedArchives = 4;

    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".avif", ".gif", ".bmp"
    };

    public static bool IsSupportedImageExtension(string ext)
    {
        return SupportedImageExtensions.Contains(ext);
    }

    public static bool IsArchiveExtension(string ext)
    {
        return string.Equals(ext, ".zip", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".cbz", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".rar", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".cbr", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".7z", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".cb7", StringComparison.OrdinalIgnoreCase);
    }

    private ICachedArchive GetOrCreateArchive(string archivePath)
    {
        lock (_lock)
        {
            if (_archiveCache.TryGetValue(archivePath, out var cached))
            {
                cached.LastAccess = DateTime.UtcNow;
                return cached;
            }

            // Evict oldest if full
            if (_archiveCache.Count >= MaxCachedArchives)
            {
                var oldestKey = _archiveCache.OrderBy(kv => kv.Value.LastAccess).First().Key;
                _archiveCache[oldestKey].Dispose();
                _archiveCache.Remove(oldestKey);
            }

            var ext = Path.GetExtension(archivePath);
            ICachedArchive newArchive;

            if (string.Equals(ext, ".zip", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".cbz", StringComparison.OrdinalIgnoreCase))
            {
                newArchive = new ZipCachedArchive(archivePath);
            }
            else
            {
                newArchive = new SharpCompressCachedArchive(archivePath);
            }

            _archiveCache[archivePath] = newArchive;
            return newArchive;
        }
    }

    public List<ViewerPage> ListArchiveEntries(string archivePath)
    {
        lock (_lock)
        {
            var archive = GetOrCreateArchive(archivePath);
            return archive.ListEntries(archivePath);
        }
    }

    public byte[] GetImageBytes(string archivePath, string? entryName, int? entryIndex)
    {
        lock (_lock)
        {
            var archive = GetOrCreateArchive(archivePath);
            return archive.GetImageBytes(entryName, entryIndex);
        }
    }

    public ViewerPage? GetFirstImageEntry(string archivePath)
    {
        var entries = ListArchiveEntries(archivePath);
        return entries.FirstOrDefault();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var item in _archiveCache.Values)
            {
                item.Dispose();
            }
            _archiveCache.Clear();
        }
    }
}
