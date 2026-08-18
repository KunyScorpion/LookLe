using LeeyesViewer.Models;

namespace LeeyesViewer.Services;

public class SpreadResult
{
    public ViewerPage? LeftPage { get; set; }
    public ViewerPage? RightPage { get; set; }
    public ViewerPage? SinglePage { get; set; }
    public bool IsDual { get; set; }
    public int PageCount { get; set; } // 1 or 2
}

public static class SpreadCalculator
{
    public static SpreadResult CalculateSpread(
        IList<ViewerPage> pages,
        int currentIndex,
        ViewMode viewMode,
        bool coverStandalone)
    {
        if (pages == null || pages.Count == 0 || currentIndex < 0 || currentIndex >= pages.Count)
        {
            return new SpreadResult { IsDual = false, PageCount = 0 };
        }

        var currentPage = pages[currentIndex];

        // 1. Single page mode
        if (viewMode == ViewMode.Single)
        {
            return new SpreadResult
            {
                SinglePage = currentPage,
                IsDual = false,
                PageCount = 1
            };
        }

        // 2. Cover standalone mode
        if (currentIndex == 0 && coverStandalone)
        {
            return new SpreadResult
            {
                SinglePage = currentPage,
                IsDual = false,
                PageCount = 1
            };
        }

        // 3. Wide / Landscape image auto solo display
        if (currentPage.IsLandscape)
        {
            return new SpreadResult
            {
                SinglePage = currentPage,
                IsDual = false,
                PageCount = 1
            };
        }

        // 4. Pair with next page if available
        int nextIndex = currentIndex + 1;
        if (nextIndex >= pages.Count)
        {
            return new SpreadResult
            {
                SinglePage = currentPage,
                IsDual = false,
                PageCount = 1
            };
        }

        var nextPage = pages[nextIndex];
        if (nextPage.IsLandscape)
        {
            // Next page is landscape -> show current page alone
            return new SpreadResult
            {
                SinglePage = currentPage,
                IsDual = false,
                PageCount = 1
            };
        }

        // Dual Page Spread
        if (viewMode == ViewMode.SpreadRtl)
        {
            // Manga Right-to-Left (First on Right, Next on Left)
            return new SpreadResult
            {
                RightPage = currentPage,
                LeftPage = nextPage,
                IsDual = true,
                PageCount = 2
            };
        }
        else
        {
            // Comic Left-to-Right (First on Left, Next on Right)
            return new SpreadResult
            {
                LeftPage = currentPage,
                RightPage = nextPage,
                IsDual = true,
                PageCount = 2
            };
        }
    }

    public static int GetNextIndex(
        IList<ViewerPage> pages,
        int currentIndex,
        ViewMode viewMode,
        bool coverStandalone)
    {
        if (pages == null || pages.Count == 0 || currentIndex >= pages.Count - 1)
            return currentIndex;

        var spread = CalculateSpread(pages, currentIndex, viewMode, coverStandalone);
        int next = currentIndex + spread.PageCount;
        return Math.Min(next, pages.Count - 1);
    }

    public static int GetPrevIndex(
        IList<ViewerPage> pages,
        int currentIndex,
        ViewMode viewMode,
        bool coverStandalone)
    {
        if (pages == null || pages.Count == 0 || currentIndex <= 0)
            return 0;

        if (viewMode == ViewMode.Single)
            return Math.Max(0, currentIndex - 1);

        // Find starting index of previous spread
        int test = Math.Max(0, currentIndex - 2);
        while (test < currentIndex)
        {
            var spread = CalculateSpread(pages, test, viewMode, coverStandalone);
            if (test + spread.PageCount >= currentIndex)
            {
                return test;
            }
            test++;
        }

        return Math.Max(0, currentIndex - 1);
    }
}
