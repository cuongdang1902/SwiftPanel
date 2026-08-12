using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using SwiftPanel.Models;

namespace SwiftPanel.ViewModels
{
    public enum ViewerContentType { Empty, Directory, Image, Text, Hex }

    public partial class FileViewerViewModel : ObservableObject
    {
        // ── Displayed state ───────────────────────────────────────────────
        [ObservableProperty] private string _fileName    = "No file selected";
        [ObservableProperty] private string _fileInfo    = string.Empty;
        [ObservableProperty] private string _contentType_Label = string.Empty;
        [ObservableProperty] private bool   _isLoading   = false;

        // ── Content ───────────────────────────────────────────────────────
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowEmpty),
            nameof(ShowImage), nameof(ShowText), nameof(ShowDir))]
        private ViewerContentType _mode = ViewerContentType.Empty;

        [ObservableProperty] private ImageSource? _imageContent;
        [ObservableProperty] private string _textContent = string.Empty;

        public bool ShowEmpty => Mode == ViewerContentType.Empty;
        public bool ShowImage => Mode == ViewerContentType.Image;
        public bool ShowText  => Mode is ViewerContentType.Text or ViewerContentType.Hex or ViewerContentType.Directory;
        public bool ShowDir   => Mode == ViewerContentType.Directory;

        // ── File type sets ────────────────────────────────────────────────
        private static readonly HashSet<string> s_imageExts = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".tif", ".ico", ".webp", ".avif" };

        private static readonly HashSet<string> s_textExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".md", ".log", ".csv", ".ini", ".cfg", ".conf", ".env",
            ".cs",  ".vb", ".fs",  ".cpp", ".c",   ".h",   ".java", ".py",
            ".js",  ".ts", ".jsx", ".tsx", ".html", ".htm", ".css",  ".scss",
            ".xml", ".xaml", ".csproj", ".sln", ".json", ".yaml", ".yml",
            ".toml", ".sh",  ".bat",  ".ps1", ".sql", ".rb",  ".php",
            ".gitignore", ".gitattributes", ".editorconfig"
        };

        // ── Loading cancellation ──────────────────────────────────────────
        private CancellationTokenSource? _cts;

        // ── Public API ────────────────────────────────────────────────────

        public async Task LoadAsync(FileItem? item)
        {
            // Cancel any previous load
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            if (item == null || item.Name == "..")
            {
                Clear();
                return;
            }

            FileName    = item.Name;
            TextContent = string.Empty;
            ImageContent= null;
            IsLoading   = true;
            Mode        = ViewerContentType.Empty;

            try
            {
                if (item.IsDirectory)
                {
                    ContentType_Label = "Directory";
                    FileInfo    = string.Empty;
                    TextContent = await Task.Run(() => GetDirectoryInfo(item.FullPath), token);
                    Mode        = ViewerContentType.Directory;
                    return;
                }

                var ext = Path.GetExtension(item.FullPath);
                var fi  = new FileInfo(item.FullPath);
                FileInfo = $"{item.DisplaySize}  ·  {item.DisplayDate}";

                if (s_imageExts.Contains(ext))
                {
                    ContentType_Label = "Image";
                    Mode = ViewerContentType.Image;
                    ImageContent = await Task.Run(() => LoadBitmap(item.FullPath), token);
                }
                else if (s_textExts.Contains(ext) && fi.Length < 10 * 1024 * 1024) // < 10 MB
                {
                    ContentType_Label = $"Text  ({ext.TrimStart('.').ToUpper()})";
                    Mode = ViewerContentType.Text;
                    TextContent = await Task.Run(() => ReadText(item.FullPath), token);
                }
                else
                {
                    ContentType_Label = "Binary (hex)";
                    Mode = ViewerContentType.Hex;
                    TextContent = await Task.Run(() => ReadHex(item.FullPath), token);
                }
            }
            catch (OperationCanceledException) { /* superseded */ }
            catch (Exception ex)
            {
                Mode = ViewerContentType.Text;
                TextContent = $"⚠ Cannot read file:\n{ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        public void Clear()
        {
            Mode         = ViewerContentType.Empty;
            FileName     = "No file selected";
            FileInfo     = string.Empty;
            TextContent  = string.Empty;
            ImageContent = null;
            ContentType_Label = string.Empty;
        }

        // ── Content loaders (run on background thread) ────────────────────

        private static ImageSource LoadBitmap(string path)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource    = new Uri(path, UriKind.Absolute);
            bmp.CacheOption  = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            if (bmp.CanFreeze) bmp.Freeze();
            return bmp;
        }

        private static string ReadText(string path)
        {
            const int maxBytes = 512 * 1024; // 512 KB
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            int toRead = (int)Math.Min(fs.Length, maxBytes);
            var buf    = new byte[toRead];
            int offset = 0;
            while (offset < toRead)
            {
                int n = fs.Read(buf, offset, toRead - offset);
                if (n == 0) break;
                offset += n;
            }

            // Detect UTF-8 BOM or fall back to current encoding
            var enc = buf.Length >= 3
                      && buf[0] == 0xEF && buf[1] == 0xBB && buf[2] == 0xBF
                      ? Encoding.UTF8 : Encoding.Default;

            var text = enc.GetString(buf, 0, offset);
            if (fs.Length > maxBytes)
                text += $"\n\n── Showing first {maxBytes / 1024} KB of {fs.Length / 1024} KB ──";
            return text;
        }

        private static string ReadHex(string path)
        {
            const int rows    = 32;
            const int rowSize = 16;
            const int maxBytes= rows * rowSize;

            using var fs  = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buf       = new byte[Math.Min(fs.Length, maxBytes)];
            int read      = fs.Read(buf, 0, buf.Length);

            var sb = new StringBuilder();
            sb.AppendLine($"File: {path}");
            sb.AppendLine($"Size: {fs.Length:N0} bytes");
            sb.AppendLine(new string('─', 73));

            for (int i = 0; i < read; i += rowSize)
            {
                sb.Append($"{i:X8}  ");
                for (int j = 0; j < rowSize; j++)
                {
                    if (i + j < read) sb.Append($"{buf[i + j]:X2} ");
                    else              sb.Append("   ");
                    if (j == 7) sb.Append(' ');
                }
                sb.Append(" │ ");
                for (int j = 0; j < rowSize && i + j < read; j++)
                {
                    byte b = buf[i + j];
                    sb.Append(b >= 32 && b < 127 ? (char)b : '·');
                }
                sb.AppendLine();
            }

            if (fs.Length > maxBytes)
                sb.AppendLine($"\n── {fs.Length - maxBytes:N0} more bytes not shown ──");

            return sb.ToString();
        }

        private static string GetDirectoryInfo(string path)
        {
            try
            {
                var di    = new DirectoryInfo(path);
                var dirs  = di.GetDirectories().Length;
                var files = di.GetFiles().Length;
                var sb    = new StringBuilder();
                sb.AppendLine($"📁  {path}");
                sb.AppendLine();
                sb.AppendLine($"  Subdirectories : {dirs}");
                sb.AppendLine($"  Files          : {files}");
                sb.AppendLine();
                sb.AppendLine($"  Created  : {di.CreationTime}");
                sb.AppendLine($"  Modified : {di.LastWriteTime}");
                sb.AppendLine($"  Accessed : {di.LastAccessTime}");
                sb.AppendLine();
                sb.AppendLine($"  Attributes : {di.Attributes}");
                return sb.ToString();
            }
            catch (Exception ex) { return $"⚠ {ex.Message}"; }
        }
    }
}
