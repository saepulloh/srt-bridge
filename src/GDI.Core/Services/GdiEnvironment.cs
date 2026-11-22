namespace GDI.Core.Services;

/// <summary>
/// Centralized cross-platform directory and environment resolver for Windows and Linux.
/// </summary>
public static class GdiEnvironment
{
    private static readonly object _initLock = new();
    private static bool _initialized;

    public static string GetBaseDirectory()
    {
        var custom = Environment.GetEnvironmentVariable("GDI_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(custom)) return custom;

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GDI", "SRT Embedded");
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
            {
                return Path.Combine(home, ".gdi");
            }
            return "/var/lib/gdi";
        }
    }

    public static string GetConfigDirectory() => Path.Combine(GetBaseDirectory(), "config");
    public static string GetPreviewsDirectory() => Path.Combine(GetBaseDirectory(), "previews");
    public static string GetLogsDirectory() => Path.Combine(GetBaseDirectory(), "logs");
    public static string GetSecurityDirectory() => Path.Combine(GetBaseDirectory(), "security");

    public static void EnsureDirectories()
    {
        if (_initialized) return;
        lock (_initLock)
        {
            if (_initialized) return;
            try { Directory.CreateDirectory(GetConfigDirectory()); } catch { }
            try { Directory.CreateDirectory(GetPreviewsDirectory()); } catch { }
            try { Directory.CreateDirectory(GetLogsDirectory()); } catch { }
            try { Directory.CreateDirectory(GetSecurityDirectory()); } catch { }
            _initialized = true;
        }
    }
}
