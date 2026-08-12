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
            DataContext  = new MainViewModel(settings);

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

            // Save settings when window closes
            Closing += (_, _) =>
            {
                if (DataContext is MainViewModel vm)
                {
                    bool maximized = WindowState == WindowState.Maximized;
                    double w = maximized ? RestoreBounds.Width  : Width;
                    double h = maximized ? RestoreBounds.Height : Height;
                    double l = maximized ? RestoreBounds.Left   : Left;
                    double t = maximized ? RestoreBounds.Top    : Top;
                    SettingsService.Save(vm.CaptureSettings(w, h, l, t, maximized));
                }
            };
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
            if (DataContext is not MainViewModel vm) return;
            if (e.Handled) return;

            var ctrl  = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            var alt   = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
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

                case Key.F5:
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