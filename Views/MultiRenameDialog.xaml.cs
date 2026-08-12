using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using SwiftPanel.Models;

namespace SwiftPanel.Views
{
    public class RenamePreviewItem
    {
        public string OldName    { get; set; } = string.Empty;
        public string NewName    { get; set; } = string.Empty;
        public bool   HasConflict{ get; set; }
        public FileItem Source   { get; set; } = null!;
    }

    public partial class MultiRenameDialog : Window
    {
        private readonly IReadOnlyList<FileItem> _items;
        private readonly string _basePath;

        public MultiRenameDialog(IReadOnlyList<FileItem> items, string basePath)
        {
            InitializeComponent();
            _items    = items;
            _basePath = basePath;
            UpdatePreview();
        }

        // ─────────────────────────────────────────────────────────────────
        // Pattern Engine
        // ─────────────────────────────────────────────────────────────────

        private List<RenamePreviewItem> BuildPreview()
        {
            var result  = new List<RenamePreviewItem>();
            var now     = DateTime.Now;
            var namePattern = NamePatternBox.Text;
            var extPattern  = ExtPatternBox.Text;

            int  counterStart  = TryParse(CounterStartBox.Text,  1);
            int  counterStep   = TryParse(CounterStepBox.Text,   1);
            int  counterDigits = TryParse(CounterDigitsBox.Text, 2);

            int counter = counterStart;
            foreach (var item in _items)
            {
                var origName = Path.GetFileNameWithoutExtension(item.Name);
                var origExt  = item.Extension; // already without dot (from FileItem)

                var newName = ApplyPattern(namePattern, origName, origExt, counter, now);
                var newExt  = ApplyPattern(extPattern,  origName, origExt, counter, now);

                newName = ApplyCase(newName);
                newExt  = ApplyCase(newExt);

                if (RemoveSpacesBox.IsChecked == true)
                {
                    newName = newName.Replace(' ', '_');
                    newExt  = newExt .Replace(' ', '_');
                }

                // Combine
                string fullNew = string.IsNullOrWhiteSpace(newExt)
                    ? newName
                    : $"{newName}.{newExt}";

                // Sanitize invalid characters
                fullNew = SanitizeFileName(fullNew);

                result.Add(new RenamePreviewItem
                {
                    OldName = item.Name,
                    NewName = fullNew,
                    Source  = item
                });

                counter += counterStep;
            }

            // Mark conflicts (duplicate new names or conflicts with existing files)
            var newNames = result.Select(r => r.NewName.ToLowerInvariant()).ToList();
            for (int i = 0; i < result.Count; i++)
            {
                var lower = result[i].NewName.ToLowerInvariant();
                bool duplicate = newNames.Count(n => n == lower) > 1;
                bool unchanged = result[i].NewName.Equals(result[i].OldName, StringComparison.OrdinalIgnoreCase);
                result[i].HasConflict = duplicate && !unchanged;
            }

            return result;
        }

        private string ApplyPattern(string pattern, string name, string ext, int counter, DateTime now)
        {
            if (string.IsNullOrEmpty(pattern)) return string.Empty;

            var sb = new StringBuilder(pattern);

            // [N1-3] – substring of name
            sb = new StringBuilder(Regex.Replace(sb.ToString(),
                @"\[N(\d+)-(\d+)\]", m =>
                {
                    int start = int.Parse(m.Groups[1].Value) - 1;
                    int end   = int.Parse(m.Groups[2].Value);
                    start = Math.Max(0, Math.Min(start, name.Length));
                    end   = Math.Max(start, Math.Min(end, name.Length));
                    return name.Substring(start, end - start);
                }));

            // [N] – full original name
            sb.Replace("[N]", name);
            // [E] – extension without dot
            sb.Replace("[E]", ext);
            // [C] – counter
            sb.Replace("[C]", counter.ToString().PadLeft(TryParse(CounterDigitsBox.Text, 2), '0'));
            // Date tokens
            sb.Replace("[D]", now.ToString("yyyyMMdd"));
            sb.Replace("[T]", now.ToString("HHmmss"));
            sb.Replace("[Y]", now.ToString("yyyy"));
            sb.Replace("[M]", now.ToString("MM"));
            sb.Replace("[d]", now.ToString("dd"));

            return sb.ToString();
        }

        private string ApplyCase(string s)
        {
            if (CaseLower.IsChecked == true) return s.ToLowerInvariant();
            if (CaseUpper.IsChecked == true) return s.ToUpperInvariant();
            if (CaseTitle.IsChecked == true) return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());
            return s;
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
        }

        private static int TryParse(string s, int fallback)
            => int.TryParse(s.Trim(), out int v) && v > 0 ? v : fallback;

        // ─────────────────────────────────────────────────────────────────
        // UI Handlers
        // ─────────────────────────────────────────────────────────────────

        private void UpdatePreview()
        {
            var items = BuildPreview();
            PreviewList.ItemsSource = items;

            int conflicts = items.Count(i => i.HasConflict);
            StatusText.Text = conflicts > 0
                ? $"⚠ {conflicts} naming conflict(s) – please fix the pattern"
                : $"✓ {items.Count} file(s) will be renamed";

            RenameButton.IsEnabled = conflicts == 0 && items.Any(i =>
                !i.NewName.Equals(i.OldName, StringComparison.OrdinalIgnoreCase));
        }

        private void OnPatternChanged(object sender, EventArgs e) => UpdatePreview();

        private void OnRenameClick(object sender, RoutedEventArgs e)
        {
            var preview = (List<RenamePreviewItem>)PreviewList.ItemsSource;
            int errors  = 0;

            foreach (var item in preview)
            {
                if (item.NewName.Equals(item.OldName, StringComparison.OrdinalIgnoreCase)) continue;
                var newPath = Path.Combine(_basePath, item.NewName);
                try
                {
                    if (item.Source.IsDirectory)
                        Directory.Move(item.Source.FullPath, newPath);
                    else
                        File.Move(item.Source.FullPath, newPath);
                }
                catch (Exception ex)
                {
                    errors++;
                    MessageBox.Show($"Cannot rename '{item.OldName}':\n{ex.Message}",
                        "Rename Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            if (errors == 0)
                DialogResult = true;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
            => DialogResult = false;

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) DialogResult = false;
        }
    }
}
