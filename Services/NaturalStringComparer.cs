using System.Runtime.InteropServices;

namespace LeeyesViewer.Services;

public class NaturalStringComparer : IComparer<string>
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StrCmpLogicalW(string psz1, string psz2);

    public static readonly NaturalStringComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (x == null && y == null) return 0;
        if (x == null) return -1;
        if (y == null) return 1;
        return StrCmpLogicalW(x, y);
    }
}
