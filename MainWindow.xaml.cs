using System.Windows;
using System.Windows.Input;
using SwiftPanel.Services;
using SwiftPanel.ViewModels;

namespace SwiftPanel
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // Load persisted settings and create ViewModel with them
            var settings = SettingsService.Load();
            var vm       = new MainViewModel(settings);
            DataContext  = vm;

            // Restore window geometry
            if (settings.WindowLeft >= 0 && settings.WindowTop >= 0)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left   = settings.WindowLeft;
                Top    = settings.WindowTop;
                Width  = settings.WindowWidth;
                Height = settings.WindowHeight;
            }
            if (settings.Maximized)
                WindowState = WindowState.Maximized;

            // Watch IsViewerOpen to toggle the viewer column
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.IsViewerOpen))
                    SetViewerColumnWidth(vm.IsViewerOpen);
            };

            // Save settings when window closes
            Closing += (_, _) =>
            {
                if (DataContext is MainViewModel mvm)
                {
                    bool maximized = WindowState == WindowState.Maximized;
                    double w = maximized ? RestoreBounds.Width  : Width;
                    double h = maximized ? RestoreBounds.Height : Height;
                    double l = maximized ? RestoreBounds.Left   : Left;
                    double t = maximized ? RestoreBounds.Top    : Top;
                    SettingsService.Save(mvm.CaptureSettings(w, h, l, t, maximized));
                }
            };
        }

        private void SetViewerColumnWidth(bool open)
        {
            if (open)
            {
                ViewerCol.Width          = new GridLength(340, GridUnitType.Pixel);
                ViewerSplitterCol.Width  = new GridLength(4);
                ViewerSplitter.Visibility    = Visibility.Visible;
                ViewerPanelControl.Visibility= Visibility.Visible;
            }
            else
            {
                ViewerCol.Width          = new GridLength(0);
                ViewerSplitterCol.Width  = new GridLength(0);
                ViewerSplitter.Visibility    = Visibility.Collapsed;
                ViewerPanelControl.Visibility= Visibility.Collapsed;
            }
        }

        // Open the ★ Bookmarks dropdown
        private void OnBookmarksBtnClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;
            var menu = BookmarksMenu;

            // Remove old bookmark items (keep "Add" + separator)
            while (menu.Items.Count > 3)
                menu.Items.RemoveAt(menu.Items.Count - 1);

            var bookmarks = SwiftPanel.Services.BookmarkService.Instance.Bookmarks;
            NoBookmarksItem.Visibility = bookmarks.Count == 0
                ? Visibility.Visible : Visibility.Collapsed;

            foreach (var bm in bookmarks)
            {
                var bm_captured = bm;
                var mi = new System.Windows.Controls.MenuItem
                {
                    Header = $"📁 {bm.Name}  ({bm.Path})"
                };
                mi.Click += (_, _) =>
                {
                    // Navigate the active panel to this bookmark
                    if (DataContext is MainViewModel vm2)
                        vm2.ActivePanel.NavigateTo(bm_captured.Path);
                };

                // Right-click to remove
                var removeItem = new System.Windows.Controls.MenuItem { Header = "Remove bookmark" };
                removeItem.Click += (_, _) =>
                    SwiftPanel.Services.BookmarkService.Instance.Bookmarks.Remove(bm_captured);
                mi.ContextMenu = new System.Windows.Controls.ContextMenu();
                mi.ContextMenu.Items.Add(removeItem);

                menu.Items.Add(mi);
            }

            menu.PlacementTarget = (System.Windows.UIElement)sender;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        /// <summary>Global keyboard shortcuts that apply regardless of which control has focus.</summary>
        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            try { System.IO.File.AppendAllText("keylog.txt", $"[{System.DateTime.Now:HH:mm:ss}] Key: {e.Key} (SystemKey: {e.SystemKey}), Handled: {e.Handled}, Focused: {Keyboard.FocusedElement?.GetType().Name}\n"); } catch { }
            if (DataContext is not MainViewModel vm) return;
            if (e.Handled) return;

            var ctrl  = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            var alt   = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);

            // Don't intercept keys if focused element is a TextBox (e.g. Filter/Rename box)
            if (Keyboard.FocusedElement is System.Windows.Controls.TextBox)
            {
                try { System.IO.File.AppendAllText("keylog.txt", $"  -> Ignored because TextBox has focus\n"); } catch { }
                return;
            }
            var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

            switch (e.Key)
            {
                case Key.Tab when !ctrl && !alt:
                    vm.SwitchActivePanelCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.W when ctrl:
                    vm.ActivePanel.CloseTabCommand.Execute(vm.ActivePanel.ActiveTabIndex);
                    e.Handled = true;
                    break;

                case Key.H when ctrl:
                    vm.ToggleHiddenCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.C when ctrl && shift:
                    vm.CopyPathCommand.Execute(null);
                    e.Handled = true;
                    break;

                // Ctrl+D – Add current folder to bookmarks
                case Key.D when ctrl && !shift:
                    vm.AddBookmarkCommand.Execute(null);
                    e.Handled = true;
                    break;

                // Ctrl+M – Multi-rename
                case Key.M when ctrl:
                    vm.MultiRenameCommand.Execute(null);
                    e.Handled = true;
                    break;

                // F3 – Toggle Quick Viewer
                case Key.F3:
                    _ = vm.ToggleViewerCommand.ExecuteAsync(null);
                    e.Handled = true;
                    break;

                // F4 – Open in editor
                case Key.F4:
                    vm.ActivePanel.OpenInEditorCommand.Execute(null);
                    e.Handled = true;
                    break;

                // Ctrl+L – Calculate folder sizes
                case Key.L when ctrl:
                    vm.CalculateFolderSizesCommand.Execute(null);
                    e.Handled = true;
                    break;

                // Alt+Left – Navigate back
                case Key.Left when alt:
                    vm.ActivePanel.NavigateBackCommand.Execute(null);
                    e.Handled = true;
                    break;

                // Alt+Right – Navigate forward
                case Key.Right when alt:
                    vm.ActivePanel.NavigateForwardCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.F5:
                    try { System.IO.File.AppendAllText("keylog.txt", $"  -> Invoking CopyCommand\n"); } catch { }
                    vm.CopyCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.F6:
                    vm.MoveCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.F7:
                    vm.NewFolderCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.F8:
                    vm.DeleteCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.F2:
                    vm.RenameCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.F when ctrl:
                    vm.FindCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.T when ctrl:
                    vm.OpenTerminalCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.Enter when alt:
                    vm.PropertiesCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.Add:
                    vm.MarkAllCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.Subtract:
                    vm.UnmarkAllCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.Multiply:
                    vm.InvertMarksCommand.Execute(null);
                    e.Handled = true;
                    break;
            }
        }
    }
}