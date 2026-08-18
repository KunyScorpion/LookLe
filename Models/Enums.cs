namespace LeeyesViewer.Models;

public enum ViewMode
{
    Single,
    SpreadRtl, // Manga Right-to-Left
    SpreadLtr  // Western Comic Left-to-Right
}

public enum FitMode
{
    FitWindow,
    FitWidth,
    FitHeight,
    Original
}

public enum ExplorerViewMode
{
    List,
    Grid
}

public enum ItemType
{
    Directory,
    Archive,
    Image
}
