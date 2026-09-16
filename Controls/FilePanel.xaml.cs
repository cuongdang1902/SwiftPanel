using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
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
            {
                RefreshBreadcrumb();
                RefreshDriveBarHighlight();   // update active-drive blue border
                RefreshTabBar();              // update tab names instantly
            }
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
        // ─────────────────────────────────────────────────────────────────
        // Drive Bar  (SpeedCommander 13 style)
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a vector HDD icon matching SpeedCommander 13's compact drive-bar style.
        /// Shape: rounded-rect body + top-cap + small platter circle.
        /// </summary>
        private static UIElement MakeDriveIcon(DriveType driveType)
        {
            // Colours
            var bodyColor = driveType switch
            {
                DriveType.CDRom   => Color.FromRgb(0x80, 0x80, 0x80),
                DriveType.Network => Color.FromRgb(0x00, 0x70, 0xC0),
                DriveType.Ram     => Color.FromRgb(0x20, 0x90, 0x40),
                _                 => Color.FromRgb(0x1F, 0x5F, 0xB0)   // Fixed / Removable
            };
            var bodyBrush  = new SolidColorBrush(bodyColor);
            var lightBrush = new SolidColorBrush(Color.FromRgb(0xB8, 0xD4, 0xF0));
            var darkBrush  = new SolidColorBrush(Color.FromRgb(0x0A, 0x30, 0x70));

            var canvas = new Canvas { Width = 14, Height = 11,
                                      VerticalAlignment = VerticalAlignment.Center };

            if (driveType == DriveType.CDRom)
            {
                // CD: circle with hole
                var outer = new Ellipse
                {
                    Width = 11, Height = 11,
                    Fill   = Brushes.Silver,
                    Stroke = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60)),
                    StrokeThickness = 0.8
                };
                Canvas.SetLeft(outer, 1); Canvas.SetTop(outer, 0);

                var highlight = new Ellipse
                {
                    Width = 6, Height = 3,
                    Fill = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255))
                };
                Canvas.SetLeft(highlight, 3); Canvas.SetTop(highlight, 1);

                var hole = new Ellipse
                {
                    Width = 3, Height = 3,
                    Fill = new SolidColorBrush(Color.FromRgb(0xE8, 0xEC, 0xEF))
                };
                Canvas.SetLeft(hole, 4); Canvas.SetTop(hole, 4);

                canvas.Children.Add(outer);
                canvas.Children.Add(highlight);
                canvas.Children.Add(hole);
            }
            else
            {
                // HDD body – main rectangle
                var body = new Rectangle
                {
                    Width = 14, Height = 9,
                    Fill   = bodyBrush,
                    RadiusX = 1.5, RadiusY = 1.5
                };
                Canvas.SetLeft(body, 0); Canvas.SetTop(body, 2);

                // Top cap (narrower, slightly lighter)
                var cap = new Rectangle
                {
                    Width = 10, Height = 2,
                    Fill   = lightBrush,
                    RadiusX = 1, RadiusY = 1
                };
                Canvas.SetLeft(cap, 2); Canvas.SetTop(cap, 0);

                // Platter circle
                var platter = new Ellipse
                {
                    Width = 5, Height = 5,
                    Fill   = lightBrush,
                    Stroke = darkBrush, StrokeThickness = 0.6
                };
                Canvas.SetLeft(platter, 1); Canvas.SetTop(platter, 3);

                // Access arm line
                var arm = new Line
                {
                    X1 = 7, Y1 = 5.5, X2 = 13, Y2 = 4,
                    Stroke = darkBrush, StrokeThickness = 0.8
                };

                // Highlight stripe (top)
                var shine = new Rectangle
                {
                    Width = 14, Height = 2,
                    Fill  = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                    RadiusX = 1.5, RadiusY = 0
                };
                Canvas.SetLeft(shine, 0); Canvas.SetTop(shine, 2);

                canvas.Children.Add(body);
                canvas.Children.Add(cap);
                canvas.Children.Add(platter);
                canvas.Children.Add(arm);
                canvas.Children.Add(shine);
            }
            return canvas;
        }

        private void PopulateDriveBar()
        {
            DriveBarPanel.Children.Clear();

            var hoverBg      = TryFindResource("DriveButtonHoverBrush") as Brush
                               ?? new SolidColorBrush(Color.FromRgb(0xC7, 0xE3, 0xF7));
            var activeBorder = TryFindResource("ActivePanelBorderBrush") as Brush
                               ?? new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC));
            var normalFg     = TryFindResource("PrimaryTextBrush")      as Brush ?? Brushes.Black;

            string? currentRoot = null;
            if (DataContext is FilePanelViewModel vm0)
                currentRoot = System.IO.Path.GetPathRoot(vm0.CurrentPath);

            bool first = true;
            foreach (var drive in System.IO.DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;

                var root   = drive.RootDirectory.FullName;
                var letter = drive.Name.TrimEnd('\\').ToUpper(); // e.g. "C:"

                bool isActive = string.Equals(root, currentRoot,
                                              StringComparison.OrdinalIgnoreCase);

                // ── Separator between drives ──────────────────────────────
                if (!first)
                {
                    DriveBarPanel.Children.Add(new TextBlock
                    {
                        Text              = "—",
                        FontSize          = 9,
                        Foreground        = new SolidColorBrush(Color.FromRgb(0xA0, 0xA8, 0xB0)),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin            = new Thickness(0)
                    });
                }
                first = false;

                // ── Drive icon (vector) ──────────────────────────────────
                var icon = MakeDriveIcon(drive.DriveType);

                // ── Drive letter ─────────────────────────────────────────
                var letterTb = new TextBlock
                {
                    Text              = letter,
                    FontSize          = 10,
                    FontWeight        = FontWeights.SemiBold,
                    Foreground        = normalFg,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin            = new Thickness(2, 0, 0, 0)
                };

                var content = new StackPanel { Orientation = Orientation.Horizontal };
                content.Children.Add(icon);
                content.Children.Add(letterTb);

                // ── Button border ─────────────────────────────────────────
                var btn = new Border
                {
                    Background      = Brushes.Transparent,
                    BorderBrush     = isActive ? activeBorder : Brushes.Transparent,
                    BorderThickness = new Thickness(1),
                    CornerRadius    = new CornerRadius(2),
                    Padding         = new Thickness(3, 1, 3, 1),
                    Margin          = new Thickness(0, 2, 0, 2),
                    Cursor          = Cursors.Hand,
                    Tag             = root,
                    ToolTip         = FilePanelViewModel.GetDriveTooltip(root),
                    Child           = content
                };

                btn.MouseEnter += (_, _) => btn.Background = hoverBg;
                btn.MouseLeave += (_, _) => btn.Background = Brushes.Transparent;
                btn.MouseLeftButtonDown += (_, _) =>
                {
                    if (DataContext is FilePanelViewModel vm && btn.Tag is string path)
                        vm.NavigateTo(path);
                };

                DriveBarPanel.Children.Add(btn);
            }
        }

        /// <summary>
        /// Refreshes the active-drive highlight border after navigation.
        /// Call this whenever CurrentPath changes.
        /// </summary>
        private void RefreshDriveBarHighlight()
        {
            if (DataContext is not FilePanelViewModel vm) return;
            string? currentRoot = System.IO.Path.GetPathRoot(vm.CurrentPath);

            var activeBorder = TryFindResource("ActivePanelBorderBrush") as Brush
                               ?? Brushes.DodgerBlue;

            foreach (var child in DriveBarPanel.Children)
            {
                if (child is not Border btn) continue;
                if (btn.Tag is not string root) continue;
                bool isActive = string.Equals(root, currentRoot,
                                              StringComparison.OrdinalIgnoreCase);
                btn.BorderBrush = isActive ? activeBorder : Brushes.Transparent;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Breadcrumb Bar (clickable segments)
        // ─────────────────────────────────────────────────────────────────
        private void RefreshBreadcrumb()
        {
            BreadcrumbPanel.Children.Clear();
            if (DataContext is not FilePanelViewModel vm) return;

            var normalFg   = TryFindResource("BreadcrumbForegroundBrush")  as Brush ?? Brushes.DimGray;
            var lastFg     = TryFindResource("BreadcrumbLastSegmentBrush") as Brush ?? Brushes.Navy;
            var separatorFg= TryFindResource("BreadcrumbSeparatorBrush")   as Brush ?? Brushes.Gray;

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
                    Foreground  = isLast ? lastFg : normalFg,
                    Tag         = part.FullPath,
                    ToolTip     = part.FullPath
                };
                btn.Click += (s, _) =>
                {
                    if (btn.Tag is string p) vm.NavigateTo(p);
                };
                BreadcrumbPanel.Children.Add(btn);

                // Separator "›" between parts
                if (!isLast)
                {
                    BreadcrumbPanel.Children.Add(new TextBlock
                    {
                        Text              = "›",
                        FontSize          = 11,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin            = new Thickness(0),
                        Foreground        = separatorFg
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
            _currentViewMode = mode;  // track for SizeChanged handler

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
            //   Details   → VirtualizingStack (vertical)     → V-scroll only
            //   List      → WrapPanel(Vertical) multi-column  → H-scroll only (height-bounded)
            //   Icons     → WrapPanel(Horizontal) grid        → V-scroll only, NO H-scroll so items wrap
            var hScroll = mode == ViewMode.List ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
            var vScroll = mode == ViewMode.List ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
            ScrollViewer.SetHorizontalScrollBarVisibility(FileListView, hScroll);
            ScrollViewer.SetVerticalScrollBarVisibility(FileListView, vScroll);

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
#pragma warning disable CS4014
                Dispatcher.BeginInvoke(UpdateListPanelHeight,
                    System.Windows.Threading.DispatcherPriority.Loaded);
#pragma warning restore CS4014
            }

            // ── Icons/Thumbnails mode: WrapPanel(Horizontal) needs MaxWidth = ListView width
            //    so it wraps items into rows (grid) instead of a single horizontal line.
            //    The SizeChanged handler keeps it in sync when the panel is resized. ──
            if (mode is ViewMode.LargeIcons or ViewMode.Thumbnails)
            {
#pragma warning disable CS4014
                Dispatcher.BeginInvoke(UpdateIconsPanelWidth,
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

        // ── List-mode helpers ─────────────────────────────────────────────────────────

        // Tracks current view mode for size-change handler
        private ViewMode _currentViewMode = ViewMode.Details;

        private void OnFileListSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_currentViewMode is ViewMode.LargeIcons or ViewMode.Thumbnails)
                UpdateIconsPanelWidth();
            else if (_currentViewMode == ViewMode.List)
                UpdateListPanelHeight();
        }

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

        /// <summary>
        /// Constrains the WrapPanel(Horizontal) max-width to the ListView's current width
        /// so icons/thumbnails wrap into grid rows instead of a single horizontal line.
        /// </summary>
        private void UpdateIconsPanelWidth()
        {
            if (FileListView.ActualWidth <= 0) return;

            var panel = FindVisualChild<WrapPanel>(FileListView);
            if (panel != null)
            {
                // Remove 4px for scrollbar gutter and border
                panel.MaxWidth = FileListView.ActualWidth - 4;
                panel.Width    = double.NaN; // auto
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

            var tabActiveBrush   = TryFindResource("TabActiveBrush")   as Brush ?? Brushes.White;
            var tabInactiveBrush = TryFindResource("TabInactiveBrush") as Brush ?? Brushes.LightGray;
            var tabHoverBrush    = TryFindResource("TabHoverBrush")    as Brush ?? Brushes.AliceBlue;
            var tabBorderBrush   = TryFindResource("TabBorderBrush")   as Brush ?? Brushes.Gray;
            var closeHoverBrush  = TryFindResource("TabCloseHoverBrush") as Brush ?? Brushes.Red;

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

                // Close button with hover highlight
                var closeBtn = new Border
                {
                    Width           = 16,
                    Height          = 16,
                    CornerRadius    = new CornerRadius(3),
                    Background      = Brushes.Transparent,
                    Cursor          = Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Center,
                    Visibility      = vm.Tabs.Count > 1 ? Visibility.Visible : Visibility.Collapsed,
                    ToolTip         = "Close tab  (Ctrl+W)",
                    Child           = new TextBlock
                    {
                        Text                = "×",
                        FontSize            = 12,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment   = VerticalAlignment.Center,
                        Foreground          = isActive ? Brushes.DimGray : Brushes.Gray
                    }
                };
                closeBtn.MouseEnter += (_, _) =>
                {
                    closeBtn.Background = closeHoverBrush;
                    ((TextBlock)closeBtn.Child).Foreground = Brushes.White;
                };
                closeBtn.MouseLeave += (_, _) =>
                {
                    closeBtn.Background = Brushes.Transparent;
                    ((TextBlock)closeBtn.Child).Foreground = isActive ? Brushes.DimGray : Brushes.Gray;
                };
                closeBtn.MouseLeftButtonDown += (_, ev) =>
                {
                    ev.Handled = true;
                    vm.CloseTabCommand.Execute(capturedIdx);
                };

                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(header);
                sp.Children.Add(closeBtn);

                var currentBg = isActive ? tabActiveBrush : tabInactiveBrush;
                var tabBorder = new Border
                {
                    Background      = currentBg,
                    BorderBrush     = tabBorderBrush,
                    BorderThickness = new Thickness(1, 0, 1, isActive ? 0 : 1),
                    Padding         = new Thickness(8, 3, 5, 3),
                    CornerRadius    = new CornerRadius(3, 3, 0, 0),
                    Cursor          = Cursors.Hand,
                    Child           = sp,
                    ToolTip         = tab.Path
                };

                // Hover effect for inactive tabs
                if (!isActive)
                {
                    tabBorder.MouseEnter += (_, _) => tabBorder.Background = tabHoverBrush;
                    tabBorder.MouseLeave += (_, _) => tabBorder.Background = tabInactiveBrush;
                }

                tabBorder.MouseLeftButtonDown += (_, _) =>
                    vm.SwitchTabCommand.Execute(capturedIdx);

                TabBarPanel.Children.Add(tabBorder);
            }

            // "+" new tab button
            var addBorder = new Border
            {
                Background      = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding         = new Thickness(6, 3, 6, 3),
                Cursor          = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Stretch,
                ToolTip         = "New Tab  (Ctrl+T)",
                Child           = new TextBlock
                {
                    Text                = "+",
                    FontSize            = 15,
                    Foreground          = Brushes.DimGray,
                    VerticalAlignment   = VerticalAlignment.Center
                }
            };
            addBorder.MouseEnter += (_, _) => addBorder.Background = tabHoverBrush;
            addBorder.MouseLeave += (_, _) => addBorder.Background = Brushes.Transparent;
            addBorder.MouseLeftButtonDown += (_, _) =>
            {
                if (DataContext is FilePanelViewModel v) v.AddTabCommand.Execute(null);
            };
            TabBarPanel.Children.Add(addBorder);
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
