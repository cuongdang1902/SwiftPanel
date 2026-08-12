using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SwiftPanel.Models;
using SwiftPanel.ViewModels;


namespace SwiftPanel.Controls
{
    public partial class FilePanel : UserControl
    {
        // ─────────────────────────────────────────────────────────────────
        // Dependency Properties
        // ─────────────────────────────────────────────────────────────────
        public static readonly DependencyProperty IsActiveProperty =
            DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(FilePanel),
                new PropertyMetadata(false));

        public bool IsActive
        {
            get => (bool)GetValue(IsActiveProperty);
            set => SetValue(IsActiveProperty, value);
        }

        public static readonly DependencyProperty MainViewModelProperty =
            DependencyProperty.Register(nameof(MainViewModel), typeof(MainViewModel), typeof(FilePanel));

        public MainViewModel? MainViewModel
        {
            get => (MainViewModel?)GetValue(MainViewModelProperty);
            set => SetValue(MainViewModelProperty, value);
        }

        // ─────────────────────────────────────────────────────────────────
        // Constructor / Loaded
        // ─────────────────────────────────────────────────────────────────
        // Drag state
        private Point _dragStartPoint;
        private bool  _isDragging;

        public FilePanel()
        {
            InitializeComponent();
            Loaded           += OnLoaded;
            DataContextChanged += OnDataContextChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            PopulateDriveBar();
            FileListView.MouseDoubleClick += OnFileDoubleClick;
            FileListView.KeyDown          += OnFileKeyDown;
            FileListView.PreviewMouseLeftButtonDown += OnListMouseDown;
            FileListView.MouseMove        += OnListMouseMove;
            FileListView.GotFocus += (_, _) =>
            {
                MainViewModel?.SetActivePanel(Tag is string t && t == "Left");
            };
            RefreshTabBar();
            RefreshBreadcrumb();

            // Initialize with Details template (XAML can't set ItemTemplate in resources by default)
            SwitchViewMode(ViewMode.Details);
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is FilePanelViewModel oldVm)
            {
                oldVm.PropertyChanged            -= OnVmPropertyChanged;
                oldVm.Tabs.CollectionChanged     -= OnTabsChanged;
            }
            if (e.NewValue is FilePanelViewModel newVm)
            {
                newVm.PropertyChanged            += OnVmPropertyChanged;
                newVm.Tabs.CollectionChanged     += OnTabsChanged;
            }
        }

        private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(FilePanelViewModel.CurrentPath))
                RefreshBreadcrumb();
            if (e.PropertyName is nameof(FilePanelViewModel.ActiveTabIndex) or nameof(FilePanelViewModel.Tabs))
                RefreshTabBar();
            if (e.PropertyName is nameof(FilePanelViewModel.ViewMode)
                && DataContext is FilePanelViewModel vm)
                SwitchViewMode(vm.ViewMode);
        }

        private void OnTabsChanged(object? sender,
            System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
            => RefreshTabBar();

        // ─────────────────────────────────────────────────────────────────
        // Drive Bar
        // ─────────────────────────────────────────────────────────────────
        private void PopulateDriveBar()
        {
            DriveBarPanel.Children.Clear();
            foreach (var drive in System.IO.DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                var btn = new Button
                {
                    Content         = drive.Name.Replace("\\", "").ToUpper(),
                    Padding         = new Thickness(5, 1, 5, 1),
                    Margin          = new Thickness(1, 2, 1, 2),
                    FontSize        = 10,
                    Background      = TryFindResource("DriveButtonBrush") as Brush,
                    BorderBrush     = TryFindResource("DriveButtonBorderBrush") as Brush,
                    BorderThickness = new Thickness(1),
                    Cursor          = Cursors.Hand,
                    Tag             = drive.RootDirectory.FullName,
                    // Show free space in tooltip
                    ToolTip         = FilePanelViewModel.GetDriveTooltip(drive.RootDirectory.FullName)
                };
                btn.Click += (s, _) =>
                {
                    if (DataContext is FilePanelViewModel vm && btn.Tag is string path)
                        vm.NavigateToCommand.Execute(path);
                };
                DriveBarPanel.Children.Add(btn);
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Breadcrumb Bar (clickable segments)
        // ─────────────────────────────────────────────────────────────────
        private void RefreshBreadcrumb()
        {
            BreadcrumbPanel.Children.Clear();
            if (DataContext is not FilePanelViewModel vm) return;

            var parts = vm.BreadcrumbParts;
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                bool isLast = i == parts.Length - 1;

                var btn = new Button
                {
                    Content     = part.Label,
                    Style       = TryFindResource("BreadcrumbButtonStyle") as Style,
                    FontWeight  = isLast ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground  = isLast
                        ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 0, 128))
                        : Brushes.Black,
                    Tag         = part.FullPath,
                    ToolTip     = part.FullPath
                };
                btn.Click += (s, _) =>
                {
                    if (btn.Tag is string p) vm.NavigateToCommand.Execute(p);
                };
                BreadcrumbPanel.Children.Add(btn);

                // Separator "›" between parts
                if (!isLast)
                {
                    BreadcrumbPanel.Children.Add(new TextBlock
                    {
                        Text              = "›",
                        FontSize          = 10,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin            = new Thickness(0),
                        Foreground        = Brushes.Gray
                    });
                }
            }

            // Scroll to end so the deepest path segment is visible
            BreadcrumbScrollViewer.ScrollToRightEnd();
        }

        // ─────────────────────────────────────────────────────────────────
        // View Mode Switching
        // ─────────────────────────────────────────────────────────────────
        private async void SwitchViewMode(ViewMode mode)
        {
            // ── Switch ItemTemplate ──
            var templateKey = mode switch
            {
                ViewMode.List       => "ListItemTemplate",
                ViewMode.LargeIcons => "LargeIconsItemTemplate",
                ViewMode.Thumbnails => "ThumbnailsItemTemplate",
                _                   => "DetailsItemTemplate"
            };
            FileListView.ItemTemplate = (DataTemplate)FindResource(templateKey);

            // ── Switch ItemsPanel ──
            var panelKey = mode switch
            {
                ViewMode.List                              => "ListPanelTemplate",
                ViewMode.LargeIcons or ViewMode.Thumbnails => "IconsPanelTemplate",
                _                                         => "DetailsPanelTemplate"
            };
            FileListView.ItemsPanel = (ItemsPanelTemplate)FindResource(panelKey);

            // ── Virtualization: disabled for all WrapPanel modes ──
            VirtualizingPanel.SetIsVirtualizing(FileListView, mode == ViewMode.Details);

            // ── ScrollViewer direction per mode ──
            //   List      → WrapPanel Vertical  → items fill height then wrap RIGHT → scroll Horizontal
            //   Icons     → WrapPanel Horizontal → items fill width then wrap DOWN  → scroll Vertical
            //   Details   → Stack Vertical                                          → scroll Vertical
            ScrollViewer.SetHorizontalScrollBarVisibility(FileListView,
                mode == ViewMode.List ? ScrollBarVisibility.Auto : ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(FileListView,
                mode == ViewMode.List ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto);

            // ── Column headers: visible only in Details mode ──
            ColumnHeadersGrid.Visibility = mode == ViewMode.Details
                ? Visibility.Visible : Visibility.Collapsed;

            // ── Highlight active view button ──
            HighlightViewButton(mode);

            // ── List mode: WrapPanel(Vertical) needs a fixed Height to know when to wrap.
            //    We set it from the ListView's ActualHeight after layout is ready. ──
            FileListView.SizeChanged -= OnListViewSizeChanged;
            if (mode == ViewMode.List)
            {
                FileListView.SizeChanged += OnListViewSizeChanged;
                // Also apply immediately after next layout pass
#pragma warning disable CS4014
                Dispatcher.BeginInvoke(UpdateListPanelHeight,
                    System.Windows.Threading.DispatcherPriority.Loaded);
#pragma warning restore CS4014
            }

            // ── Load thumbnails asynchronously if needed ──
            if (mode is ViewMode.LargeIcons or ViewMode.Thumbnails
                && DataContext is FilePanelViewModel vm)
            {
                int size = mode == ViewMode.Thumbnails ? 96 : 48;
                await vm.LoadThumbnailsAsync(size);
            }
        }

        // ── List-mode helpers ──────────────────────────────────────────────

        private void OnListViewSizeChanged(object sender, SizeChangedEventArgs e)
            => UpdateListPanelHeight();

        private void UpdateListPanelHeight()
        {
            // Wait for actual height to be measured
            if (FileListView.ActualHeight <= 0) return;

            var panel = FindVisualChild<WrapPanel>(FileListView);
            if (panel != null)
            {
                // Give WrapPanel the available height so it wraps into columns
                panel.Height = FileListView.ActualHeight - 4;
            }
        }

        /// <summary>Depth-first search for a child of type T in the visual tree.</summary>
        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match) return match;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        private void HighlightViewButton(ViewMode mode)
        {
            foreach (UIElement child in ViewModeButtonsPanel.Children)
            {
                if (child is Button btn && btn.Tag is string tag
                    && Enum.TryParse<ViewMode>(tag, out var btnMode))
                {
                    btn.Background = btnMode == mode
                        ? new SolidColorBrush(Color.FromRgb(0x90, 0xB8, 0xD8))
                        : Brushes.Transparent;
                }
            }
        }

        // Called by view mode buttons in drive bar
        private void OnViewModeClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not FilePanelViewModel vm) return;
            if (sender is Button btn && btn.Tag is string tag)
                vm.SetViewModeCommand.Execute(tag);
        }

        // Context menu view submenu
        private void OnContextViewDetails(object s, RoutedEventArgs e)
            => (DataContext as FilePanelViewModel)?.SetViewModeCommand.Execute("Details");
        private void OnContextViewList(object s, RoutedEventArgs e)
            => (DataContext as FilePanelViewModel)?.SetViewModeCommand.Execute("List");
        private void OnContextViewLargeIcons(object s, RoutedEventArgs e)
            => (DataContext as FilePanelViewModel)?.SetViewModeCommand.Execute("LargeIcons");
        private void OnContextViewThumbnails(object s, RoutedEventArgs e)
            => (DataContext as FilePanelViewModel)?.SetViewModeCommand.Execute("Thumbnails");

        // ─────────────────────────────────────────────────────────────────
        // Column Header Sort
        // ─────────────────────────────────────────────────────────────────
        private void OnColumnHeaderClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not FilePanelViewModel vm) return;
            if (sender is Button btn && btn.Tag is string column)
                vm.SortByCommand.Execute(column);
        }

        // ─────────────────────────────────────────────────────────────────
        // Tab Bar
        // ─────────────────────────────────────────────────────────────────
        private void RefreshTabBar()
        {
            TabBarPanel.Children.Clear();
            if (DataContext is not FilePanelViewModel vm) return;

            for (int i = 0; i < vm.Tabs.Count; i++)
            {
                var tab = vm.Tabs[i];
                int capturedIdx = i;
                bool isActive   = i == vm.ActiveTabIndex;

                var header = new TextBlock
                {
                    Text              = tab.Header,
                    FontSize          = 10,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin            = new Thickness(0, 0, 4, 0),
                    MaxWidth          = 120,
                    TextTrimming      = TextTrimming.CharacterEllipsis,
                    FontWeight        = isActive ? FontWeights.SemiBold : FontWeights.Normal
                };

                var closeBtn = new Button
                {
                    Content         = "×",
                    Background      = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    FontSize        = 11,
                    Padding         = new Thickness(2, 0, 2, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor          = Cursors.Hand,
                    Visibility      = vm.Tabs.Count > 1 ? Visibility.Visible : Visibility.Collapsed,
                    ToolTip         = "Close tab  (Ctrl+W)"
                };
                closeBtn.Click += (_, ev) =>
                {
                    ev.Handled = true;
                    vm.CloseTabCommand.Execute(capturedIdx);
                };

                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(header);
                sp.Children.Add(closeBtn);

                var tabBorder = new Border
                {
                    Background = isActive
                        ? TryFindResource("TabActiveBrush") as Brush ?? Brushes.White
                        : TryFindResource("TabInactiveBrush") as Brush ?? Brushes.LightGray,
                    BorderBrush     = TryFindResource("TabBorderBrush") as Brush ?? Brushes.Gray,
                    BorderThickness = new Thickness(1, 0, 1, isActive ? 0 : 1),
                    Padding         = new Thickness(7, 2, 4, 2),
                    Cursor          = Cursors.Hand,
                    Child           = sp,
                    ToolTip         = tab.Path
                };
                tabBorder.MouseLeftButtonDown += (_, _) =>
                    vm.SwitchTabCommand.Execute(capturedIdx);

                TabBarPanel.Children.Add(tabBorder);
            }

            // "+" new tab button
            var addBtn = new Button
            {
                Content         = "+",
                Background      = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontSize        = 14,
                Padding         = new Thickness(5, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Stretch,
                Cursor          = Cursors.Hand,
                ToolTip         = "New Tab (Ctrl+T)"
            };
            addBtn.Click += (_, _) =>
            {
                if (DataContext is FilePanelViewModel v) v.AddTabCommand.Execute(null);
            };
            TabBarPanel.Children.Add(addBtn);
        }

        // ─────────────────────────────────────────────────────────────────
        // File List – Mouse & Keyboard
        // ─────────────────────────────────────────────────────────────────
        private void OnFileDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is FilePanelViewModel vm && vm.SelectedItem != null)
                vm.OpenItemCommand.Execute(vm.SelectedItem);
        }

        // ─────────────────────────────────────────────────────────────────
        // Drag & Drop – SOURCE side
        // ─────────────────────────────────────────────────────────────────
        private void OnListMouseDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(FileListView);
            _isDragging = false;
        }

        private void OnListMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _isDragging) return;
            var pos = e.GetPosition(FileListView);
            var diff = _dragStartPoint - pos;
            if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            if (DataContext is not FilePanelViewModel vm) return;
            var items = vm.GetActionItems();
            if (items.Count == 0) return;

            var paths = items.Select(i => i.FullPath).ToArray();
            var dataObj = new DataObject(DataFormats.FileDrop, paths);
            _isDragging = true;
            DragDrop.DoDragDrop(FileListView, dataObj, DragDropEffects.Copy | DragDropEffects.Move);
            _isDragging = false;
        }

        // ─────────────────────────────────────────────────────────────────
        // Drag & Drop – TARGET side
        // ─────────────────────────────────────────────────────────────────
        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
                    ? DragDropEffects.Move
                    : DragDropEffects.Copy;
                FileListView.BorderBrush = new SolidColorBrush(Color.FromRgb(30, 111, 191));
                FileListView.BorderThickness = new Thickness(2, 2, 2, 2);
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void OnDragLeave(object sender, DragEventArgs e)
        {
            FileListView.BorderBrush = TryFindResource("FileListBorderBrush") as Brush
                ?? Brushes.Gray;
            FileListView.BorderThickness = new Thickness(1, 0, 1, 0);
        }

        private void OnFileDrop(object sender, DragEventArgs e)
        {
            OnDragLeave(sender, e);  // reset border

            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            if (DataContext is not FilePanelViewModel vm) return;

            var srcPaths = (string[])e.Data.GetData(DataFormats.FileDrop);
            bool isMove  = e.KeyStates.HasFlag(DragDropKeyStates.ShiftKey);
            var dest     = vm.CurrentPath;

            foreach (var srcPath in srcPaths)
            {
                try
                {
                    var name     = System.IO.Path.GetFileName(srcPath);
                    var destPath = System.IO.Path.Combine(dest, name);

                    if (System.IO.File.Exists(srcPath))
                    {
                        System.IO.File.Copy(srcPath, destPath, overwrite: false);
                        if (isMove) System.IO.File.Delete(srcPath);
                    }
                    else if (System.IO.Directory.Exists(srcPath))
                    {
                        if (isMove)
                            System.IO.Directory.Move(srcPath, destPath);
                        else
                            CopyDirRecursive(srcPath, destPath);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, isMove ? "Move Error" : "Copy Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private static void CopyDirRecursive(string src, string dst)
        {
            System.IO.Directory.CreateDirectory(dst);
            foreach (var f in System.IO.Directory.GetFiles(src))
                System.IO.File.Copy(f, System.IO.Path.Combine(dst, System.IO.Path.GetFileName(f)));
            foreach (var d in System.IO.Directory.GetDirectories(src))
                CopyDirRecursive(d, System.IO.Path.Combine(dst, System.IO.Path.GetFileName(d)));
        }

        private void OnFileKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not FilePanelViewModel vm) return;
            bool ctrl  = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

            switch (e.Key)
            {
                case Key.Enter:
                    if (vm.SelectedItem != null) vm.OpenItemCommand.Execute(vm.SelectedItem);
                    e.Handled = true; break;

                case Key.Back:
                    vm.NavigateUpCommand.Execute(null);
                    e.Handled = true; break;

                case Key.Space:
                    vm.ToggleMarkCommand.Execute(vm.SelectedItem);
                    MoveSelection(1);
                    e.Handled = true; break;

                case Key.F2:
                    vm.StartRenameCommand.Execute(vm.SelectedItem);
                    e.Handled = true; break;

                case Key.F5:
                    MainViewModel?.CopyCommand.Execute(null);
                    e.Handled = true; break;

                case Key.F6:
                    MainViewModel?.MoveCommand.Execute(null);
                    e.Handled = true; break;

                case Key.F7:
                    MainViewModel?.NewFolderCommand.Execute(null);
                    e.Handled = true; break;

                case Key.F8:
                case Key.Delete:
                    MainViewModel?.DeleteCommand.Execute(null);
                    e.Handled = true; break;

                // Ctrl+H = toggle hidden files
                case Key.H when ctrl:
                    vm.ToggleHiddenCommand.Execute(null);
                    e.Handled = true; break;

                // Ctrl+F = open quick filter
                case Key.F when ctrl:
                    vm.ShowFilter();
                    FilterBox.Focus();
                    e.Handled = true; break;

                // Ctrl+Shift+C = copy path
                case Key.C when ctrl && shift:
                    vm.CopyPathToClipboardCommand.Execute(null);
                    e.Handled = true; break;

                // Ctrl+1/2/3/4 = switch view mode
                case Key.D1 or Key.NumPad1 when ctrl:
                    vm.SetViewModeCommand.Execute("Details");    e.Handled = true; break;
                case Key.D2 or Key.NumPad2 when ctrl:
                    vm.SetViewModeCommand.Execute("List");       e.Handled = true; break;
                case Key.D3 or Key.NumPad3 when ctrl:
                    vm.SetViewModeCommand.Execute("LargeIcons"); e.Handled = true; break;
                case Key.D4 or Key.NumPad4 when ctrl:
                    vm.SetViewModeCommand.Execute("Thumbnails"); e.Handled = true; break;

                case Key.Add when Keyboard.Modifiers == ModifierKeys.None:
                    vm.MarkAll(); e.Handled = true; break;

                case Key.Subtract when Keyboard.Modifiers == ModifierKeys.None:
                    vm.UnmarkAll(); e.Handled = true; break;

                case Key.Multiply when Keyboard.Modifiers == ModifierKeys.None:
                    vm.InvertMarks(); e.Handled = true; break;
            }
        }

        private void MoveSelection(int delta)
        {
            int newIdx = Math.Clamp(FileListView.SelectedIndex + delta, 0, FileListView.Items.Count - 1);
            FileListView.SelectedIndex = newIdx;
            FileListView.ScrollIntoView(FileListView.SelectedItem);
        }

        // ─────────────────────────────────────────────────────────────────
        // Quick Filter Bar
        // ─────────────────────────────────────────────────────────────────
        private void OnFilterBoxGotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb) tb.SelectAll();
        }

        private void OnFilterBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (DataContext is FilePanelViewModel vm) vm.HideFilter();
                FileListView.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter || e.Key == Key.Down)
            {
                // Move focus back to list
                FileListView.Focus();
                e.Handled = true;
            }
        }

        private void OnFilterClose(object sender, RoutedEventArgs e)
        {
            if (DataContext is FilePanelViewModel vm) vm.HideFilter();
            FileListView.Focus();
        }

        // ─────────────────────────────────────────────────────────────────
        // Inline Rename – *** FIXED: get item from row DataContext ***
        // ─────────────────────────────────────────────────────────────────
        private static FileItem? GetRowItem(object sender)
            => (sender as FrameworkElement)?.DataContext as FileItem;

        private void OnRenameBoxGotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb) tb.SelectAll();
        }

        private void OnRenameBoxLostFocus(object sender, RoutedEventArgs e)
        {
            // Use the actual row item (not SelectedItem which may differ)
            var item = GetRowItem(sender);
            if (DataContext is FilePanelViewModel vm && item != null)
                vm.CommitRenameCommand.Execute(item);
        }

        private void OnRenameBoxKeyDown(object sender, KeyEventArgs e)
        {
            var item = GetRowItem(sender);
            if (DataContext is not FilePanelViewModel vm || item == null) return;

            if (e.Key == Key.Enter)
            {
                vm.CommitRenameCommand.Execute(item);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                vm.CancelRenameCommand.Execute(item);
                e.Handled = true;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Context Menu
        // ─────────────────────────────────────────────────────────────────
        private void OnContextOpen(object s, RoutedEventArgs e)
        {
            if (DataContext is FilePanelViewModel vm) vm.OpenItemCommand.Execute(vm.SelectedItem);
        }
        private void OnContextCopy(object s, RoutedEventArgs e)         => MainViewModel?.CopyCommand.Execute(null);
        private void OnContextMove(object s, RoutedEventArgs e)         => MainViewModel?.MoveCommand.Execute(null);
        private void OnContextDelete(object s, RoutedEventArgs e)       => MainViewModel?.DeleteCommand.Execute(null);
        private void OnContextRename(object s, RoutedEventArgs e)
        {
            if (DataContext is FilePanelViewModel vm) vm.StartRenameCommand.Execute(vm.SelectedItem);
        }
        private void OnContextMark(object s, RoutedEventArgs e)
        {
            if (DataContext is FilePanelViewModel vm) vm.ToggleMarkCommand.Execute(vm.SelectedItem);
        }
        private void OnContextMarkAll(object s, RoutedEventArgs e)
        {
            if (DataContext is FilePanelViewModel vm) vm.MarkAll();
        }
        private void OnContextUnmarkAll(object s, RoutedEventArgs e)
        {
            if (DataContext is FilePanelViewModel vm) vm.UnmarkAll();
        }
        private void OnContextInvertMarks(object s, RoutedEventArgs e)
        {
            if (DataContext is FilePanelViewModel vm) vm.InvertMarks();
        }
        private void OnContextCopyPath(object s, RoutedEventArgs e)
        {
            if (DataContext is FilePanelViewModel vm) vm.CopyPathToClipboardCommand.Execute(null);
        }
        private void OnContextToggleHidden(object s, RoutedEventArgs e)
        {
            if (DataContext is FilePanelViewModel vm) vm.ToggleHiddenCommand.Execute(null);
        }
        private void OnContextNewFolder(object s, RoutedEventArgs e)    => MainViewModel?.NewFolderCommand.Execute(null);
        private void OnContextTerminal(object s, RoutedEventArgs e)     => MainViewModel?.OpenTerminalCommand.Execute(null);
        private void OnContextProperties(object s, RoutedEventArgs e)   => MainViewModel?.PropertiesCommand.Execute(null);
    }
}
