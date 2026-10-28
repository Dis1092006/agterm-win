using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AgTerm.App;

internal static class InstalledFonts
{
    public static IReadOnlyList<string> MonospacedFamilies()
    {
        var all = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        var monospaced = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        var dc = GetDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var font = new LogFont { CharacterSet = 1, FaceName = "" };
            int Collect(IntPtr logFont, IntPtr textMetric, uint type, IntPtr parameter)
            {
                var name = Marshal.PtrToStructure<LogFont>(logFont).FaceName;
                if (!string.IsNullOrWhiteSpace(name) && !name.StartsWith('@'))
                {
                    all.Add(name);
                    // GDI's TMPF_FIXED_PITCH bit is set for variable-pitch fonts.
                    if ((Marshal.PtrToStructure<TextMetric>(textMetric).PitchAndFamily & 1) == 0)
                        monospaced.Add(name);
                }
                return 1;
            }
            if (EnumFontFamiliesEx(dc, ref font, Collect, IntPtr.Zero, 0) == 0 || all.Count == 0)
                throw new InvalidOperationException("Windows could not enumerate installed font families.");
        }
        finally
        {
            if (ReleaseDC(IntPtr.Zero, dc) == 0)
                System.Diagnostics.Trace.TraceError("Windows could not release the font enumeration device context.");
        }
        if (monospaced.Count > 0) return monospaced.ToArray();
        System.Diagnostics.Trace.TraceWarning("Windows reported no monospaced fonts; showing all installed families.");
        return all.ToArray();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LogFont
    {
        public int Height, Width, Escapement, Orientation, Weight;
        public byte Italic, Underline, StrikeOut, CharacterSet, OutPrecision, ClipPrecision, Quality, PitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FaceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TextMetric
    {
        public int Height, Ascent, Descent, InternalLeading, ExternalLeading, AverageWidth, MaximumWidth,
            Weight, Overhang, DigitizedAspectX, DigitizedAspectY;
        public ushort FirstChar, LastChar, DefaultChar, BreakChar;
        public byte Italic, Underlined, StruckOut, PitchAndFamily, CharacterSet;
    }

    private delegate int EnumFontCallback(IntPtr logFont, IntPtr textMetric, uint type, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll", EntryPoint = "EnumFontFamiliesExW", CharSet = CharSet.Unicode)]
    private static extern int EnumFontFamiliesEx(IntPtr dc, ref LogFont font, EnumFontCallback callback,
        IntPtr parameter, uint flags);
}
