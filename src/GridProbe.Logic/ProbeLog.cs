namespace GridProbe;

internal static class ProbeLog
{
    public const string OutDir = @"D:\Projects\Space Engineers Stuff\Grid Schematics 2\output";
    private static readonly object Gate = new();
    private static readonly string LogFile;

    static ProbeLog()
    {
        Directory.CreateDirectory(OutDir);
        LogFile = Path.Combine(OutDir, "probe.log");
    }

    public static void Line(string msg)
    {
        try
        {
            lock (Gate)
                File.AppendAllText(LogFile, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}");
        }
        catch { }
    }

    public static void Error(string context, Exception e) => Line($"ERROR {context}: {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
}
