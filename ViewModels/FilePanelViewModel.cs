using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftPanel.Models;
using SwiftPanel.Helpers;
using SwiftPanel.Services;

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

        // ── Navigation history ────────────────────────────────────────────
        private readonly List<string> _navHistory = new();
        private int _navIndex = -1;
        public bool CanNavigateBack    => _navIndex > 0;
        public bool CanNavigateForward => _navIndex < _navHistory.Count - 1;

        // ── ZIP state ─────────────────────────────────────────────────────
        [ObservableProperty] private bool   _isInZip          = false;
        [ObservableProperty] private string _zipFilePath      = string.Empty;
        [ObservableProperty] private string _zipInternalPath  = string.Empty;

        // ── Disk usage (updated on NavigateTo) ────────────────────────────
        [ObservableProperty] private double _driveUsedPercent = 0;
        [ObservableProperty] private string _driveSpaceText   = string.Empty;

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
        public FilePanelViewModel(IEnumerable<SavedTab>? savedTabs = null, int activeTabIndex = 0, string? fallbackPath = null)
        {
            if (savedTabs != null && savedTabs.Any())
            {
                foreach (var st in savedTabs)
                    Tabs.Add(new TabEntry { Header = st.Header, Path = st.Path });
                
                ActiveTabIndex = (activeTabIndex >= 0 && activeTabIndex < Tabs.Count) ? activeTabIndex : 0;
                NavigateTo(Tabs[ActiveTabIndex].Path);
            }
            else
            {
                var path = fallbackPath ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                Tabs.Add(new TabEntry
                {
                    Header = Path.GetFileName(path).IfEmpty(path),
                    Path = path
                });
                NavigateTo(path);
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Navigation  (history + ZIP awareness + disk info)
        // ─────────────────────────────────────────────────────────────────
        // NavigateTo is NOT a RelayCommand because it has an optional bool parameter.
        // Call it directly from code-behind; breadcrumb buttons call it via Click handlers.
        public void NavigateTo(string path, bool pushHistory = true)
        {
            // ZIP virtual path?
            if (IsZipPath(path))
            {
                var (zf, ip) = SplitZipPath(path);
                NavigateToZip(zf, ip, pushHistory);
                return;
            }

            if (!Directory.Exists(path)) return;

            // Push to history
            if (pushHistory && path != CurrentPath)
            {
                if (_navHistory.Count == 0 && !string.IsNullOrEmpty(CurrentPath))
                    _navHistory.Add(CurrentPath);

                if (_navIndex < _navHistory.Count - 1 && _navIndex >= 0)
                    _navHistory.RemoveRange(_navIndex + 1, _navHistory.Count - _navIndex - 1);

                _navHistory.Add(path);
                _navIndex = _navHistory.Count - 1;

                OnPropertyChanged(nameof(CanNavigateBack));
                OnPropertyChanged(nameof(CanNavigateForward));
            }

            if (ActiveTabIndex >= 0 && ActiveTabIndex < Tabs.Count)
            {
                Tabs[ActiveTabIndex].Header = Path.GetFileName(path).IfEmpty(path);
                Tabs[ActiveTabIndex].Path = path;
            }

            IsInZip    = false;
            CurrentPath = path;
            FilterText  = string.Empty;
            IsFilterVisible = false;
            LoadDirectory(path);
            SetupWatcher(path);
            UpdateDriveInfo(path);
        }

        [RelayCommand]
        public void NavigateUp()
        {
            if (IsInZip)
            {
                var trimmed = ZipInternalPath.TrimEnd('/');
                int slash   = trimmed.LastIndexOf('/');
                if (slash < 0)
                    NavigateTo(Path.GetDirectoryName(ZipFilePath)!);  // exit ZIP
                else
                    NavigateToZip(ZipFilePath, trimmed.Substring(0, slash + 1));
            }
            else
            {
                var parent = Directory.GetParent(CurrentPath);
                if (parent != null) NavigateTo(parent.FullName);
            }
        }

        [RelayCommand]
        public void NavigateBack()
        {
            if (!CanNavigateBack) return;
            _navIndex--;
            OnPropertyChanged(nameof(CanNavigateBack));
            OnPropertyChanged(nameof(CanNavigateForward));
            NavigateTo(_navHistory[_navIndex], pushHistory: false);
        }

        [RelayCommand]
        public void NavigateForward()
        {
            if (!CanNavigateForward) return;
            _navIndex++;
            OnPropertyChanged(nameof(CanNavigateBack));
            OnPropertyChanged(nameof(CanNavigateForward));
            NavigateTo(_navHistory[_navIndex], pushHistory: false);
        }

        [RelayCommand]
        public void OpenItem(FileItem? item)
        {
            if (item == null) return;

            if (item.Name == "..")      { NavigateUp();          return; }
            if (item.IsDirectory)       { NavigateTo(item.FullPath); return; }

            // ZIP on real FS → navigate inside
            var ext = Path.GetExtension(item.FullPath ?? "").ToLowerInvariant();
            if (ext == ".zip" && item.FullPath != null && File.Exists(item.FullPath))
            {
                NavigateToZip(item.FullPath, "");
                return;
            }

            // Item inside ZIP → extract to temp
            if (IsInZip)
            {
                var tmp = ExtractZipEntryToTemp(ZipFilePath, ZipInternalPath + item.Name);
                if (tmp != null)
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        { FileName = tmp, UseShellExecute = true });
                return;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                { FileName = item.FullPath, UseShellExecute = true });
        }

        // ─────────────────────────────────────────────────────────────────
        // F4 – Open in text editor
        // ─────────────────────────────────────────────────────────────────
        [RelayCommand]
        public void OpenInEditor()
        {
            var item = SelectedItem;
            if (item == null || item.IsDirectory || item.Name == "..") return;

            var filePath = item.FullPath;
            if (IsInZip)
                filePath = ExtractZipEntryToTemp(ZipFilePath, ZipInternalPath + item.Name) ?? filePath;

            string[] editors =
            [
                @"C:\Program Files\Notepad++\notepad++.exe",
                @"C:\Program Files (x86)\Notepad++\notepad++.exe",
                "notepad.exe"
            ];
            foreach (var editor in editors)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        { FileName = editor, Arguments = $"\"{filePath}\"", UseShellExecute = true });
                    return;
                }
                catch { }
            }
        }

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
        // Folder Size Calculator  (Ctrl+L)
        // ─────────────────────────────────────────────────────────────────
        [RelayCommand]
        public async Task CalculateFolderSizes()
        {
            var folders = _allItems
                .Where(i => i.IsDirectory && i.Name != "..")
                .ToList();

            if (folders.Count == 0) return;

            // Calculate all folder sizes in parallel
            var tasks = folders.Select(folder => Task.Run(() =>
            {
                long size = GetDirectorySize(folder.FullPath);
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    folder.SizeBytes   = size;
                    folder.DisplaySize = FileItem.FormatSizePublic(size);
                });
            }));

            await Task.WhenAll(tasks);

            // Re-sort to reflect new sizes if sorting by size
            Application.Current.Dispatcher.Invoke(ApplySort);
        }

        private static long GetDirectorySize(string path)
        {
            try
            {
                return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                    .Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } });
            }
            catch { return 0; }
        }

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
            if (IsInZip) { _watcher = null; return; } // no watcher inside ZIP
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

        // ── Disk usage ────────────────────────────────────────────────────
        private void UpdateDriveInfo(string path)
        {
            try
            {
                var root = Path.GetPathRoot(path);
                if (string.IsNullOrEmpty(root)) return;
                var di    = new DriveInfo(root);
                if (!di.IsReady) return;
                long free  = di.AvailableFreeSpace;
                long total = di.TotalSize;
                long used  = total - free;
                DriveUsedPercent = total > 0 ? (double)used / total * 100 : 0;
                DriveSpaceText   = $"{FormatSize(free)} free / {FormatSize(total)}";
            }
            catch
            {
                DriveUsedPercent = 0;
                DriveSpaceText   = string.Empty;
            }
        }

        // ── ZIP navigation ────────────────────────────────────────────────

        /// <summary>Navigate into a ZIP file at a given internal path (e.g., "subfolder/").</summary>
        public void NavigateToZip(string zipFilePath, string internalPath, bool pushHistory = true)
        {
            if (!File.Exists(zipFilePath)) return;

            ZipFilePath     = zipFilePath;
            ZipInternalPath = internalPath;
            IsInZip         = true;

            var displayPath = zipFilePath + (string.IsNullOrEmpty(internalPath) ? "" : "\\" + internalPath.Replace('/', '\\'));

            if (pushHistory && displayPath != CurrentPath)
            {
                if (_navHistory.Count == 0 && !string.IsNullOrEmpty(CurrentPath))
                    _navHistory.Add(CurrentPath);

                if (_navIndex < _navHistory.Count - 1 && _navIndex >= 0)
                    _navHistory.RemoveRange(_navIndex + 1, _navHistory.Count - _navIndex - 1);

                _navHistory.Add(displayPath);
                _navIndex = _navHistory.Count - 1;

                OnPropertyChanged(nameof(CanNavigateBack));
                OnPropertyChanged(nameof(CanNavigateForward));
            }
            if (ActiveTabIndex >= 0 && ActiveTabIndex < Tabs.Count)
            {
                Tabs[ActiveTabIndex].Header = Path.GetFileName(zipFilePath);
                Tabs[ActiveTabIndex].Path = displayPath;
            }

            CurrentPath     = displayPath;
            FilterText      = string.Empty;
            IsFilterVisible = false;

            LoadZipDirectory(zipFilePath, internalPath);
        }

        private void LoadZipDirectory(string zipFilePath, string internalPath)
        {
            _allItems.Clear();
            Items.Clear();

            // Parent entry
            _allItems.Add(new FileItem { Name = "..", IsDirectory = true, DisplaySize = "<UP>", FullPath = "" });

            try
            {
                using var archive = ZipFile.OpenRead(zipFilePath);

                // Normalize internal path: must end with '/' if non-empty
                if (!string.IsNullOrEmpty(internalPath) && !internalPath.EndsWith('/'))
                    internalPath += '/';

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var entry in archive.Entries)
                {
                    var name = entry.FullName.Replace('\\', '/');
                    if (!name.StartsWith(internalPath, StringComparison.OrdinalIgnoreCase)) continue;

                    var remainder = name.Substring(internalPath.Length);
                    if (string.IsNullOrEmpty(remainder)) continue;

                    var parts     = remainder.Split('/');
                    var firstName = parts[0];
                    if (string.IsNullOrEmpty(firstName) || seen.Contains(firstName)) continue;
                    seen.Add(firstName);

                    bool isDir = parts.Length > 1 || remainder.EndsWith('/');

                    // Virtual full path for navigation
                    var vPath = zipFilePath + "\\" + (string.IsNullOrEmpty(internalPath)
                        ? firstName : internalPath.TrimEnd('/') + "\\" + firstName);

                    var item = new FileItem
                    {
                        Name        = firstName,
                        IsDirectory = isDir,
                        FullPath    = vPath,
                        Extension   = isDir ? "" : Path.GetExtension(firstName).TrimStart('.').ToUpper(),
                        Attributes  = "Z---"
                    };

                    if (!isDir)
                    {
                        item.SizeBytes    = entry.Length;
                        item.DisplaySize  = FileItem.FormatSizePublic(entry.Length);
                        item.DateModified = entry.LastWriteTime.DateTime;
                        item.Icon         = IconHelper.GetFileIconByExtension("." + item.Extension);
                    }
                    else
                    {
                        item.Icon = IconHelper.GetFolderIcon();
                    }

                    _allItems.Add(item);
                }
            }
            catch (Exception ex)
            {
                StatusText = $"ZIP error: {ex.Message}";
            }

            ApplySort();
            ApplyFilter();
        }

        private static bool IsZipPath(string path)
        {
            var lower = path.ToLowerInvariant().Replace('\\', '/');
            return lower.Contains(".zip/");
        }

        private static (string zipFile, string internalPath) SplitZipPath(string path)
        {
            var normalized = path.Replace('\\', '/');
            var lower      = normalized.ToLowerInvariant();
            int idx        = lower.IndexOf(".zip/", StringComparison.Ordinal);
            if (idx < 0) return (path, "");

            var zipFile      = path.Substring(0, idx + 4);   // up to .zip
            var internalPath = path.Substring(idx + 5);       // after .zip/
            return (zipFile, internalPath);
        }

        /// <summary>Extract a single ZIP entry to a temp folder and return the temp path.</summary>
        private static string? ExtractZipEntryToTemp(string zipFilePath, string entryName)
        {
            try
            {
                entryName = entryName.Replace('\\', '/').TrimStart('/');
                var tempDir = Path.Combine(Path.GetTempPath(), "SwiftPanel",
                    Path.GetFileNameWithoutExtension(zipFilePath));
                Directory.CreateDirectory(tempDir);

                using var archive = ZipFile.OpenRead(zipFilePath);
                var entry = archive.Entries.FirstOrDefault(e =>
                    e.FullName.Replace('\\', '/').Equals(entryName, StringComparison.OrdinalIgnoreCase));
                if (entry == null) return null;

                var destPath = Path.Combine(tempDir, entry.Name);
                entry.ExtractToFile(destPath, overwrite: true);
                return destPath;
            }
            catch { return null; }
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
