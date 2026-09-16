using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftPanel.Models;
using SwiftPanel.Services;
using SwiftPanel.Views;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace SwiftPanel.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty] private bool _isLeftActive = true;
        [ObservableProperty] private bool _isRightActive = false;

        partial void OnIsLeftActiveChanged(bool value)
        {
            OnPropertyChanged(nameof(ActivePanel));
            OnPropertyChanged(nameof(InactivePanel));
        }

        public FilePanelViewModel LeftPanel  { get; }
        public FilePanelViewModel RightPanel { get; }

        public FilePanelViewModel ActivePanel   => IsLeftActive ? LeftPanel : RightPanel;
        public FilePanelViewModel InactivePanel => IsLeftActive ? RightPanel : LeftPanel;

        // ── F3 Quick Viewer ───────────────────────────────────────────────
        public FileViewerViewModel Viewer { get; } = new();
        [ObservableProperty] private bool _isViewerOpen = false;

        public MainViewModel(AppSettings? settings = null)
        {
            var s = settings ?? new AppSettings();
            LeftPanel  = new FilePanelViewModel(s.LeftPath);
            RightPanel = new FilePanelViewModel(s.RightPath);
            ApplySettings(s);

            // Auto-refresh viewer when panel selection changes
            LeftPanel.PropertyChanged  += OnPanelSelectionChanged;
            RightPanel.PropertyChanged += OnPanelSelectionChanged;
        }

        private async void OnPanelSelectionChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FilePanelViewModel.SelectedItem) && IsViewerOpen)
                await Viewer.LoadAsync(ActivePanel.SelectedItem);
        }

        [RelayCommand]
        public async Task ToggleViewer()
        {
            IsViewerOpen = !IsViewerOpen;
            if (IsViewerOpen)
                await Viewer.LoadAsync(ActivePanel.SelectedItem);
            else
                Viewer.Clear();
        }

        [RelayCommand]
        public void CalculateFolderSizes()
            => ActivePanel.CalculateFolderSizesCommand.Execute(null);

        /// <summary>Apply persisted settings to both panels.</summary>
        public void ApplySettings(AppSettings s)
        {
            LeftPanel.ViewMode      = s.LeftViewMode;
            LeftPanel.CurrentSort   = s.LeftSort;
            LeftPanel.SortAscending = s.LeftSortAscending;
            LeftPanel.ShowHidden    = s.LeftShowHidden;

            RightPanel.ViewMode      = s.RightViewMode;
            RightPanel.CurrentSort   = s.RightSort;
            RightPanel.SortAscending = s.RightSortAscending;
            RightPanel.ShowHidden    = s.RightShowHidden;

            BookmarkService.Instance.Load(s.Bookmarks);
        }

        /// <summary>Capture current state into an AppSettings snapshot for persistence.</summary>
        public AppSettings CaptureSettings(double winW, double winH, double winL, double winT, bool maximized)
            => new AppSettings
            {
                LeftPath           = LeftPanel.CurrentPath,
                RightPath          = RightPanel.CurrentPath,
                LeftViewMode       = LeftPanel.ViewMode,
                RightViewMode      = RightPanel.ViewMode,
                LeftSort           = LeftPanel.CurrentSort,
                LeftSortAscending  = LeftPanel.SortAscending,
                RightSort          = RightPanel.CurrentSort,
                RightSortAscending = RightPanel.SortAscending,
                LeftShowHidden     = LeftPanel.ShowHidden,
                RightShowHidden    = RightPanel.ShowHidden,
                WindowWidth        = winW,
                WindowHeight       = winH,
                WindowLeft         = winL,
                WindowTop          = winT,
                Maximized          = maximized,
                Bookmarks          = BookmarkService.Instance.Export()
            };

        // ─────────────────────────────────────────────────────────────────
        // Panel focus switching (Tab key)
        // ─────────────────────────────────────────────────────────────────
        [RelayCommand]
        public void SwitchActivePanel()
        {
            IsLeftActive = !IsLeftActive;
            IsRightActive = !IsRightActive;
        }

        public void SetActivePanel(bool leftActive)
        {
            IsLeftActive = leftActive;
            IsRightActive = !leftActive;
        }

        // ─────────────────────────────────────────────────────────────────
        // New Folder (F7)
        // ─────────────────────────────────────────────────────────────────
        [RelayCommand]
        private void ToggleHidden()
            => ActivePanel.ToggleHiddenCommand.Execute(null);

        [RelayCommand]
        private void CopyPath()
            => ActivePanel.CopyPathToClipboardCommand.Execute(null);

        [RelayCommand]
        private void NewFolder()
        {
            var activePanel = ActivePanel;
            var newPath = Path.Combine(activePanel.CurrentPath, "New Folder");
            int i = 1;
            while (Directory.Exists(newPath))
                newPath = Path.Combine(activePanel.CurrentPath, $"New Folder ({i++})");
            try
            {
                Directory.CreateDirectory(newPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "New Folder Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Rename (F2)
        // ─────────────────────────────────────────────────────────────────
        [RelayCommand]
        private void Rename()
        {
            ActivePanel.StartRenameCommand.Execute(ActivePanel.SelectedItem);
        }

        // ─────────────────────────────────────────────────────────────────
        // Copy (F5) – async with progress dialog
        // ─────────────────────────────────────────────────────────────────
        [RelayCommand]
        private async Task Copy()
        {
            var src = ActivePanel;
            var dst = InactivePanel;
            var items = src.GetActionItems();
            if (items.Count == 0) return;

            // Collect all file paths to copy
            var allFiles = EnumerateFiles(items);
            int total = allFiles.Count;
            if (total == 0) return;

            var dialog = new ProgressDialog($"Copying {total} file(s) to:\n{dst.CurrentPath}")
            {
                Owner = Application.Current.MainWindow
            };

            dialog.Show();
            var token = dialog.CancellationToken;

            try
            {
                await Task.Run(() =>
                {
                    for (int i = 0; i < allFiles.Count; i++)
                    {
                        if (token.IsCancellationRequested) break;

                        var (srcPath, relativePath) = allFiles[i];
                        var destPath = Path.Combine(dst.CurrentPath, relativePath);
                        var destDir = Path.GetDirectoryName(destPath)!;
                        Directory.CreateDirectory(destDir);

                        // Skip if same path
                        if (srcPath.Equals(destPath, StringComparison.OrdinalIgnoreCase)) continue;

                        // Ask overwrite on UI thread if file exists
                        if (File.Exists(destPath))
                        {
                            bool overwrite = false;
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                var r = MessageBox.Show(
                                    $"'{Path.GetFileName(destPath)}' already exists.\nOverwrite?",
                                    "Confirm Overwrite",
                                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                                overwrite = r == MessageBoxResult.Yes;
                            });
                            if (!overwrite) continue;
                        }

                        File.Copy(srcPath, destPath, overwrite: true);

                        int pct = (int)((i + 1) / (double)total * 100);
                        dialog.UpdateProgress(pct, Path.GetFileName(srcPath));
                    }
                }, token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                MessageBox.Show(ex.Message, "Copy Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                dialog.Close();
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Move (F6) – async with progress dialog
        // ─────────────────────────────────────────────────────────────────
        [RelayCommand]
        private async Task Move()
        {
            var src = ActivePanel;
            var dst = InactivePanel;
            var items = src.GetActionItems();
            if (items.Count == 0) return;

            var allFiles = EnumerateFiles(items);
            int total = allFiles.Count;
            if (total == 0) return;

            var dialog = new ProgressDialog($"Moving {total} file(s) to:\n{dst.CurrentPath}")
            {
                Owner = Application.Current.MainWindow
            };

            dialog.Show();
            var token = dialog.CancellationToken;

            try
            {
                await Task.Run(() =>
                {
                    // For directories, try Directory.Move first (same volume = fast)
                    foreach (var item in items.Where(i => i.IsDirectory))
                    {
                        if (token.IsCancellationRequested) break;
                        var destPath = Path.Combine(dst.CurrentPath, item.Name);
                        try { Directory.Move(item.FullPath, destPath); }
                        catch
                        {
                            // Cross-volume: copy then delete
                            CopyDirectoryRecursive(item.FullPath, destPath);
                            Directory.Delete(item.FullPath, recursive: true);
                        }
                    }

                    // Move individual files
                    var fileItems = allFiles.Where(f => items.Any(i => !i.IsDirectory && i.FullPath == f.srcPath)).ToList();
                    for (int i = 0; i < fileItems.Count; i++)
                    {
                        if (token.IsCancellationRequested) break;
                        var (srcPath, relativePath) = fileItems[i];
                        var destPath = Path.Combine(dst.CurrentPath, relativePath);
                        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

                        if (File.Exists(destPath))
                        {
                            bool overwrite = false;
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                var r = MessageBox.Show(
                                    $"'{Path.GetFileName(destPath)}' already exists.\nOverwrite?",
                                    "Confirm Overwrite",
                                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                                overwrite = r == MessageBoxResult.Yes;
                            });
                            if (!overwrite) continue;
                        }

                        File.Move(srcPath, destPath, overwrite: true);

                        int pct = (int)((i + 1) / (double)fileItems.Count * 100);
                        dialog.UpdateProgress(pct, Path.GetFileName(srcPath));
                    }
                }, token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                MessageBox.Show(ex.Message, "Move Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                dialog.Close();
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Delete (F8 / Delete key)
        // ─────────────────────────────────────────────────────────────────
        [RelayCommand]
        private void Delete()
        {
            var panel = ActivePanel;
            var items = panel.GetActionItems();
            if (items.Count == 0) return;

            string msg = items.Count == 1
                ? $"Delete '{items[0].Name}'?"
                : $"Delete {items.Count} selected items?";

            var result = MessageBox.Show(msg, "Confirm Delete",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            foreach (var item in items)
            {
                try
                {
                    if (item.IsDirectory)
                        Directory.Delete(item.FullPath, recursive: true);
                    else
                        File.Delete(item.FullPath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"{item.Name}: {ex.Message}", "Delete Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Find (Ctrl+F / Alt+F7)
        // ─────────────────────────────────────────────────────────────────
        [RelayCommand]
        private void Find()
        {
            var dlg = new SearchDialog(ActivePanel.CurrentPath)
            {
                Owner = Application.Current.MainWindow
            };

            if (dlg.ShowDialog() == true && dlg.SelectedPath != null)
            {
                var path = dlg.SelectedPath;
                // Navigate to the folder containing the result
                var dir = File.Exists(path) ? Path.GetDirectoryName(path) : path;
                if (dir != null)
                    ActivePanel.NavigateTo(dir);
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Mark All / Unmark All / Invert Marks
        // ─────────────────────────────────────────────────────────────────
        [RelayCommand]
        private void MarkAll() => ActivePanel.MarkAll();

        [RelayCommand]
        private void UnmarkAll() => ActivePanel.UnmarkAll();

        [RelayCommand]
        private void InvertMarks() => ActivePanel.InvertMarks();

        // ─────────────────────────────────────────────────────────────────
        // Bookmarks (Ctrl+D = add, shown in FilePanel popup)
        // ─────────────────────────────────────────────────────────────────
        [RelayCommand]
        public void AddBookmark()
            => BookmarkService.Instance.Add(ActivePanel.CurrentPath);

        [RelayCommand]
        public void RemoveBookmark(BookmarkEntry entry)
            => BookmarkService.Instance.Bookmarks.Remove(entry);

        // ─────────────────────────────────────────────────────────────────
        // Multi-Rename (Ctrl+M)
        // ─────────────────────────────────────────────────────────────────
        [RelayCommand]
        public void MultiRename()
        {
            var panel = ActivePanel;
            var items = panel.GetActionItems()
                .Where(i => i.Name != "..")
                .ToList();

            if (items.Count == 0)
            {
                MessageBox.Show("Select or mark files to rename.",
                    "Multi-Rename", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new MultiRenameDialog(items, panel.CurrentPath)
            {
                Owner = Application.Current.MainWindow
            };
            dlg.ShowDialog();
            // FileSystemWatcher will auto-refresh the panel
        }

        // ─────────────────────────────────────────────────────────────────
        // Properties (Alt+Enter)
        // ─────────────────────────────────────────────────────────────────
        [RelayCommand]
        private void Properties()
        {
            var item = ActivePanel.SelectedItem;
            if (item == null || item.Name == "..") return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "rundll32.exe",
                    Arguments = $"shell32.dll,ShellExec_RunDLL properties \"{item.FullPath}\"",
                    UseShellExecute = false
                });
            }
            catch { }
        }

        // ─────────────────────────────────────────────────────────────────
        // Open Terminal here (Ctrl+T)
        // ─────────────────────────────────────────────────────────────────
        [RelayCommand]
        private void OpenTerminal()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    WorkingDirectory = ActivePanel.CurrentPath,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        // ─────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────
        /// <summary>Enumerate all files from a list of FileItems (recursing into directories).</summary>
        private static List<(string srcPath, string relativePath)> EnumerateFiles(IReadOnlyList<FileItem> items)
        {
            var result = new List<(string, string)>();
            foreach (var item in items)
            {
                if (item.IsDirectory)
                {
                    foreach (var f in Directory.EnumerateFiles(item.FullPath, "*", SearchOption.AllDirectories))
                    {
                        var rel = Path.Combine(item.Name, Path.GetRelativePath(item.FullPath, f));
                        result.Add((f, rel));
                    }
                }
                else
                {
                    result.Add((item.FullPath, item.Name));
                }
            }
            return result;
        }

        private static void CopyDirectoryRecursive(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var file in Directory.GetFiles(src))
                File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), overwrite: true);
            foreach (var dir in Directory.GetDirectories(src))
                CopyDirectoryRecursive(dir, Path.Combine(dst, Path.GetFileName(dir)));
        }
    }
}
