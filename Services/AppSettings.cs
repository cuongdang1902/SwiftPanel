using System;
using System.Collections.Generic;
using SwiftPanel.ViewModels;

namespace SwiftPanel.Services
{
    /// <summary>All persisted user preferences for SwiftPanel.</summary>
    public class AppSettings
    {
        // ── Panel paths & Tabs ────────────────────────────────────────────
        public string LeftPath  { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        public string RightPath { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        public List<SavedTab> LeftTabs { get; set; } = new();
        public int LeftActiveTabIndex { get; set; } = 0;
        public List<SavedTab> RightTabs { get; set; } = new();
        public int RightActiveTabIndex { get; set; } = 0;

        // ── View modes ────────────────────────────────────────────────────
        public ViewMode LeftViewMode  { get; set; } = ViewMode.Details;
        public ViewMode RightViewMode { get; set; } = ViewMode.Details;

        // ── Sort state ────────────────────────────────────────────────────
        public SortColumn LeftSort          { get; set; } = SortColumn.Name;
        public bool       LeftSortAscending { get; set; } = true;
        public SortColumn RightSort         { get; set; } = SortColumn.Name;
        public bool       RightSortAscending{ get; set; } = true;

        // ── Display options ───────────────────────────────────────────────
        public bool LeftShowHidden  { get; set; } = false;
        public bool RightShowHidden { get; set; } = false;

        // ── Window geometry ───────────────────────────────────────────────
        public double WindowWidth  { get; set; } = 1100;
        public double WindowHeight { get; set; } = 650;
        public double WindowLeft   { get; set; } = -1;   // -1 = center screen
        public double WindowTop    { get; set; } = -1;
        public bool   Maximized    { get; set; } = false;

        // ── Bookmarks ─────────────────────────────────────────────────────
        public List<BookmarkEntry> Bookmarks { get; set; } = new();
    }

    public class BookmarkEntry
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
    }

    public class SavedTab
    {
        public string Header { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
    }
}
