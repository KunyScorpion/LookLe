using System.Windows.Media.Imaging;

namespace LeeyesViewer.Models;

public class ViewerPage : ObservableObject
{
    private int _index;
    private string _name = string.Empty;
    private string _sourceType = "file";
    private string _path = string.Empty;
    private string? _entryName;
    private int? _entryIndex;
    private long _size;
    private int _width;
    private int _height;
    private bool _isLandscape;
    private BitmapSource? _thumbnailBitmap;
    private bool _isActivePage;

    public int Index
    {
        get => _index;
        set => SetProperty(ref _index, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string SourceType
    {
        get => _sourceType;
        set => SetProperty(ref _sourceType, value);
    }

    public string Path
    {
        get => _path;
        set => SetProperty(ref _path, value);
    }

    public string? EntryName
    {
        get => _entryName;
        set => SetProperty(ref _entryName, value);
    }

    public int? EntryIndex
    {
        get => _entryIndex;
        set => SetProperty(ref _entryIndex, value);
    }

    public long Size
    {
        get => _size;
        set => SetProperty(ref _size, value);
    }

    public int Width
    {
        get => _width;
        set => SetProperty(ref _width, value);
    }

    public int Height
    {
        get => _height;
        set => SetProperty(ref _height, value);
    }

    public bool IsLandscape
    {
        get => _isLandscape;
        set => SetProperty(ref _isLandscape, value);
    }

    public BitmapSource? ThumbnailBitmap
    {
        get => _thumbnailBitmap;
        set => SetProperty(ref _thumbnailBitmap, value);
    }

    public bool IsActivePage
    {
        get => _isActivePage;
        set => SetProperty(ref _isActivePage, value);
    }
}
