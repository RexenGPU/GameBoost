using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using GameBoost.Core.Data;
using GameBoost.Core.Logging;

namespace GameBoost.Core.Games;

public static class GameIconCache
{
    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_LARGEICON = 0x0;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const int IconSize = 64;

    public static string GetIconPath(string exePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(exePath)) return string.Empty;
            var full = Path.GetFullPath(exePath);
            if (!File.Exists(full)) return string.Empty;
            var target = CacheFile("exe-" + full);
            if (target.Length == 0) return string.Empty;
            if (File.Exists(target)) return target;
            using var icon = Icon.ExtractAssociatedIcon(full);
            if (icon is null) return string.Empty;
            using var bitmap = icon.ToBitmap();
            SavePng(bitmap, target);
            return File.Exists(target) ? target : string.Empty;
        }
        catch (Exception ex)
        {
            Log.Warn("GameIconCache", "Icône impossible : " + exePath + " — " + ex.Message);
            return string.Empty;
        }
    }

    public static string GetFolderIconPath(string path)
    {
        SHFILEINFO info = default;
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            var full = Path.GetFullPath(path);
            if (!Directory.Exists(full) && !File.Exists(full)) return string.Empty;
            var target = CacheFile("path-" + full);
            if (target.Length == 0) return string.Empty;
            if (File.Exists(target)) return target;
            SHGetFileInfo(full, FILE_ATTRIBUTE_DIRECTORY, ref info,
                (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON);
            if (info.hIcon == IntPtr.Zero) return string.Empty;
            using var icon = Icon.FromHandle(info.hIcon);
            using var bitmap = icon.ToBitmap();
            SavePng(bitmap, target);
            return File.Exists(target) ? target : string.Empty;
        }
        catch (Exception ex)
        {
            Log.Warn("GameIconCache", "Icône de dossier impossible : " + path + " — " + ex.Message);
            return string.Empty;
        }
        finally
        {
            if (info.hIcon != IntPtr.Zero) DestroyIcon(info.hIcon);
        }
    }

    private static string CacheFile(string key)
    {
        try
        {
            AppPaths.Ensure();
            var directory = Path.Combine(AppPaths.CacheDir, "icons");
            Directory.CreateDirectory(directory);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key.ToLowerInvariant())))[..24];
            return Path.Combine(directory, hash + ".png");
        }
        catch (Exception ex)
        {
            Log.Warn("GameIconCache", "Dossier de cache inaccessible : " + ex.Message);
            return string.Empty;
        }
    }

    private static void SavePng(Bitmap source, string target)
    {
        using var resized = Resize(source, IconSize);
        resized.Save(target, ImageFormat.Png);
    }

    private static Bitmap Resize(Bitmap source, int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(Color.Transparent);
        var scale = Math.Min((double)size / source.Width, (double)size / source.Height);
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var x = (size - width) / 2;
        var y = (size - height) / 2;
        graphics.DrawImage(source, new Rectangle(x, y, width, height));
        return bitmap;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
