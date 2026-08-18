using System.Windows;
using System.Windows.Input;
using LeeyesViewer.Services;

namespace LeeyesViewer.Views;

public partial class ShortcutsDialog : Window
{
    private readonly ThumbnailCacheService _cacheService;
    private readonly MainWindow _mainWindow;

    public ShortcutsDialog(ThumbnailCacheService cacheService, MainWindow mainWindow)
    {
        InitializeComponent();
        _cacheService = cacheService;
        _mainWindow = mainWindow;

        Loaded += (s, e) =>
        {
            SliderUiScale.Value = _mainWindow.CurrentUiScale;
            TxtUiScaleVal.Text = $"{Math.Round(_mainWindow.CurrentUiScale * 100)}%";
            ChkContinuousNav.IsChecked = _mainWindow.IsContinuousNavigationEnabled;
            RefreshCacheInfo();
        };

        KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
    }

    private void RefreshCacheInfo()
    {
        var stats = _cacheService.GetCacheStats();
        TxtCacheDir.Text = $"保存先: {stats.CacheDir}";
        TxtCacheCount.Text = $"{stats.FileCount:N0} 枚";

        double mb = stats.TotalBytes / (1024.0 * 1024.0);
        TxtCacheSize.Text = mb >= 1.0 ? $"{mb:F2} MB" : $"{stats.TotalBytes / 1024.0:F1} KB";
    }

    private void RefreshCache_Click(object sender, RoutedEventArgs e)
    {
        RefreshCacheInfo();
    }

    private void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        _cacheService.ClearCache();
        RefreshCacheInfo();
        BtnClearCache.Content = "✓ クリア完了";
    }

    private void ChkContinuousNav_Changed(object sender, RoutedEventArgs e)
    {
        if (ChkContinuousNav != null)
        {
            _mainWindow.SetContinuousNavigation(ChkContinuousNav.IsChecked == true);
        }
    }

    private void SliderUiScale_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtUiScaleVal != null)
        {
            TxtUiScaleVal.Text = $"{Math.Round(e.NewValue * 100)}%";
            _mainWindow.ApplyUiScale(e.NewValue);
        }
    }

    private void ResetScale_Click(object sender, RoutedEventArgs e)
    {
        SliderUiScale.Value = 1.0;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
