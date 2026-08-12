using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace SwiftPanel.Services
{
    /// <summary>
    /// Global singleton bookmark manager.
    /// Both panels share the same bookmarks list.
    /// </summary>
    public class BookmarkService
    {
        // ── Singleton ─────────────────────────────────────────────────────
        public static BookmarkService Instance { get; } = new BookmarkService();
        private BookmarkService() { }

        // ── Observable collection (both panels can bind to this) ──────────
        public ObservableCollection<BookmarkEntry> Bookmarks { get; } = new();

        // ── Public API ────────────────────────────────────────────────────

        /// <summary>Load bookmarks from a settings snapshot (called at startup).</summary>
        public void Load(System.Collections.Generic.List<BookmarkEntry> entries)
        {
            Bookmarks.Clear();
            foreach (var e in entries)
                Bookmarks.Add(e);
        }

        /// <summary>
        /// Add a bookmark. Uses the folder name as label.
        /// Ignores duplicates (same path).
        /// </summary>
        public void Add(string path)
        {
            path = path.TrimEnd('\\', '/');
            if (Bookmarks.Any(b => b.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
                return;

            var label = Path.GetFileName(path);
            if (string.IsNullOrEmpty(label)) label = path; // root drive

            Bookmarks.Add(new BookmarkEntry { Name = label, Path = path });
        }

        /// <summary>Remove bookmark at index.</summary>
        public void Remove(int index)
        {
            if (index >= 0 && index < Bookmarks.Count)
                Bookmarks.RemoveAt(index);
        }

        /// <summary>Export for persistence.</summary>
        public System.Collections.Generic.List<BookmarkEntry> Export()
            => Bookmarks.ToList();
    }
}
