using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftPanel.Models;
using SwiftPanel.Helpers;

namespace SwiftPanel.ViewModels
{
    // ─────────────────────────────────────────────────────────────────────
    // Sort support
    // ─────────────────────────────────────────────────────────────────────
    public enum SortColumn { Name, Extension, Size, Date, Attributes }

    // ─────────────────────────────────────────────────────────────────────
    // View-mode support
    // ─────────────────────────────────────────────────────────────────────
    public enum ViewMode
    {
        Details,     // List with columns (default)
        List,        // Small icons + name, wraps vertically
        LargeIcons,  // 32×32 shell icons, wraps horizontally
        Thumbnails   // 96×96 image preview (or shell icon), wraps horizontally
    }

    public partial class FilePanelViewModel : ObservableObject
    {
        // ── Observable state ──────────────────────────────────────────────
        [ObservableProperty] private string _currentPath = string.Empty;
        [ObservableProperty] private ObservableCollection<FileItem> _items = new();
        [ObservableProperty] private FileItem? _selectedItem;
        [ObservableProperty] private string _statusText = string.Empty;
        [ObservableProperty] private bool _isActive;
        [ObservableProperty] private ObservableCollection<TabEntry> _tabs = new();
        [ObservableProperty] private int _activeTabIndex = 0;

        // ── Sort state ────────────────────────────────────────────────────
        [ObservableProperty] private SortColumn _currentSort = SortColumn.Name;
        [ObservableProperty] private bool _sortAscending = true;

        // ── Filter & display options ───────────────────────────────────────
        [ObservableProperty] private string _filterText = string.Empty;
        [ObservableProperty] private bool _showHidden = false;
        [ObservableProperty] private bool _isFilterVisible = false;

        // ── View mode ──────────────────────────────────────────────────────
        [ObservableProperty] private ViewMode _viewMode = ViewMode.Details;

        // Raw (unfiltered) list – filtering works on top of this
        private List<FileItem> _allItems = new();

        // Arrow indicator strings
        public string NameSortArrow       => SortArrow(SortColumn.Name);
        public string ExtSortArrow        => SortArrow(SortColumn.Extension);
        public string SizeSortArrow       => SortArrow(SortColumn.Size);
        public string DateSortArrow       => SortArrow(SortColumn.Date);
        public string AttributesSortArrow => SortArrow(SortColumn.Attributes);

        private string SortArrow(SortColumn col)
            => CurrentSort == col ? (SortAscending ? " ▲" : " ▼") : string.Empty;

        private FileSystemWatcher? _watcher;

        // ── Called whenever FilterText or ShowHidden change ────────────────
        partial void OnFilterTextChanged(string value)   => ApplyFilter();
        partial void OnShowHiddenChanged(bool value)     => LoadDirectory(CurrentPath);

        // ─────────────────────────────────────────────────────────────────
        // Constructors
        // ─────────────────────────────────────────────────────────────────
        public FilePanelViewModel()
        {
            var path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Tabs.Add(new TabEntry { Header = "Home", Path = path });
            NavigateTo(path);
        }

        public FilePanelViewModel(string initialPath)
        {
            Tabs.Add(new TabEntry
            {
                Header = Path.GetFileName(initialPath).IfEmpty(initialPath),
                Path = initialPath
            });
            NavigateTo(initialPath);
        }

        // ─────────────────────────────────────────────────────────────────
        // Navigation
        // ─────────────────────────────────────────────────────────────────
        [RelayCommand]
        public void NavigateTo(string path)
        {
            if (!Directory.Exists(path)) return;
            CurrentPath = path;
            FilterText = string.Empty;   // clear filter on navigate
            IsFilterVisible = false;
            LoadDirectory(path);
            SetupWatcher(path);

            if (ActiveTabIndex >= 0 && ActiveTabIndex < Tabs.Count)
                Tabs[ActiveTabIndex].Header = Path.GetFileName(path).IfEmpty(path);
        }

        [RelayCommand]
        public void NavigateUp()
        {
            var parent = Directory.GetParent(CurrentPath);
            if (parent != null)
                NavigateTo(parent.FullName);
        }

        [RelayCommand]
        public void OpenItem(FileItem? item)
        {
            if (item == null) return;
            if (item.IsDirectory)
                NavigateTo(item.FullPath);
            else
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = item.FullPath,
                    UseShellExecute = true
                });
        }

        // ─────────────────────────────────────────────────────────────────
        // View Mode
        // ─────────────────────────────────────────────────────────────────
        [RelayCommand]
        public void SetViewMode(string modeName)
        {
            if (Enum.TryParse<ViewMode>(modeName, out var mode))
                ViewMode = mode;
        }

        /// <summary>
        /// Load large icons / thumbnails asynchronously for all items.
        /// Called by the view when switching to LargeIcons or Thumbnails mode.
        /// </summary>
        public async Task LoadThumbnailsAsync(int decodeWidth)
        {
            // Snapshot to avoid collection-change issues
            var snapshot = _allItems.ToList();

            var tasks = snapshot
                .Where(i => i.Name != ".." && i.Thumbnail == null)
                .Select(item => Task.Run(() =>
                {
                    var thumb = ThumbnailHelper.Load(item.FullPath, item.IsDirectory, decodeWidth);
                    // BeginInvoke so UI isn't blocked waiting for every item
                    Application.Current.Dispatcher.BeginInvoke(() => item.Thumbnail = thumb);
                }));

            await Task.WhenAll(tasks);
        }

        // ─────────────────────────────────────────────────────────────────
        // Show / Hide hidden files  (Ctrl+H)
        // ─────────────────────────────────────────────────────────────────
        [RelayCommand]
        public void ToggleHidden() => ShowHidden = !ShowHidden;

        // ─────────────────────────────────────────────────────────────────
        // Quick Filter  (Ctrl+F or just start typing)
        // ─────────────────────────────────────────────────────────────────
        public void ShowFilter()
        {
            IsFilterVisible = true;
        }

        public void HideFilter()
        {
            FilterText = string.Empty;
            IsFilterVisible = false;
        }

        private void ApplyFilter()
        {
            Items.Clear();
            var filter = FilterText.Trim();

            var source = string.IsNullOrEmpty(filter)
                ? _allItems
                : _allItems.Where(i =>
                    i.Name == ".." ||
                    i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var item in source)
                Items.Add(item);

            UpdateStatus();
        }

        // ─────────────────────────────────────────────────────────────────
        // Copy path to clipboard  (Ctrl+Shift+C)
        // ─────────────────────────────────────────────────────────────────
        [RelayCommand]
        public void CopyPathToClipboard()
        {
            var item = SelectedItem;
            var text = (item != null && item.Name != "..") ? item.FullPath : CurrentPath;
            System.Windows.Clipboard.SetText(text);
        }

        // ─────────────────────────────────────────────────────────────────
        // Drive info helper  (used for drive button tooltips)
        // ─────────────────────────────────────────────────────────────────
        public static string GetDriveTooltip(string drivePath)
        {
            try
            {
                var di = new DriveInfo(drivePath);
                if (!di.IsReady) return drivePath;
                long free  = di.AvailableFreeSpace;
                long total = di.TotalSize;
                return $"{di.VolumeLabel} ({drivePath.TrimEnd('\\')})"
                     + $"\nFree:  {FormatSize(free)}"
                     + $"\nTotal: {FormatSize(total)}";
            }
            catch { return drivePath; }
        }

        // ─────────────────────────────────────────────────────────────────
        // Sorting
        // ─────────────────────────────────────────────────────────────────
        [RelayCommand]
        public void SortBy(string columnName)
        {
            var col = Enum.Parse<SortColumn>(columnName, ignoreCase: true);
            if (CurrentSort == col)
                SortAscending = !SortAscending;   // toggle direction
            else
            {
                CurrentSort = col;
                SortAscending = true;
            }

            // Notify all header arrows
            OnPropertyChanged(nameof(NameSortArrow));
            OnPropertyChanged(nameof(ExtSortArrow));
            OnPropertyChanged(nameof(SizeSortArrow));
            OnPropertyChanged(nameof(DateSortArrow));
            OnPropertyChanged(nameof(AttributesSortArrow));

            ApplySort();
        }

        private void ApplySort()
        {
            SortAllItems();
            ApplyFilter();
        }

        /// <summary>Sorts _allItems in-place (dirs before files, ".." always first).</summary>
        private void SortAllItems()
        {
            var up    = _allItems.FirstOrDefault(i => i.Name == "..");
            var dirs  = _allItems.Where(i => i.IsDirectory && i.Name != "..").ToList();
            var files = _allItems.Where(i => !i.IsDirectory).ToList();

            IOrderedEnumerable<FileItem> SortList(IEnumerable<FileItem> list)
                => (CurrentSort, SortAscending) switch
                {
                    (SortColumn.Name,       true)  => list.OrderBy(i => i.Name,      StringComparer.OrdinalIgnoreCase),
                    (SortColumn.Name,       false) => list.OrderByDescending(i => i.Name, StringComparer.OrdinalIgnoreCase),
                    (SortColumn.Extension,  true)  => list.OrderBy(i => i.Extension, StringComparer.OrdinalIgnoreCase),
                    (SortColumn.Extension,  false) => list.OrderByDescending(i => i.Extension, StringComparer.OrdinalIgnoreCase),
                    (SortColumn.Size,       true)  => list.OrderBy(i => i.SizeBytes),
                    (SortColumn.Size,       false) => list.OrderByDescending(i => i.SizeBytes),
                    (SortColumn.Date,       true)  => list.OrderBy(i => i.DateModified),
                    (SortColumn.Date,       false) => list.OrderByDescending(i => i.DateModified),
                    (SortColumn.Attributes, true)  => list.OrderBy(i => i.Attributes),
                    (SortColumn.Attributes, false) => list.OrderByDescending(i => i.Attributes),
                    _                              => list.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                };

            _allItems.Clear();
            if (up != null) _allItems.Add(up);
            _allItems.AddRange(SortList(dirs));
            _allItems.AddRange(SortList(files));
        }

        // ─────────────────────────────────────────────────────────────────
        // Tabs
        // ─────────────────────────────────────────────────────────────────
        [RelayCommand]
        public void AddTab()
        {
            var entry = new TabEntry
            {
                Header = Path.GetFileName(CurrentPath).IfEmpty(CurrentPath),
                Path = CurrentPath
            };
            Tabs.Add(entry);
            ActiveTabIndex = Tabs.Count - 1;
        }

        [RelayCommand]
        public void CloseTab(int index)
        {
            if (Tabs.Count <= 1) return;
            Tabs.RemoveAt(index);
            ActiveTabIndex = Math.Min(ActiveTabIndex, Tabs.Count - 1);
            NavigateTo(Tabs[ActiveTabIndex].Path);
        }

        [RelayCommand]
        public void SwitchTab(int index)
        {
            if (index < 0 || index >= Tabs.Count) return;
            ActiveTabIndex = index;
            NavigateTo(Tabs[index].Path);
        }

        // ─────────────────────────────────────────────────────────────────
        // Mark / Unmark (Space key)
        // ─────────────────────────────────────────────────────────────────
        [RelayCommand]
        public void ToggleMark(FileItem? item)
        {
            if (item == null || item.Name == "..") return;
            item.IsMarked = !item.IsMarked;
            UpdateStatus();
        }

        public void MarkAll()
        {
            foreach (var item in Items.Where(i => i.Name != ".."))
                item.IsMarked = true;
            UpdateStatus();
        }

        public void UnmarkAll()
        {
            foreach (var item in Items)
                item.IsMarked = false;
            UpdateStatus();
        }

        public void InvertMarks()
        {
            foreach (var item in Items.Where(i => i.Name != ".."))
                item.IsMarked = !item.IsMarked;
            UpdateStatus();
        }

        /// <summary>Returns marked items, or falls back to selected item if none marked.</summary>
        public IReadOnlyList<FileItem> GetActionItems()
        {
            var marked = Items.Where(i => i.IsMarked).ToList();
            if (marked.Count > 0) return marked;
            if (SelectedItem != null && SelectedItem.Name != "..") return new[] { SelectedItem };
            return Array.Empty<FileItem>();
        }

        // ─────────────────────────────────────────────────────────────────
        // Inline Rename
        // ─────────────────────────────────────────────────────────────────
        [RelayCommand]
        public void StartRename(FileItem? item)
        {
            if (item == null || item.Name == "..") return;
            foreach (var i in Items) i.IsRenaming = false;
            item.EditingName = item.Name;
            item.IsRenaming = true;
        }

        [RelayCommand]
        public void CommitRename(FileItem? item)
        {
            if (item == null || !item.IsRenaming) return;
            item.IsRenaming = false;

            var newName = item.EditingName.Trim();
            if (string.IsNullOrEmpty(newName) || newName == item.Name) return;

            var newPath = Path.Combine(CurrentPath, newName);
            try
            {
                if (item.IsDirectory)
                    Directory.Move(item.FullPath, newPath);
                else
                    File.Move(item.FullPath, newPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Rename Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void CancelRename(FileItem? item)
        {
            if (item == null) return;
            item.IsRenaming = false;
        }

        // ─────────────────────────────────────────────────────────────────
        // Directory Loading
        // ─────────────────────────────────────────────────────────────────
        private void LoadDirectory(string path)
        {
            _allItems.Clear();
            Items.Clear();
            try
            {
                var parent = Directory.GetParent(path);
                if (parent != null)
                    _allItems.Add(new FileItem
                    {
                        Name = "..", DisplaySize = "<UP>",
                        IsDirectory = true, FullPath = parent.FullName
                    });

                foreach (var dir in Directory.GetDirectories(path).OrderBy(d => d))
                {
                    try
                    {
                        var di = new DirectoryInfo(dir);
                        bool isHidden = di.Attributes.HasFlag(FileAttributes.Hidden);
                        if (isHidden && !ShowHidden) continue;
                        var item = FileItem.FromDirectoryInfo(di);
                        item.Icon = IconHelper.GetFolderIcon();
                        _allItems.Add(item);
                    }
                    catch { }
                }

                foreach (var file in Directory.GetFiles(path).OrderBy(f => f))
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        bool isHidden = fi.Attributes.HasFlag(FileAttributes.Hidden);
                        if (isHidden && !ShowHidden) continue;
                        var item = FileItem.FromFileInfo(fi);
                        item.Icon = IconHelper.GetFileIcon(file);
                        _allItems.Add(item);
                    }
                    catch { }
                }

                // Sort then filter
                if (CurrentSort != SortColumn.Name || !SortAscending)
                    SortAllItems();

                ApplyFilter();   // also refreshes Items & status
            }
            catch (UnauthorizedAccessException)
            {
                StatusText = "Access denied";
            }
        }

        private void UpdateStatus()
        {
            int dirs  = Items.Count(i => i.IsDirectory && i.Name != "..");
            int files = Items.Count(i => !i.IsDirectory);
            long totalSize = Items.Where(i => !i.IsDirectory).Sum(i => i.SizeBytes);

            var marked = Items.Where(i => i.IsMarked).ToList();
            if (marked.Count > 0)
            {
                long markedSize = marked.Where(i => !i.IsDirectory).Sum(i => i.SizeBytes);
                int mDirs  = marked.Count(i => i.IsDirectory);
                int mFiles = marked.Count(i => !i.IsDirectory);
                StatusText = $"[{marked.Count} marked: {mDirs}d {mFiles}f {FormatSize(markedSize)}]  {dirs}d {files}f {FormatSize(totalSize)}";
            }
            else
            {
                StatusText = $"{dirs} dirs, {files} files | {FormatSize(totalSize)}";
            }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
            if (bytes >= 1_048_576)     return $"{bytes / 1_048_576.0:F1} MB";
            if (bytes >= 1_024)         return $"{bytes / 1_024.0:F0} KB";
            return $"{bytes} B";
        }

        private void SetupWatcher(string path)
        {
            _watcher?.Dispose();
            try
            {
                _watcher = new FileSystemWatcher(path)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                    EnableRaisingEvents = true
                };
                _watcher.Changed += (_, _) => Application.Current.Dispatcher.Invoke(() => LoadDirectory(CurrentPath));
                _watcher.Created += (_, _) => Application.Current.Dispatcher.Invoke(() => LoadDirectory(CurrentPath));
                _watcher.Deleted += (_, _) => Application.Current.Dispatcher.Invoke(() => LoadDirectory(CurrentPath));
                _watcher.Renamed += (_, _) => Application.Current.Dispatcher.Invoke(() => LoadDirectory(CurrentPath));
            }
            catch { }
        }

        // ─────────────────────────────────────────────────────────────────
        // Breadcrumb
        // ─────────────────────────────────────────────────────────────────
        public BreadcrumbPart[] BreadcrumbParts
        {
            get
            {
                var parts = CurrentPath.Split(
                    new[] { Path.DirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries);
                var result = new BreadcrumbPart[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                {
                    string partPath = string.Join(
                        Path.DirectorySeparatorChar.ToString(),
                        parts.Take(i + 1)) + Path.DirectorySeparatorChar;
                    result[i] = new BreadcrumbPart { Label = parts[i], FullPath = partPath };
                }
                return result;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Supporting types
    // ─────────────────────────────────────────────────────────────────────
    public class BreadcrumbPart
    {
        public string Label    { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
    }

    public partial class TabEntry : ObservableObject
    {
        [ObservableProperty] private string _header = string.Empty;
        public string Path { get; set; } = string.Empty;
    }

    internal static class StringExtensions
    {
        public static string IfEmpty(this string s, string fallback)
            => string.IsNullOrEmpty(s) ? fallback : s;
    }
}
