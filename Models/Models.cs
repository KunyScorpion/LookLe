using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media.Imaging;

namespace LeeyesViewer.Models;

public class DirectoryItem : ObservableObject
{
    private string _name = string.Empty;
    private string _path = string.Empty;
    private ItemType _itemType;
    private long? _size;
    private bool _isFavorite;
    private BitmapSource? _coverBitmap;
    private bool _isActiveItem;
    private int _pageIndex = -1;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Path
    {
        get => _path;
        set => SetProperty(ref _path, value);
    }

    public ItemType ItemType
    {
        get => _itemType;
        set => SetProperty(ref _itemType, value);
    }

    public long? Size
    {
        get => _size;
        set => SetProperty(ref _size, value);
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        set => SetProperty(ref _isFavorite, value);
    }

    public BitmapSource? CoverBitmap
    {
        get => _coverBitmap;
        set => SetProperty(ref _coverBitmap, value);
    }

    public bool IsActiveItem
    {
        get => _isActiveItem;
        set => SetProperty(ref _isActiveItem, value);
    }

    public int PageIndex
    {
        get => _pageIndex;
        set => SetProperty(ref _pageIndex, value);
    }
}

public class FavoriteItem : ObservableObject
{
    private string _path = string.Empty;
    private string _name = string.Empty;
    private bool _isArchive;
    private BitmapSource? _coverBitmap;
    private bool _isActiveItem;

    public string Path
    {
        get => _path;
        set => SetProperty(ref _path, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public bool IsArchive
    {
        get => _isArchive;
        set => SetProperty(ref _isArchive, value);
    }

    [JsonIgnore]
    public BitmapSource? CoverBitmap
    {
        get => _coverBitmap;
        set => SetProperty(ref _coverBitmap, value);
    }

    [JsonIgnore]
    public bool IsActiveItem
    {
        get => _isActiveItem;
        set => SetProperty(ref _isActiveItem, value);
    }
}

public class ThumbnailCacheStats
{
    public int FileCount { get; set; }
    public long TotalBytes { get; set; }
    public string CacheDir { get; set; } = string.Empty;
}

public class AppSettings
{
    public List<FavoriteItem> Favorites { get; set; } = new();
    public string? LastOpenedPath { get; set; }
    public int ThumbnailSize { get; set; } = 150;
    public ExplorerViewMode ExplorerMode { get; set; } = ExplorerViewMode.Grid;
    public ViewMode ViewMode { get; set; } = ViewMode.SpreadRtl;
    public bool CoverStandalone { get; set; } = true;
    public FitMode FitMode { get; set; } = FitMode.FitWindow;
    public double UiScale { get; set; } = 1.0;
    public double BaseFontSize { get; set; } = 14.0;
    public bool StartOnBookshelf { get; set; } = true;
    public bool ContinuousNavigation { get; set; } = false;

    // Window size and state persistence
    public double WindowWidth { get; set; } = 1320;
    public double WindowHeight { get; set; } = 860;
    public double WindowLeft { get; set; } = -1;
    public double WindowTop { get; set; } = -1;
    public WindowState WindowState { get; set; } = WindowState.Normal;
    public double SidebarWidth { get; set; } = 290;
}
