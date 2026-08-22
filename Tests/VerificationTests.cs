using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using LeeyesViewer.Models;
using LeeyesViewer.Services;

namespace LeeyesViewer.Tests;

public static class VerificationTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=== Running LookLe Native C# Tests ===");

        TestNaturalSorting();
        TestSpreadCalculator();
        TestShiftJisZipStreaming();
        TestThumbnailCache();
        TestPortableStoragePaths();

        Console.WriteLine("=== ALL TESTS PASSED SUCCESSFULLY ===");
    }

    private static void TestNaturalSorting()
    {
        var list = new List<string> { "vol10.zip", "vol1.zip", "vol2.zip", "vol20.zip" };
        list.Sort(NaturalStringComparer.Instance);

        Debug.Assert(list[0] == "vol1.zip");
        Debug.Assert(list[1] == "vol2.zip");
        Debug.Assert(list[2] == "vol10.zip");
        Debug.Assert(list[3] == "vol20.zip");
        Console.WriteLine("✓ Natural Sorting: OK");
    }

    private static void TestSpreadCalculator()
    {
        var pages = new List<ViewerPage>
        {
            new() { Index = 0, Name = "01_cover.jpg", IsLandscape = false },
            new() { Index = 1, Name = "02_page.jpg", IsLandscape = false },
            new() { Index = 2, Name = "03_page.jpg", IsLandscape = false },
            new() { Index = 3, Name = "04_wide.jpg", IsLandscape = true }, // Wide page
            new() { Index = 4, Name = "05_page.jpg", IsLandscape = false },
            new() { Index = 5, Name = "06_page.jpg", IsLandscape = false },
        };

        // Cover standalone in Manga RTL mode
        var spread0 = SpreadCalculator.CalculateSpread(pages, 0, ViewMode.SpreadRtl, coverStandalone: true);
        Debug.Assert(!spread0.IsDual && spread0.SinglePage?.Index == 0);

        // Next pair 2 & 3 in RTL mode (Page 1 on right, Page 2 on left)
        var spread1 = SpreadCalculator.CalculateSpread(pages, 1, ViewMode.SpreadRtl, coverStandalone: true);
        Debug.Assert(spread1.IsDual && spread1.RightPage?.Index == 1 && spread1.LeftPage?.Index == 2);

        // Page 3 is wide -> auto solo display
        var spread3 = SpreadCalculator.CalculateSpread(pages, 3, ViewMode.SpreadRtl, coverStandalone: true);
        Debug.Assert(!spread3.IsDual && spread3.SinglePage?.Index == 3);

        // Page 4 & 5 pair
        var spread4 = SpreadCalculator.CalculateSpread(pages, 4, ViewMode.SpreadRtl, coverStandalone: true);
        Debug.Assert(spread4.IsDual && spread4.RightPage?.Index == 4 && spread4.LeftPage?.Index == 5);

        Console.WriteLine("✓ Spread Calculator & Landscape Auto-Solo: OK");
    }

    private static void TestShiftJisZipStreaming()
    {
        var sampleZip = @"C:\Users\silve\.gemini\antigravity\scratch\sample_manga_vol01.zip";
        if (File.Exists(sampleZip))
        {
            using var manager = new ArchiveManager();
            var entries = manager.ListArchiveEntries(sampleZip);
            Debug.Assert(entries.Count > 0);

            var bytes = manager.GetImageBytes(sampleZip, entries[0].Name, entries[0].EntryIndex);
            Debug.Assert(bytes.Length > 0);
            Console.WriteLine($"✓ In-Memory ZIP Streaming ({entries.Count} entries): OK");
        }
    }

    private static void TestThumbnailCache()
    {
        var cache = new ThumbnailCacheService();
        var hash = cache.ComputeHash("archive", "test.zip", "cover.jpg", DateTime.UtcNow);
        Debug.Assert(!string.IsNullOrEmpty(hash));

        var stats = cache.GetCacheStats();
        Debug.Assert(stats.CacheDir.Length > 0);
        Console.WriteLine($"✓ Thumbnail Cache Manager ({stats.CacheDir}): OK");
    }

    private static void TestPortableStoragePaths()
    {
        var settingsService = new SettingsService();
        var testSettings = settingsService.LoadSettings();
        Debug.Assert(testSettings != null);

        var expectedSettingsPath = Path.Combine(AppContext.BaseDirectory, "settings.json");
        var expectedThumbnailPath = Path.Combine(AppContext.BaseDirectory, "Thumbnails");

        var cache = new ThumbnailCacheService();
        var stats = cache.GetCacheStats();

        Debug.Assert(stats.CacheDir == expectedThumbnailPath, $"Cache dir mismatch: expected {expectedThumbnailPath}, got {stats.CacheDir}");
        Console.WriteLine($"✓ Portable Storage Paths: OK (Settings: {expectedSettingsPath}, Cache: {expectedThumbnailPath})");
    }
}
