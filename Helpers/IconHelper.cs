using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SwiftPanel.Helpers
{
    public static class IconHelper
    {
        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
            ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]  public string szTypeName;
        }

        private const uint SHGFI_ICON             = 0x100;
        private const uint SHGFI_SMALLICON        = 0x001;  // 16×16
        private const uint SHGFI_LARGEICON        = 0x000;  // 32×32  (default, no flag needed)
        private const uint SHGFI_USEFILEATTRIBUTES = 0x010;
        private const uint FILE_ATTRIBUTE_NORMAL   = 0x080;
        private const uint FILE_ATTRIBUTE_DIRECTORY= 0x010;

        // ── Public API ──────────────────────────────────────────────────────

        /// <summary>16×16 small shell icon (Details/List views).</summary>
        public static ImageSource? GetFileIcon(string filePath)
            => GetShellIcon(filePath, isFolder: false, small: true);

        /// <summary>16×16 small folder icon.</summary>
        public static ImageSource? GetFolderIcon()
            => GetShellIcon(string.Empty, isFolder: true, small: true);

        /// <summary>32×32 large shell icon (LargeIcons view).</summary>
        public static ImageSource? GetLargeFileIcon(string filePath)
            => GetShellIcon(filePath, isFolder: false, small: false);

        /// <summary>32×32 large folder icon.</summary>
        public static ImageSource? GetLargeFolderIcon()
            => GetShellIcon(string.Empty, isFolder: true, small: false);

        // ── Implementation ──────────────────────────────────────────────────

        private static ImageSource? GetShellIcon(string path, bool isFolder, bool small)
        {
            try
            {
                var shinfo = new SHFILEINFO();
                uint flags = SHGFI_ICON | (small ? SHGFI_SMALLICON : SHGFI_LARGEICON);
                uint attr  = FILE_ATTRIBUTE_NORMAL;

                if (isFolder)
                {
                    flags |= SHGFI_USEFILEATTRIBUTES;
                    attr   = FILE_ATTRIBUTE_DIRECTORY;
                    path   = "folder";
                }

                SHGetFileInfo(path, attr, ref shinfo, (uint)Marshal.SizeOf(shinfo), flags);

                if (shinfo.hIcon == IntPtr.Zero) return null;

                var bitmap = Imaging.CreateBitmapSourceFromHIcon(
                    shinfo.hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                DestroyIcon(shinfo.hIcon);

                // Freeze so it can cross thread boundaries
                if (bitmap.CanFreeze) bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }
    }
}
