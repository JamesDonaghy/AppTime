using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace AppTime
{
    /// Extracts and caches icons from application executables so library cards,
    /// session rows, and details views can show a real app glyph without hitting
    /// the filesystem on every paint.
    public static class AppIconCache
    {
        private static readonly Dictionary<string, Image> Cache =
            new(StringComparer.OrdinalIgnoreCase);

        private static readonly object Gate = new();

        /// Returns a square bitmap of the requested size for the given executable.
        /// Cached by path+size. Falls back to a simple letter tile when the file is
        /// missing or has no associated icon.
        public static Image GetIcon(string? executablePath, string? displayName, int size)
        {
            size = Math.Max(8, size);
            var key = $"{executablePath ?? string.Empty}|{size}";

            lock (Gate)
            {
                if (Cache.TryGetValue(key, out var cached))
                {
                    return cached;
                }

                Image image;
                try
                {
                    image = ExtractIcon(executablePath, size)
                        ?? CreateFallbackIcon(displayName, size);
                }
                catch
                {
                    image = CreateFallbackIcon(displayName, size);
                }

                Cache[key] = image;
                return image;
            }
        }

        public static Image GetIcon(AppEntry? app, int size)
        {
            if (app is null)
            {
                return CreateFallbackIcon("?", size);
            }

            return GetIcon(app.ExecutablePath, app.Name, size);
        }

        /// Drop cached images for a path (e.g. after the executable path is edited).
        public static void Invalidate(string? executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return;
            }

            lock (Gate)
            {
                var prefix = executablePath + "|";
                var keysToRemove = new List<string>();
                foreach (var key in Cache.Keys)
                {
                    if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        keysToRemove.Add(key);
                    }
                }

                foreach (var key in keysToRemove)
                {
                    if (Cache.Remove(key, out var image))
                    {
                        image.Dispose();
                    }
                }
            }
        }

        private static Image? ExtractIcon(string? executablePath, int size)
        {
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                return null;
            }

            // Prefer the shell API for a higher-quality large icon when available.
            var fromShell = ExtractViaShell(executablePath, size);
            if (fromShell is not null)
            {
                return fromShell;
            }

            using var icon = Icon.ExtractAssociatedIcon(executablePath);
            if (icon is null)
            {
                return null;
            }

            return ResizeToSquare(icon.ToBitmap(), size);
        }

        private static Image? ExtractViaShell(string path, int size)
        {
            var info = new SHFILEINFO();
            // LARGEICON is typically 32x32; SMALLICON 16x16. We always resize to the
            // requested size so callers can ask for 24/28/40 without caring.
            var flags = SHGFI_ICON | (size >= 28 ? SHGFI_LARGEICON : SHGFI_SMALLICON);
            var result = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(info), flags);
            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                using var icon = Icon.FromHandle(info.hIcon);
                // Icon.FromHandle does not take ownership - DestroyIcon below does.
                using var bitmap = icon.ToBitmap();
                return ResizeToSquare(bitmap, size);
            }
            finally
            {
                DestroyIcon(info.hIcon);
            }
        }

        private static Image ResizeToSquare(Image source, int size)
        {
            var dest = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(dest))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(source, 0, 0, size, size);
            }

            return dest;
        }

        private static Image CreateFallbackIcon(string? displayName, int size)
        {
            var letter = "?";
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                letter = char.ToUpperInvariant(displayName.Trim()[0]).ToString();
            }

            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var bg = AppTheme.AccentSubtle;
            var fg = AppTheme.Accent;
            using (var brush = new SolidBrush(bg))
            {
                var inset = Math.Max(1, size / 16);
                g.FillEllipse(brush, inset, inset, size - inset * 2 - 1, size - inset * 2 - 1);
            }

            var fontSize = Math.Max(8f, size * 0.45f);
            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(fg);
            var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString(letter, font, textBrush, new RectangleF(0, 0, size, size), sf);

            return bmp;
        }

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_LARGEICON = 0x000000000;
        private const uint SHGFI_SMALLICON = 0x000000001;

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
        private static extern IntPtr SHGetFileInfo(
            string pszPath,
            uint dwFileAttributes,
            ref SHFILEINFO psfi,
            uint cbFileInfo,
            uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);
    }
}