using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SwiftPanel.Models
{
    public partial class FileItem : ObservableObject
    {
        [ObservableProperty] private string _name = string.Empty;
        [ObservableProperty] private string _extension = string.Empty;
        [ObservableProperty] private string _displaySize = string.Empty;
        [ObservableProperty] private long _sizeBytes;
        [ObservableProperty] private DateTime _dateModified;
        [ObservableProperty] private string _attributes = string.Empty;
        [ObservableProperty] private bool _isDirectory;
        [ObservableProperty] private bool _isSelected;
        [ObservableProperty] private bool _isMarked;   // Space-key mark (yellow highlight)
        [ObservableProperty] private bool _isRenaming; // Inline rename mode
        [ObservableProperty] private string _editingName = string.Empty;

        // Small 16×16 shell icon (always loaded)
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DisplayThumbnail))]
        private ImageSource? _icon;

        // Large icon / thumbnail (loaded on-demand for LargeIcons/Thumbnails views)
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DisplayThumbnail))]
        private ImageSource? _thumbnail;

        // Returns Thumbnail if loaded; falls back to Icon
        public ImageSource? DisplayThumbnail => Thumbnail ?? Icon;

        public string FullPath { get; set; } = string.Empty;
        public string DisplayName => Name; // No brackets around folders

        public string DisplayDate => DateModified.ToString("MM/dd/yyyy  HH:mm");

        public static FileItem FromFileInfo(FileInfo fi)
        {
            return new FileItem
            {
                Name = fi.Name,
                Extension = fi.Extension.TrimStart('.').ToUpper(),
                SizeBytes = fi.Length,
                DisplaySize = FormatSize(fi.Length),
                DateModified = fi.LastWriteTime,
                Attributes = GetAttributeString(fi.Attributes),
                IsDirectory = false,
                FullPath = fi.FullName
            };
        }

        public static FileItem FromDirectoryInfo(DirectoryInfo di)
        {
            return new FileItem
            {
                Name = di.Name,
                Extension = string.Empty,
                SizeBytes = 0,
                DisplaySize = "<DIR>",
                DateModified = di.LastWriteTime,
                Attributes = GetAttributeString(di.Attributes),
                IsDirectory = true,
                FullPath = di.FullName
            };
        }

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
            if (bytes >= 1_048_576)     return $"{bytes / 1_048_576.0:F1} MB";
            if (bytes >= 1_024)         return $"{bytes / 1_024.0:F0} KB";
            return $"{bytes} B";
        }

        /// <summary>Public wrapper – used by CalculateFolderSizes in the ViewModel.</summary>
        public static string FormatSizePublic(long bytes) => FormatSize(bytes);

        private static string GetAttributeString(FileAttributes attr)
        {
            return string.Concat(
                attr.HasFlag(FileAttributes.ReadOnly) ? "R" : "-",
                attr.HasFlag(FileAttributes.Hidden) ? "H" : "-",
                attr.HasFlag(FileAttributes.System) ? "S" : "-",
                attr.HasFlag(FileAttributes.Archive) ? "A" : "-"
            );
        }
    }
}
