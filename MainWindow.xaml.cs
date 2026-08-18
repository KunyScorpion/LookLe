using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LeeyesViewer.Effects;
using LeeyesViewer.Models;
using LeeyesViewer.Services;
using LeeyesViewer.Views;

namespace LeeyesViewer;

public partial class MainWindow : Window
{
    private readonly ArchiveManager _archiveManager = new();
    private readonly ThumbnailCacheService _cacheService = new();
    private readonly ImagePipelineService _imagePipeline;
    private readonly SettingsService _settingsService = new();
    private readonly ColorAdjustEffect _colorEffect = new();

    private CancellationTokenSource? _dirThumbCts;
    private CancellationTokenSource? _bookshelfThumbCts;

    private AppSettings _settings = new();
    private List<ViewerPage> _pages = new();
    private int _currentIndex = 0;
    private string? _currentPath;
    private bool _isArchive;
    private string? _activeDirectory;

    private ViewMode _viewMode = ViewMode.SpreadRtl;
    private bool _coverStandalone = true;
    private FitMode _fitMode = FitMode.FitWindow;
    private ExplorerViewMode _explorerViewMode = ExplorerViewMode.Grid;
    private double _zoom = 1.0;
    private double _rotation = 0;
    private bool _isFlippedHorizontal = false;
    private bool _isFullscreen = false;
    private double _uiScale = 1.0;
    private double _lastUserSidebarWidth = 290.0;

    // Filter properties
    private bool _isGrayscale = false;
    private double _brightness = 0.0;
    private double _contrast = 1.0;

    public double CurrentUiScale => _uiScale;
    public bool IsContinuousNavigationEnabled => _settings.ContinuousNavigation;

    private string? _siblingPrev;
    private string? _siblingNext;

    private Point _panStart;
    private Point _panOrigin;
    private bool _isPanning;

    private readonly ObservableCollection<DirectoryItem> _explorerItems = new();
    private readonly ObservableCollection<FavoriteItem> _favoriteItems = new();

    public MainWindow()
    {
        InitializeComponent();
        _imagePipeline = new ImagePipelineService(_archiveManager, _cacheService);

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        PreviewMouseDown += MainWindow_PreviewMouseDown;
        Drop += MainWindow_Drop;
        SizeChanged += MainWindow_SizeChanged;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = _settingsService.LoadSettings();

        // Restore Window Size, Position, and State
        if (_settings.WindowWidth >= MinWidth) Width = _settings.WindowWidth;
        if (_settings.WindowHeight >= MinHeight) Height = _settings.WindowHeight;

        if (_settings.WindowLeft >= 0 && _settings.WindowTop >= 0)
        {
            if (_settings.WindowLeft < SystemParameters.VirtualScreenWidth - 100 &&
                _settings.WindowTop < SystemParameters.VirtualScreenHeight - 100)
            {
                Left = _settings.WindowLeft;
                Top = _settings.WindowTop;
                WindowStartupLocation = WindowStartupLocation.Manual;
            }
        }

        if (_settings.SidebarWidth >= 100 && _settings.SidebarWidth <= 800)
        {
            _lastUserSidebarWidth = _settings.SidebarWidth;
            ColSidebar.Width = new GridLength(_settings.SidebarWidth);
        }

        if (_settings.WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Maximized;
        }

        // Restore settings
        _viewMode = _settings.ViewMode;
        _coverStandalone = _settings.CoverStandalone;
        _fitMode = _settings.FitMode;
        _explorerViewMode = _settings.ExplorerMode;
        _uiScale = _settings.UiScale > 0 ? _settings.UiScale : 1.0;
        ApplyUiScale(_uiScale);
        SliderThumbSize.Value = _settings.ThumbnailSize > 0 ? _settings.ThumbnailSize : 180;

        UpdateSpreadModeButtons();
        UpdateFitModeButtons();
        ApplyExplorerViewMode();

        // Populate favorites
        foreach (var fav in _settings.Favorites)
        {
            _favoriteItems.Add(fav);
        }
        GridBookshelfItems.ItemsSource = _favoriteItems;
        ListBookshelfItems.ItemsSource = _favoriteItems;
        UpdateBookshelfUI();

        // Populate drives
        LoadDrives();

        // Default startup on 📚 Bookshelf (本棚)
        SwitchToTab(isBookshelf: true);

        // Load Bookshelf cover thumbnails in background
        RefreshBookshelfCovers();

        // Preload first drive path in background for explorer tab
        var drives = DriveInfo.GetDrives().Where(d => d.IsReady).ToList();
        if (drives.Count > 0)
        {
            LoadDirectory(drives[0].RootDirectory.FullName);
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _dirThumbCts?.Cancel();
        _bookshelfThumbCts?.Cancel();

        // Save Window Size, Position, and State
        if (WindowState == WindowState.Maximized)
        {
            _settings.WindowState = WindowState.Maximized;
            _settings.WindowWidth = RestoreBounds.Width;
            _settings.WindowHeight = RestoreBounds.Height;
            _settings.WindowLeft = RestoreBounds.Left;
            _settings.WindowTop = RestoreBounds.Top;
        }
        else
        {
            _settings.WindowState = WindowState.Normal;
            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;
            _settings.WindowLeft = Left;
            _settings.WindowTop = Top;
        }

        if (ColSidebar.Width.Value > 0)
        {
            _settings.SidebarWidth = ColSidebar.Width.Value;
        }
        else if (_lastUserSidebarWidth > 0)
        {
            _settings.SidebarWidth = _lastUserSidebarWidth;
        }

        _settings.ViewMode = _viewMode;
        _settings.CoverStandalone = _coverStandalone;
        _settings.FitMode = _fitMode;
        _settings.ExplorerMode = _explorerViewMode;
        _settings.ThumbnailSize = (int)SliderThumbSize.Value;
        _settings.UiScale = _uiScale;
        _settings.LastOpenedPath = _currentPath;
        _settings.Favorites = _favoriteItems.ToList();
        _settingsService.SaveSettings(_settings);

        _archiveManager.Dispose();
    }

    public void ApplyUiScale(double scale)
    {
        _uiScale = Math.Max(0.7, Math.Min(1.5, scale));
        UiScaleTransform.ScaleX = _uiScale;
        UiScaleTransform.ScaleY = _uiScale;
    }

    public void SetContinuousNavigation(bool enabled)
    {
        _settings.ContinuousNavigation = enabled;
    }

    private void UpdateActiveLeftPaneItem(string? activePath)
    {
        if (string.IsNullOrEmpty(activePath)) return;

        foreach (var fav in _favoriteItems)
        {
            fav.IsActiveItem = string.Equals(fav.Path, activePath, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var item in _explorerItems)
        {
            item.IsActiveItem = string.Equals(item.Path, activePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    public void NavigateToParent()
    {
        if (!string.IsNullOrEmpty(_activeDirectory))
        {
            var parent = Directory.GetParent(_activeDirectory);
            if (parent != null)
            {
                LoadDirectory(parent.FullName);
                SwitchToTab(isBookshelf: false);
                return;
            }
        }

        if (!string.IsNullOrEmpty(_currentPath))
        {
            var parent = Path.GetDirectoryName(_currentPath);
            if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
            {
                LoadDirectory(parent);
                SwitchToTab(isBookshelf: false);
            }
        }
    }

    #region Sidebar Tabs & Bookshelf Opening Logic

    private void SwitchToTab(bool isBookshelf)
    {
        if (isBookshelf)
        {
            BtnTabBookshelf.Style = (Style)FindResource("ActivePillBtn");
            BtnTabExplorer.Style = (Style)FindResource("PillBtn");
            PanelTabBookshelf.Visibility = Visibility.Visible;
            PanelTabExplorer.Visibility = Visibility.Collapsed;
        }
        else
        {
            BtnTabBookshelf.Style = (Style)FindResource("PillBtn");
            BtnTabExplorer.Style = (Style)FindResource("ActivePillBtn");
            PanelTabBookshelf.Visibility = Visibility.Collapsed;
            PanelTabExplorer.Visibility = Visibility.Visible;
        }
    }

    private void TabBookshelf_Click(object sender, RoutedEventArgs e)
    {
        SwitchToTab(isBookshelf: true);
    }

    private void TabExplorer_Click(object sender, RoutedEventArgs e)
    {
        SwitchToTab(isBookshelf: false);
    }

    private void LeftPane_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is DependencyObject dep)
        {
            var scrollViewer = FindVisualChild<ScrollViewer>(dep);
            if (scrollViewer != null)
            {
                e.Handled = true;

                // Card Grid row height: 158px + 8px margin = 166px
                // List row height: ~32px, 3 rows = 96px
                double step = (_explorerViewMode == ExplorerViewMode.Grid) ? 166.0 : 96.0;

                double targetOffset = e.Delta < 0
                    ? scrollViewer.VerticalOffset + step
                    : scrollViewer.VerticalOffset - step;

                targetOffset = Math.Max(0.0, Math.Min(scrollViewer.ScrollableHeight, targetOffset));
                scrollViewer.ScrollToVerticalOffset(targetOffset);
            }
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null) return null;
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) return typed;
            var result = FindVisualChild<T>(child);
            if (result != null) return result;
        }
        return null;
    }

    private void UpdateBookshelfUI()
    {
        TxtBookshelfCount.Text = $"({_favoriteItems.Count} 件)";
        PanelBookshelfEmpty.Visibility = _favoriteItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshBookshelfCovers()
    {
        _bookshelfThumbCts?.Cancel();
        _bookshelfThumbCts = new CancellationTokenSource();
        var ct = _bookshelfThumbCts.Token;

        Task.Run(async () =>
        {
            foreach (var fav in _favoriteItems.ToList())
            {
                if (ct.IsCancellationRequested) break;
                if (fav.CoverBitmap == null && (File.Exists(fav.Path) || Directory.Exists(fav.Path)))
                {
                    try
                    {
                        var cover = await _imagePipeline.LoadContainerCoverAsync(fav.Path, fav.IsArchive);
                        if (cover != null && !ct.IsCancellationRequested)
                        {
                            Dispatcher.Invoke(() => fav.CoverBitmap = cover);
                        }
                    }
                    catch { }
                }
            }
        }, ct);
    }

    private void BookshelfItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        if (((FrameworkElement)sender).DataContext is FavoriteItem fav)
        {
            OpenFavoriteItem(fav);
        }
    }

    private void GridBookshelfItems_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Mouse.RightButton == MouseButtonState.Pressed) return;

        if (GridBookshelfItems.SelectedItem is FavoriteItem fav)
        {
            OpenFavoriteItem(fav);
            GridBookshelfItems.SelectedItem = null;
        }
    }

    private void ListBookshelfItems_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Mouse.RightButton == MouseButtonState.Pressed) return;

        if (ListBookshelfItems.SelectedItem is FavoriteItem fav)
        {
            OpenFavoriteItem(fav);
            ListBookshelfItems.SelectedItem = null;
        }
    }

    private void OpenFavoriteItem(FavoriteItem fav)
    {
        if (string.IsNullOrEmpty(fav.Path)) return;

        bool isArch = ArchiveManager.IsArchiveExtension(Path.GetExtension(fav.Path));
        if (isArch || File.Exists(fav.Path))
        {
            OpenPath(fav.Path);
            var dir = Path.GetDirectoryName(fav.Path);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                LoadDirectory(dir);
            }
        }
        else if (Directory.Exists(fav.Path))
        {
            LoadDirectory(fav.Path);

            var dirInfo = new DirectoryInfo(fav.Path);
            var hasImages = dirInfo.EnumerateFiles().Any(f => ArchiveManager.IsSupportedImageExtension(f.Extension));

            if (hasImages)
            {
                OpenPath(fav.Path);
            }
            else
            {
                // Switch to explorer tab so the user sees the folder's volumes/ZIPs
                SwitchToTab(isBookshelf: false);

                // Auto-open first volume if present
                var firstArchive = _explorerItems.FirstOrDefault(i => i.ItemType == ItemType.Archive);
                if (firstArchive != null)
                {
                    OpenPath(firstArchive.Path);
                }
            }
        }

        UpdateActiveLeftPaneItem(fav.Path);
    }

    #endregion

    #region Drives & Directory Explorer (Left-Click Only)

    private void LoadDrives()
    {
        PanelDrives.Children.Clear();
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            var btn = new Button
            {
                Content = drive.Name.TrimEnd('\\'),
                Style = (Style)FindResource("PillBtn"),
                Margin = new Thickness(0, 0, 4, 4),
                Tag = drive.RootDirectory.FullName,
                Focusable = false
            };
            btn.Click += (s, e) => LoadDirectory((string)((Button)s).Tag);
            PanelDrives.Children.Add(btn);
        }
    }

    private void LoadDirectory(string dirPath)
    {
        if (!Directory.Exists(dirPath)) return;
        _activeDirectory = dirPath;
        TxtCurrentDirName.Text = Path.GetFileName(dirPath);
        if (string.IsNullOrEmpty(TxtCurrentDirName.Text)) TxtCurrentDirName.Text = dirPath;

        _dirThumbCts?.Cancel();
        _dirThumbCts = new CancellationTokenSource();
        var ct = _dirThumbCts.Token;

        _explorerItems.Clear();
        try
        {
            var dirInfo = new DirectoryInfo(dirPath);

            // Subdirectories
            var subDirs = dirInfo.EnumerateDirectories()
                .Where(d => !d.Attributes.HasFlag(FileAttributes.Hidden))
                .OrderBy(d => d.Name, NaturalStringComparer.Instance);

            foreach (var d in subDirs)
            {
                _explorerItems.Add(new DirectoryItem
                {
                    Name = d.Name,
                    Path = d.FullName,
                    ItemType = ItemType.Directory
                });
            }

            // Archives and Images
            var files = dirInfo.EnumerateFiles()
                .Where(f => !f.Attributes.HasFlag(FileAttributes.Hidden))
                .OrderBy(f => f.Name, NaturalStringComparer.Instance);

            int imageCount = 0;
            foreach (var f in files)
            {
                var ext = f.Extension;
                if (ArchiveManager.IsArchiveExtension(ext))
                {
                    _explorerItems.Add(new DirectoryItem
                    {
                        Name = f.Name,
                        Path = f.FullName,
                        ItemType = ItemType.Archive,
                        Size = f.Length
                    });
                }
                else if (ArchiveManager.IsSupportedImageExtension(ext))
                {
                    imageCount++;
                    _explorerItems.Add(new DirectoryItem
                    {
                        Name = f.Name,
                        Path = f.FullName,
                        ItemType = ItemType.Image,
                        Size = f.Length
                    });
                }
            }

            UpdateActiveLeftPaneItem(_currentPath);

            // Load thumbnails/covers asynchronously
            Task.Run(async () =>
            {
                foreach (var item in _explorerItems.ToList())
                {
                    if (ct.IsCancellationRequested) break;
                    try
                    {
                        if (item.ItemType == ItemType.Image)
                        {
                            var thumb = await _imagePipeline.LoadThumbnailAsync(new ViewerPage
                            {
                                Path = item.Path,
                                SourceType = "file"
                            });
                            if (thumb != null && !ct.IsCancellationRequested)
                            {
                                Dispatcher.Invoke(() => item.CoverBitmap = thumb);
                            }
                        }
                        else if (item.ItemType == ItemType.Directory)
                        {
                            var cover = await _imagePipeline.LoadContainerCoverAsync(item.Path, isArchive: false);
                            if (cover != null && !ct.IsCancellationRequested)
                            {
                                Dispatcher.Invoke(() => item.CoverBitmap = cover);
                            }
                        }
                        else if (item.ItemType == ItemType.Archive)
                        {
                            var cover = await _imagePipeline.LoadContainerCoverAsync(item.Path, isArchive: true);
                            if (cover != null && !ct.IsCancellationRequested)
                            {
                                Dispatcher.Invoke(() => item.CoverBitmap = cover);
                            }
                        }
                    }
                    catch { }
                }
            }, ct);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"フォルダ読み込みエラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        FilterExplorerItems();
    }

    private void FilterExplorerItems()
    {
        var filter = TxtSearchFilter.Text.Trim();
        var filtered = string.IsNullOrEmpty(filter)
            ? _explorerItems.ToList()
            : _explorerItems.Where(i => i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        ListExplorerItems.ItemsSource = filtered;
        GridExplorerItems.ItemsSource = filtered;
    }

    private void GoParentFolder_Click(object sender, RoutedEventArgs e)
    {
        NavigateToParent();
    }

    private void RefreshFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_activeDirectory)) LoadDirectory(_activeDirectory);
    }

    private void TxtSearchFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        FilterExplorerItems();
    }

    private void ExplorerItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        if (((FrameworkElement)sender).DataContext is DirectoryItem item)
        {
            HandleExplorerItemClick(item);
        }
    }

    private void GridExplorerItems_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Mouse.RightButton == MouseButtonState.Pressed) return;

        if (GridExplorerItems.SelectedItem is DirectoryItem item)
        {
            HandleExplorerItemClick(item);
            GridExplorerItems.SelectedItem = null;
        }
    }

    private void ListExplorerItems_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Mouse.RightButton == MouseButtonState.Pressed) return;

        if (ListExplorerItems.SelectedItem is DirectoryItem item)
        {
            HandleExplorerItemClick(item);
            ListExplorerItems.SelectedItem = null;
        }
    }

    private void HandleExplorerItemClick(DirectoryItem item)
    {
        if (item.ItemType == ItemType.Directory)
        {
            LoadDirectory(item.Path);
        }
        else if (item.ItemType == ItemType.Archive)
        {
            OpenPath(item.Path);
        }
        else if (item.ItemType == ItemType.Image)
        {
            if (_currentPath != _activeDirectory && !string.IsNullOrEmpty(_activeDirectory))
            {
                OpenPath(_activeDirectory);
            }
            var page = _pages.FirstOrDefault(p => p.Path == item.Path);
            if (page != null) GoToPage(page.Index);
        }

        UpdateActiveLeftPaneItem(item.Path);
    }

    #endregion

    #region Viewer Core Logic (Dynamic Landscape & Dual Spread Rendering)

    public async void OpenPath(string path)
    {
        try
        {
            _imagePipeline.ClearMemoryCache();
            _isArchive = ArchiveManager.IsArchiveExtension(Path.GetExtension(path));
            _currentPath = path;

            if (_isArchive)
            {
                _pages = _archiveManager.ListArchiveEntries(path);
                TxtStatusType.Text = Path.GetExtension(path).ToUpperInvariant().TrimStart('.') + " 書庫";
            }
            else if (Directory.Exists(path))
            {
                var dirInfo = new DirectoryInfo(path);
                var imageFiles = dirInfo.EnumerateFiles()
                    .Where(f => ArchiveManager.IsSupportedImageExtension(f.Extension))
                    .OrderBy(f => f.Name, NaturalStringComparer.Instance)
                    .ToList();

                _pages = imageFiles.Select((f, idx) => new ViewerPage
                {
                    Index = idx,
                    Name = f.Name,
                    SourceType = "file",
                    Path = f.FullName,
                    Size = f.Length
                }).ToList();

                TxtStatusType.Text = "フォルダ";
            }

            _currentIndex = 0;
            _zoom = 1.0;
            _rotation = 0;
            _isFlippedHorizontal = false;

            // UI updates
            var titleName = Path.GetFileName(path);
            TxtTitleBadge.Text = titleName;
            BorderTitleBadge.Visibility = Visibility.Visible;
            PanelEmptyGuide.Visibility = _pages.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            TxtStatusPath.Text = path;

            UpdateFavoriteStar();
            UpdateSiblingNavigation();
            UpdateActiveLeftPaneItem(path);
            ItemsFilmstrip.ItemsSource = _pages;

            // Render first spread
            await RenderCurrentSpreadAsync();

            // Load thumbnails asynchronously and update UI via INotifyPropertyChanged
            _ = Task.Run(async () =>
            {
                foreach (var p in _pages.ToList())
                {
                    var thumb = await _imagePipeline.LoadThumbnailAsync(p);
                    if (thumb != null)
                    {
                        Dispatcher.Invoke(() => p.ThumbnailBitmap = thumb);
                    }
                }
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"読み込みエラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RenderCurrentSpreadAsync()
    {
        if (_pages.Count == 0 || _currentIndex < 0 || _currentIndex >= _pages.Count)
        {
            ImgSingle.Source = null;
            ImgLeft.Source = null;
            ImgRight.Source = null;
            ImgSingle.Visibility = Visibility.Collapsed;
            ImgLeft.Visibility = Visibility.Collapsed;
            ImgRight.Visibility = Visibility.Collapsed;
            return;
        }

        var currentPage = _pages[_currentIndex];

        // 1. Load current page bitmap
        var bmpCurrent = await _imagePipeline.LoadFullImageAsync(currentPage);
        currentPage.Width = bmpCurrent.PixelWidth;
        currentPage.Height = bmpCurrent.PixelHeight;
        currentPage.IsLandscape = bmpCurrent.PixelWidth > bmpCurrent.PixelHeight;

        // Determine if current page must be displayed standalone
        bool isSingle = (_viewMode == ViewMode.Single)
                        || (_currentIndex == 0 && _coverStandalone)
                        || currentPage.IsLandscape
                        || (_currentIndex + 1 >= _pages.Count);

        ViewerPage? nextPage = null;
        BitmapSource? bmpNext = null;

        if (!isSingle)
        {
            nextPage = _pages[_currentIndex + 1];
            bmpNext = await _imagePipeline.LoadFullImageAsync(nextPage);
            nextPage.Width = bmpNext.PixelWidth;
            nextPage.Height = bmpNext.PixelHeight;
            nextPage.IsLandscape = bmpNext.PixelWidth > bmpNext.PixelHeight;

            if (nextPage.IsLandscape)
            {
                // Next page is landscape -> current page must be shown alone!
                isSingle = true;
                nextPage = null;
                bmpNext = null;
            }
        }

        // Clear all active highlight states first
        foreach (var p in _pages)
        {
            p.IsActivePage = false;
        }

        if (isSingle || nextPage == null || bmpNext == null)
        {
            // SINGLE / LANDSCAPE DISPLAY (Clean isolation - no neighboring images)
            currentPage.IsActivePage = true;

            ImgLeft.Visibility = Visibility.Collapsed;
            ImgRight.Visibility = Visibility.Collapsed;
            ImgLeft.Source = null;
            ImgRight.Source = null;

            ImgSingle.Visibility = Visibility.Visible;
            ImgSingle.Source = bmpCurrent;

            TxtStatusResolution.Text = $"{bmpCurrent.PixelWidth} × {bmpCurrent.PixelHeight} px";
            TxtCurrentPageNum.Text = (_currentIndex + 1).ToString();
            TxtStatusPages.Text = $"{_currentIndex + 1} / {_pages.Count}";
        }
        else
        {
            // DUAL SPREAD DISPLAY
            currentPage.IsActivePage = true;
            nextPage.IsActivePage = true;

            ImgSingle.Visibility = Visibility.Collapsed;
            ImgSingle.Source = null;

            ImgLeft.Visibility = Visibility.Visible;
            ImgRight.Visibility = Visibility.Visible;

            if (_viewMode == ViewMode.SpreadRtl)
            {
                // Manga RTL: current on Right, next on Left
                ImgRight.Source = bmpCurrent;
                ImgLeft.Source = bmpNext;
            }
            else
            {
                // Comic LTR: current on Left, next on Right
                ImgLeft.Source = bmpCurrent;
                ImgRight.Source = bmpNext;
            }

            TxtStatusResolution.Text = $"{bmpCurrent.PixelWidth + bmpNext.PixelWidth} × {Math.Max(bmpCurrent.PixelHeight, bmpNext.PixelHeight)} px";
            TxtCurrentPageNum.Text = $"{_currentIndex + 1}-{_currentIndex + 2}";
            TxtStatusPages.Text = $"{_currentIndex + 1}-{_currentIndex + 2} / {_pages.Count}";
        }

        TxtTotalPagesNum.Text = _pages.Count.ToString();

        ApplyFitMode();
        ApplyTransform();
        ApplyColorAdjustments();

        // Multi-page prefetch ahead (+6) and behind (-2)
        _imagePipeline.PrefetchPages(_pages, _currentIndex);

        // Auto-scroll thumbnail filmstrip to center the active thumbnail(s)
        if (ScrollFilmstrip != null && _pages.Count > 0)
        {
            double cardWidth = Math.Max(70.0, Math.Round(SliderThumbSize.Value * 0.68)) + 8;
            double targetOffset = (_currentIndex * cardWidth) - (ScrollFilmstrip.ActualWidth / 2) + (cardWidth / 2);
            ScrollFilmstrip.ScrollToHorizontalOffset(Math.Max(0, targetOffset));
        }
    }

    private void ApplyFitMode()
    {
        double viewportW = GridMainViewport.ActualWidth;
        double viewportH = GridMainViewport.ActualHeight;
        if (viewportW <= 0 || viewportH <= 0) return;

        bool isDual = ImgLeft.Visibility == Visibility.Visible && ImgRight.Visibility == Visibility.Visible;

        double targetMaxW = isDual ? (viewportW / 2.0) : viewportW;
        double targetMaxH = viewportH;

        // When rotated 90° or 270°, swap constraints so transformed layout box fits viewport
        if (_rotation == 90 || _rotation == 270)
        {
            double temp = targetMaxW;
            targetMaxW = targetMaxH;
            targetMaxH = temp;
        }

        switch (_fitMode)
        {
            case FitMode.FitWindow:
                ImgSingle.MaxWidth = targetMaxW;
                ImgSingle.MaxHeight = targetMaxH;
                ImgLeft.MaxWidth = targetMaxW;
                ImgLeft.MaxHeight = targetMaxH;
                ImgRight.MaxWidth = targetMaxW;
                ImgRight.MaxHeight = targetMaxH;
                break;
            case FitMode.FitWidth:
                ImgSingle.MaxWidth = targetMaxW;
                ImgSingle.MaxHeight = double.PositiveInfinity;
                ImgLeft.MaxWidth = targetMaxW;
                ImgLeft.MaxHeight = double.PositiveInfinity;
                ImgRight.MaxWidth = targetMaxW;
                ImgRight.MaxHeight = double.PositiveInfinity;
                break;
            case FitMode.FitHeight:
                ImgSingle.MaxWidth = double.PositiveInfinity;
                ImgSingle.MaxHeight = targetMaxH;
                ImgLeft.MaxWidth = double.PositiveInfinity;
                ImgLeft.MaxHeight = targetMaxH;
                ImgRight.MaxWidth = double.PositiveInfinity;
                ImgRight.MaxHeight = targetMaxH;
                break;
            case FitMode.Original:
                ImgSingle.MaxWidth = double.PositiveInfinity;
                ImgSingle.MaxHeight = double.PositiveInfinity;
                ImgLeft.MaxWidth = double.PositiveInfinity;
                ImgLeft.MaxHeight = double.PositiveInfinity;
                ImgRight.MaxWidth = double.PositiveInfinity;
                ImgRight.MaxHeight = double.PositiveInfinity;
                break;
        }
    }

    private void ApplyTransform()
    {
        if (TransformScale == null) return;

        TransformScale.ScaleX = _zoom;
        TransformScale.ScaleY = _zoom;

        double scaleX = _isFlippedHorizontal ? -1.0 : 1.0;

        // Apply per-image center-axis rotation and horizontal flip
        if (RotateImgSingle != null) RotateImgSingle.Angle = _rotation;
        if (RotateImgLeft != null) RotateImgLeft.Angle = _rotation;
        if (RotateImgRight != null) RotateImgRight.Angle = _rotation;

        if (FlipImgSingle != null) FlipImgSingle.ScaleX = scaleX;
        if (FlipImgLeft != null) FlipImgLeft.ScaleX = scaleX;
        if (FlipImgRight != null) FlipImgRight.ScaleX = scaleX;

        if (BtnFlipHorizontal != null)
        {
            BtnFlipHorizontal.Style = (Style)FindResource(_isFlippedHorizontal ? "ActivePillBtn" : "ToolbarBtn");
        }

        TxtStatusZoom.Text = $"{Math.Round(_zoom * 100)}%";
        BtnZoomLevel.Content = $"{Math.Round(_zoom * 100)}%";
    }

    public void ToggleFlipHorizontal()
    {
        _isFlippedHorizontal = !_isFlippedHorizontal;
        ApplyTransform();
    }

    public void ToggleGrayscale()
    {
        _isGrayscale = !_isGrayscale;
        if (ChkGrayscale != null) ChkGrayscale.IsChecked = _isGrayscale;
        ApplyColorAdjustments();
    }

    private void ApplyColorAdjustments()
    {
        if (ContainerImages == null) return;

        if (!_isGrayscale && Math.Abs(_brightness) < 0.001 && Math.Abs(_contrast - 1.0) < 0.001)
        {
            ContainerImages.Effect = null;
            if (BtnFilterPopup != null) BtnFilterPopup.Style = (Style)FindResource("ToolbarBtn");
        }
        else
        {
            _colorEffect.Grayscale = _isGrayscale ? 1.0 : 0.0;
            _colorEffect.Brightness = _brightness;
            _colorEffect.Contrast = _contrast;
            ContainerImages.Effect = _colorEffect;
            if (BtnFilterPopup != null) BtnFilterPopup.Style = (Style)FindResource("ActivePillBtn");
        }
    }

    private void FilterControl_Changed(object sender, RoutedEventArgs e)
    {
        if (ChkGrayscale == null || SliderBrightness == null || SliderContrast == null) return;

        _isGrayscale = ChkGrayscale.IsChecked == true;
        _brightness = Math.Round(SliderBrightness.Value, 2);
        _contrast = Math.Round(SliderContrast.Value, 2);

        if (TxtBrightnessVal != null) TxtBrightnessVal.Text = $"{_brightness:+0.00;-0.00;0}";
        if (TxtContrastVal != null) TxtContrastVal.Text = $"{_contrast:F1}";

        ApplyColorAdjustments();
    }

    private void ResetFilter_Click(object sender, RoutedEventArgs e)
    {
        _isGrayscale = false;
        _brightness = 0.0;
        _contrast = 1.0;

        if (ChkGrayscale != null) ChkGrayscale.IsChecked = false;
        if (SliderBrightness != null) SliderBrightness.Value = 0.0;
        if (SliderContrast != null) SliderContrast.Value = 1.0;

        ApplyColorAdjustments();
    }

    private void ToggleFilterPopup_Click(object sender, RoutedEventArgs e)
    {
        PopupFilter.IsOpen = !PopupFilter.IsOpen;
    }

    public async void GoToPage(int index)
    {
        if (_pages.Count == 0) return;
        _currentIndex = Math.Max(0, Math.Min(_pages.Count - 1, index));
        TransformTranslate.X = 0;
        TransformTranslate.Y = 0;
        await RenderCurrentSpreadAsync();
    }

    public void StepSinglePage(int delta)
    {
        if (_pages.Count == 0) return;
        int target = _currentIndex + delta;
        if (target >= 0 && target < _pages.Count)
        {
            GoToPage(target);
        }
    }

    public void ShiftSpreadOffset()
    {
        if (_pages.Count == 0) return;
        if (_currentIndex > 0)
        {
            StepSinglePage(-1);
        }
        else
        {
            StepSinglePage(1);
        }
    }

    public async void NextPage()
    {
        if (_pages.Count == 0) return;
        int next = SpreadCalculator.GetNextIndex(_pages, _currentIndex, _viewMode, _coverStandalone);

        if (next == _currentIndex && _currentIndex >= _pages.Count - 1)
        {
            // Only move to next sibling if ContinuousNavigation is enabled
            if (_settings.ContinuousNavigation && !string.IsNullOrEmpty(_siblingNext))
            {
                OpenPath(_siblingNext);
            }
        }
        else
        {
            _currentIndex = next;
            TransformTranslate.X = 0;
            TransformTranslate.Y = 0;
            await RenderCurrentSpreadAsync();
        }
    }

    public async void PrevPage()
    {
        if (_pages.Count == 0) return;
        int prev = SpreadCalculator.GetPrevIndex(_pages, _currentIndex, _viewMode, _coverStandalone);

        if (prev == _currentIndex && _currentIndex <= 0)
        {
            // Only move to prev sibling if ContinuousNavigation is enabled
            if (_settings.ContinuousNavigation && !string.IsNullOrEmpty(_siblingPrev))
            {
                OpenPath(_siblingPrev);
            }
        }
        else
        {
            _currentIndex = prev;
            TransformTranslate.X = 0;
            TransformTranslate.Y = 0;
            await RenderCurrentSpreadAsync();
        }
    }

    public void FirstPage() => GoToPage(0);
    public void LastPage() => GoToPage(_pages.Count - 1);

    #endregion

    #region Sibling & Isolated Favorites Click Handling

    private void UpdateSiblingNavigation()
    {
        _siblingPrev = null;
        _siblingNext = null;

        if (string.IsNullOrEmpty(_currentPath)) return;

        var parent = Path.GetDirectoryName(_currentPath);
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent)) return;

        var dirInfo = new DirectoryInfo(parent);
        List<string> siblings;

        if (_isArchive)
        {
            siblings = dirInfo.EnumerateFiles()
                .Where(f => ArchiveManager.IsArchiveExtension(f.Extension))
                .OrderBy(f => f.Name, NaturalStringComparer.Instance)
                .Select(f => f.FullName)
                .ToList();
        }
        else
        {
            siblings = dirInfo.EnumerateDirectories()
                .Where(d => !d.Attributes.HasFlag(FileAttributes.Hidden))
                .OrderBy(d => d.Name, NaturalStringComparer.Instance)
                .Select(d => d.FullName)
                .ToList();
        }

        int idx = siblings.IndexOf(_currentPath);
        if (idx >= 0)
        {
            if (idx > 0) _siblingPrev = siblings[idx - 1];
            if (idx < siblings.Count - 1) _siblingNext = siblings[idx + 1];
        }

        BtnPrevSibling.IsEnabled = !string.IsNullOrEmpty(_siblingPrev);
        BtnNextSibling.IsEnabled = !string.IsNullOrEmpty(_siblingNext);
    }

    public void NextSibling()
    {
        if (!string.IsNullOrEmpty(_siblingNext)) OpenPath(_siblingNext);
    }

    public void PrevSibling()
    {
        if (!string.IsNullOrEmpty(_siblingPrev)) OpenPath(_siblingPrev);
    }

    private void UpdateFavoriteStar()
    {
        bool isFav = !string.IsNullOrEmpty(_currentPath) && _favoriteItems.Any(f => f.Path == _currentPath);
        BtnFavActive.Foreground = isFav ? new SolidColorBrush(Color.FromRgb(234, 179, 8)) : new SolidColorBrush(Color.FromRgb(107, 114, 128));
    }

    private void ToggleCurrentFavorite_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (string.IsNullOrEmpty(_currentPath)) return;
        ToggleFavoritePath(_currentPath, _isArchive);
    }

    private void ContextToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is MenuItem menuItem && menuItem.DataContext is DirectoryItem item)
        {
            ToggleFavoritePath(item.Path, item.ItemType == ItemType.Archive);
        }
    }

    private void ToggleFavoritePath(string path, bool isArchive)
    {
        var existing = _favoriteItems.FirstOrDefault(f => f.Path == path);
        if (existing != null)
        {
            _favoriteItems.Remove(existing);
        }
        else
        {
            var fav = new FavoriteItem
            {
                Path = path,
                Name = Path.GetFileName(path),
                IsArchive = isArchive
            };
            _favoriteItems.Insert(0, fav);

            // Fetch cover for new favorite
            Task.Run(async () =>
            {
                var cover = await _imagePipeline.LoadContainerCoverAsync(path, isArchive);
                if (cover != null)
                {
                    Dispatcher.Invoke(() => fav.CoverBitmap = cover);
                }
            });
        }

        UpdateBookshelfUI();
        UpdateFavoriteStar();
        UpdateActiveLeftPaneItem(_currentPath);
    }

    private void RemoveFavorite_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is MenuItem menuItem && menuItem.DataContext is FavoriteItem fav)
        {
            _favoriteItems.Remove(fav);
            UpdateBookshelfUI();
            UpdateFavoriteStar();
        }
        else if (((FrameworkElement)sender).DataContext is FavoriteItem fav2)
        {
            _favoriteItems.Remove(fav2);
            UpdateBookshelfUI();
            UpdateFavoriteStar();
        }
    }

    #endregion

    #region UI Event Handlers & View Modes

    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        if (ColSidebar.Width.Value > 0)
        {
            _lastUserSidebarWidth = ColSidebar.Width.Value;
            ColSidebar.Width = new GridLength(0);
            ColSplitter.Width = new GridLength(0);
        }
        else
        {
            double restoreW = _lastUserSidebarWidth >= 100 ? _lastUserSidebarWidth : 290.0;
            ColSidebar.Width = new GridLength(restoreW);
            ColSplitter.Width = new GridLength(4);
        }
    }

    private void ToggleFilmstrip_Click(object sender, RoutedEventArgs e)
    {
        BorderFilmstrip.Visibility = BorderFilmstrip.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ToggleFullscreen_Click(object sender, RoutedEventArgs e)
    {
        _isFullscreen = !_isFullscreen;
        if (_isFullscreen)
        {
            if (ColSidebar.Width.Value > 0)
            {
                _lastUserSidebarWidth = ColSidebar.Width.Value;
            }
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            ToolbarBorder.Visibility = Visibility.Collapsed;
            BorderFilmstrip.Visibility = Visibility.Collapsed;
            ColSidebar.Width = new GridLength(0);
            ColSplitter.Width = new GridLength(0);
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = WindowState.Normal;
            ToolbarBorder.Visibility = Visibility.Visible;
            BorderFilmstrip.Visibility = Visibility.Visible;
            double restoreW = _lastUserSidebarWidth >= 100 ? _lastUserSidebarWidth : 290.0;
            ColSidebar.Width = new GridLength(restoreW);
            ColSplitter.Width = new GridLength(4);
        }
    }

    private void SliderThumbSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtThumbSizeVal != null)
        {
            TxtThumbSizeVal.Text = $"{(int)e.NewValue}px";
        }
    }

    private void ExplorerList_Click(object sender, RoutedEventArgs e)
    {
        _explorerViewMode = ExplorerViewMode.List;
        ApplyExplorerViewMode();
    }

    private void ExplorerGrid_Click(object sender, RoutedEventArgs e)
    {
        _explorerViewMode = ExplorerViewMode.Grid;
        ApplyExplorerViewMode();
    }

    private void ApplyExplorerViewMode()
    {
        bool isGrid = _explorerViewMode == ExplorerViewMode.Grid;

        BtnExplorerList.Style = (Style)FindResource(isGrid ? "PillBtn" : "ActivePillBtn");
        BtnExplorerGrid.Style = (Style)FindResource(isGrid ? "ActivePillBtn" : "PillBtn");

        ListExplorerItems.Visibility = isGrid ? Visibility.Collapsed : Visibility.Visible;
        GridExplorerItems.Visibility = isGrid ? Visibility.Visible : Visibility.Collapsed;

        ListBookshelfItems.Visibility = isGrid ? Visibility.Collapsed : Visibility.Visible;
        GridBookshelfItems.Visibility = isGrid ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void ModeSingle_Click(object sender, RoutedEventArgs e)
    {
        _viewMode = ViewMode.Single;
        UpdateSpreadModeButtons();
        await RenderCurrentSpreadAsync();
    }

    private async void ModeSpreadRtl_Click(object sender, RoutedEventArgs e)
    {
        _viewMode = ViewMode.SpreadRtl;
        UpdateSpreadModeButtons();
        await RenderCurrentSpreadAsync();
    }

    private async void ModeSpreadLtr_Click(object sender, RoutedEventArgs e)
    {
        _viewMode = ViewMode.SpreadLtr;
        UpdateSpreadModeButtons();
        await RenderCurrentSpreadAsync();
    }

    private async void ToggleCoverStandalone_Click(object sender, RoutedEventArgs e)
    {
        _coverStandalone = !_coverStandalone;
        BtnCoverStandalone.Style = (Style)FindResource(_coverStandalone ? "ActivePillBtn" : "PillBtn");
        await RenderCurrentSpreadAsync();
    }

    private void ShiftSpread_Click(object sender, RoutedEventArgs e)
    {
        ShiftSpreadOffset();
    }

    private void UpdateSpreadModeButtons()
    {
        BtnModeSingle.Style = (Style)FindResource(_viewMode == ViewMode.Single ? "ActivePillBtn" : "PillBtn");
        BtnModeSpreadRtl.Style = (Style)FindResource(_viewMode == ViewMode.SpreadRtl ? "ActivePillBtn" : "PillBtn");
        BtnModeSpreadLtr.Style = (Style)FindResource(_viewMode == ViewMode.SpreadLtr ? "ActivePillBtn" : "PillBtn");
        BtnCoverStandalone.Style = (Style)FindResource(_coverStandalone ? "ActivePillBtn" : "PillBtn");

        TxtStatusMode.Text = _viewMode switch
        {
            ViewMode.SpreadRtl => "見開き (右開き)",
            ViewMode.SpreadLtr => "見開き (左開き)",
            _ => "単ページ"
        };

        // Update Filmstrip FlowDirection: RTL for Manga Spread RTL, LTR for others
        if (ScrollFilmstrip != null)
        {
            ScrollFilmstrip.FlowDirection = _viewMode == ViewMode.SpreadRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        }
    }

    private void FitWindow_Click(object sender, RoutedEventArgs e) { _fitMode = FitMode.FitWindow; UpdateFitModeButtons(); ApplyFitMode(); }
    private void FitWidth_Click(object sender, RoutedEventArgs e) { _fitMode = FitMode.FitWidth; UpdateFitModeButtons(); ApplyFitMode(); }
    private void FitHeight_Click(object sender, RoutedEventArgs e) { _fitMode = FitMode.FitHeight; UpdateFitModeButtons(); ApplyFitMode(); }
    private void FitOriginal_Click(object sender, RoutedEventArgs e) { _fitMode = FitMode.Original; UpdateFitModeButtons(); ApplyFitMode(); }

    private void UpdateFitModeButtons()
    {
        BtnFitWindow.Style = (Style)FindResource(_fitMode == FitMode.FitWindow ? "ActivePillBtn" : "PillBtn");
        BtnFitWidth.Style = (Style)FindResource(_fitMode == FitMode.FitWidth ? "ActivePillBtn" : "PillBtn");
        BtnFitHeight.Style = (Style)FindResource(_fitMode == FitMode.FitHeight ? "ActivePillBtn" : "PillBtn");
        BtnFitOriginal.Style = (Style)FindResource(_fitMode == FitMode.Original ? "ActivePillBtn" : "PillBtn");
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) { _zoom = Math.Min(5.0, Math.Round((_zoom + 0.1) * 10) / 10); ApplyTransform(); }
    private void ZoomOut_Click(object sender, RoutedEventArgs e) { _zoom = Math.Max(0.2, Math.Round((_zoom - 0.1) * 10) / 10); ApplyTransform(); }
    private void ZoomReset_Click(object sender, RoutedEventArgs e) { _zoom = 1.0; TransformTranslate.X = 0; TransformTranslate.Y = 0; ApplyTransform(); }
    
    private void Rotate_Click(object sender, RoutedEventArgs e)
    {
        _rotation = (_rotation + 90) % 360;
        ApplyFitMode();
        ApplyTransform();
    }

    private void FlipHorizontal_Click(object sender, RoutedEventArgs e)
    {
        ToggleFlipHorizontal();
    }

    private void OpenShortcuts_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ShortcutsDialog(_cacheService, this) { Owner = this };
        dlg.ShowDialog();
    }

    private void FirstPage_Click(object sender, RoutedEventArgs e) => FirstPage();
    private void PrevPage_Click(object sender, RoutedEventArgs e) => PrevPage();
    private void NextPage_Click(object sender, RoutedEventArgs e) => NextPage();
    private void LastPage_Click(object sender, RoutedEventArgs e) => LastPage();
    private void PrevSibling_Click(object sender, RoutedEventArgs e) => PrevSibling();
    private void NextSibling_Click(object sender, RoutedEventArgs e) => NextSibling();

    private void ThumbCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is ViewerPage page)
        {
            GoToPage(page.Index);
        }
    }

    #endregion

    #region Mouse & Global Keyboard Navigation (PreviewKeyDown with RTL Key Mappings & Back Navigation)

    private void MainWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.XButton1)
        {
            e.Handled = true;
            NavigateToParent();
        }
        else if (e.ChangedButton == MouseButton.XButton2)
        {
            e.Handled = true;
            NextSibling();
        }
    }

    private void ZoneLeft_Click(object sender, MouseButtonEventArgs e)
    {
        // Left zone: advance reading in RTL Manga
        NextPage();
    }

    private void ZoneRight_Click(object sender, MouseButtonEventArgs e)
    {
        // Right zone: turn back in RTL Manga
        PrevPage();
    }

    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            double delta = e.Delta > 0 ? 0.1 : -0.1;
            _zoom = Math.Max(0.2, Math.Min(5.0, Math.Round((_zoom + delta) * 10) / 10));
            ApplyTransform();
        }
        else if (_zoom <= 1.05)
        {
            if (e.Delta > 0) PrevPage();
            else NextPage();
        }
    }

    private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.RightButton == MouseButtonState.Pressed || e.MiddleButton == MouseButtonState.Pressed || (_zoom > 1.05 && e.LeftButton == MouseButtonState.Pressed))
        {
            _isPanning = true;
            _panStart = e.GetPosition(this);
            _panOrigin = new Point(TransformTranslate.X, TransformTranslate.Y);
            Mouse.Capture(GridMainViewport);
        }
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isPanning)
        {
            var cur = e.GetPosition(this);
            TransformTranslate.X = _panOrigin.X + (cur.X - _panStart.X);
            TransformTranslate.Y = _panOrigin.Y + (cur.Y - _panStart.Y);
        }
    }

    private void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            Mouse.Capture(null);
        }
    }

    private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleFullscreen_Click(this, new RoutedEventArgs());
        }
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // If search filter is focused, allow typing characters, but handle Escape/F-keys
        if (TxtSearchFilter.IsFocused)
        {
            if (e.Key == Key.Escape)
            {
                Keyboard.ClearFocus();
                e.Handled = true;
            }
            else if (e.Key == Key.F1 || e.Key == Key.F3 || e.Key == Key.F4 || e.Key == Key.F11)
            {
                // Let F-keys pass through
            }
            else
            {
                return;
            }
        }

        switch (e.Key)
        {
            case Key.Back: // Backspace: 1つ上の親階層へ戻る
                e.Handled = true;
                NavigateToParent();
                break;
            case Key.Left: // ← 左キー: 次のページへ進む (日本の漫画・RTL基準)
            case Key.PageDown:
                e.Handled = true;
                NextPage();
                break;
            case Key.Right: // → 右キー: 前のページへ戻る (日本の漫画・RTL基準)
            case Key.PageUp:
                e.Handled = true;
                PrevPage();
                break;
            case Key.Up: // ↑ 上キー: 1ページ戻す (見開きずれ調整)
                e.Handled = true;
                StepSinglePage(-1);
                break;
            case Key.Down: // ↓ 下キー: 1ページ進める (見開きずれ調整)
                e.Handled = true;
                StepSinglePage(1);
                break;
            case Key.O:
                e.Handled = true;
                ShiftSpreadOffset();
                break;
            case Key.H:
                e.Handled = true;
                ToggleFlipHorizontal();
                break;
            case Key.G: // Gキー: モノクロ化 (グレースケール) トグル
                e.Handled = true;
                ToggleGrayscale();
                break;
            case Key.Space:
                e.Handled = true;
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) PrevPage();
                else NextPage();
                break;
            case Key.Home:
                e.Handled = true;
                FirstPage();
                break;
            case Key.End:
                e.Handled = true;
                LastPage();
                break;
            case Key.OemCloseBrackets: // ]
                e.Handled = true;
                NextSibling();
                break;
            case Key.OemOpenBrackets: // [
                e.Handled = true;
                PrevSibling();
                break;
            case Key.M:
                e.Handled = true;
                _viewMode = _viewMode == ViewMode.SpreadRtl ? ViewMode.SpreadLtr : _viewMode == ViewMode.SpreadLtr ? ViewMode.Single : ViewMode.SpreadRtl;
                UpdateSpreadModeButtons();
                _ = RenderCurrentSpreadAsync();
                break;
            case Key.C:
                e.Handled = true;
                _coverStandalone = !_coverStandalone;
                BtnCoverStandalone.Style = (Style)FindResource(_coverStandalone ? "ActivePillBtn" : "PillBtn");
                _ = RenderCurrentSpreadAsync();
                break;
            case Key.D1:
                e.Handled = true;
                FitWindow_Click(this, new RoutedEventArgs());
                break;
            case Key.D2:
                e.Handled = true;
                FitWidth_Click(this, new RoutedEventArgs());
                break;
            case Key.D3:
                e.Handled = true;
                FitHeight_Click(this, new RoutedEventArgs());
                break;
            case Key.D4:
                e.Handled = true;
                FitOriginal_Click(this, new RoutedEventArgs());
                break;
            case Key.OemPlus:
            case Key.Add:
                e.Handled = true;
                ZoomIn_Click(this, new RoutedEventArgs());
                break;
            case Key.OemMinus:
            case Key.Subtract:
                e.Handled = true;
                ZoomOut_Click(this, new RoutedEventArgs());
                break;
            case Key.D0:
                e.Handled = true;
                ZoomReset_Click(this, new RoutedEventArgs());
                break;
            case Key.R:
                e.Handled = true;
                _rotation = (_rotation + 90) % 360;
                ApplyFitMode();
                ApplyTransform();
                break;
            case Key.F3:
                e.Handled = true;
                ToggleFilmstrip_Click(this, new RoutedEventArgs());
                break;
            case Key.F4:
                e.Handled = true;
                ToggleSidebar_Click(this, new RoutedEventArgs());
                break;
            case Key.F11:
            case Key.Enter:
                e.Handled = true;
                ToggleFullscreen_Click(this, new RoutedEventArgs());
                break;
            case Key.F1:
                e.Handled = true;
                OpenShortcuts_Click(this, new RoutedEventArgs());
                break;
            case Key.Escape:
                if (PopupFilter.IsOpen)
                {
                    PopupFilter.IsOpen = false;
                    e.Handled = true;
                }
                else if (_isFullscreen)
                {
                    e.Handled = true;
                    ToggleFullscreen_Click(this, new RoutedEventArgs());
                }
                break;
        }
    }

    private void MainWindow_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                OpenPath(files[0]);
            }
        }
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ColSidebar.Width.Value >= 100)
        {
            _lastUserSidebarWidth = ColSidebar.Width.Value;
        }
        ApplyFitMode();
    }

    #endregion
}
