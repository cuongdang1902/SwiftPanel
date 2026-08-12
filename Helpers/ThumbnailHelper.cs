using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SwiftPanel.Helpers
{
    /// <summary>
    /// Generates thumbnails for file items:
    ///   • Image files  → actual BitmapImage preview (decoded to requested size)
    ///   • Other files  → large shell icon via IconHelper
    ///   • Directories  → large shell folder icon
    ///
    /// All returned ImageSource objects are Freeze()d and safe to use across threads.
    /// </summary>
    public static class ThumbnailHelper
    {
        private static readonly HashSet<string> s_imageExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp",
            ".tiff", ".tif", ".ico", ".webp", ".avif"
        };

        /// <summary>
        /// Load thumbnail synchronously (intended to run on a background thread).
        /// </summary>
        /// <param name="filePath">Full path to the file.</param>
        /// <param name="isDirectory">True for directory items.</param>
        /// <param name="decodeWidth">Target decode width in pixels (e.g. 48 or 96).</param>
        public static ImageSource? Load(string filePath, bool isDirectory, int decodeWidth = 96)
        {
            if (isDirectory)
                return IconHelper.GetLargeFolderIcon();

            try
            {
                var ext = Path.GetExtension(filePath);
                if (s_imageExts.Contains(ext))
                    return LoadImagePreview(filePath, decodeWidth);
            }
            catch { /* fall through to shell icon */ }

            return IconHelper.GetLargeFileIcon(filePath);
        }

        private static ImageSource? LoadImagePreview(string filePath, int decodeWidth)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource          = new Uri(filePath, UriKind.Absolute);
            bmp.DecodePixelWidth   = decodeWidth;
            bmp.CacheOption        = BitmapCacheOption.OnLoad;   // fully load into memory
            bmp.CreateOptions      = BitmapCreateOptions.None;
            bmp.EndInit();

            if (bmp.CanFreeze) bmp.Freeze();
            return bmp;
        }
    }
}
