using System;
using System.IO;
using Godot;

namespace STS2Mobile;

// iOS sandbox paths using Godot's user:// (Documents directory).
public static class AppPaths
{
    public static string DocumentsDir => ProjectSettings.GlobalizePath("user://");
    public static string ModsDir => ProjectSettings.GlobalizePath("user://Mods");
    public static string SavesDir => ProjectSettings.GlobalizePath("user://Saves");
    public static string PckPath => ProjectSettings.GlobalizePath("user://SlayTheSpire2.pck");

    public static string ExternalModsDir => ModsDir;
    public static string ExternalSaveBackupsDir => SavesDir;

    public static bool HasStoragePermission() => true;

    public static void EnsureDirectories()
    {
        try
        {
            Directory.CreateDirectory(ModsDir);
            Directory.CreateDirectory(SavesDir);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"EnsureDirectories: {ex.Message}");
        }
    }
}
