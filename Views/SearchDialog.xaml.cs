using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace SwiftPanel.Views
{
    public partial class SearchDialog : Window
    {
        private readonly string _startPath;
        private CancellationTokenSource? _searchCts;
        public string? SelectedPath { get; private set; }

        public SearchDialog(string startPath)
        {
            InitializeComponent();
            _startPath = startPath;
            SearchPathBox.Text = startPath;
        }

        private void OnSearchKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) OnFind(sender, e);
        }

        private async void OnFind(object sender, RoutedEventArgs e)
        {
            var pattern = SearchBox.Text.Trim();
            if (string.IsNullOrEmpty(pattern))
            {
                SearchBox.Focus();
                return;
            }

            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var token = _searchCts.Token;

            ResultsList.Items.Clear();
            StatusLabel.Text = "Searching...";

            var searchPath = SearchPathBox.Text.Trim();
            if (!Directory.Exists(searchPath)) searchPath = _startPath;

            bool caseSensitive = CaseSensitiveCheck.IsChecked == true;
            bool subfolders = SubfoldersCheck.IsChecked == true;
            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            int count = 0;
            try
            {
                await Task.Run(() =>
                {
                    var option = subfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                    var entries = Directory.EnumerateFileSystemEntries(searchPath, "*", option);

                    foreach (var entry in entries)
                    {
                        if (token.IsCancellationRequested) break;

                        var name = Path.GetFileName(entry);
                        if (name.Contains(pattern, comparison))
                        {
                            count++;
                            Dispatcher.Invoke(() => ResultsList.Items.Add(entry));
                        }
                    }
                }, token);

                StatusLabel.Text = $"Found {count} result(s).";
            }
            catch (OperationCanceledException)
            {
                StatusLabel.Text = "Search cancelled.";
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Error: {ex.Message}";
            }
        }

        private void OnResultDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ResultsList.SelectedItem is string path)
            {
                SelectedPath = path;
                DialogResult = true;
                Close();
            }
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            _searchCts?.Cancel();
            Close();
        }
    }
}
