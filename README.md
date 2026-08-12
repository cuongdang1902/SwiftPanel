# SwiftPanel

A fast, modern dual-panel file manager for Windows built with WPF (.NET 9).  
Inspired by Total Commander and SpeedCommander.

![Platform](https://img.shields.io/badge/platform-Windows-blue)
![Framework](https://img.shields.io/badge/.NET-9.0-purple)
![Language](https://img.shields.io/badge/language-C%23-green)
![License](https://img.shields.io/badge/license-MIT-orange)

---

## Features

### 📂 Dual-Panel Navigation
- Side-by-side panels for fast file operations
- Independent tabs per panel (`Ctrl+T` new tab, `Ctrl+W` close)
- Clickable breadcrumb path bar
- Drive bar with free space tooltip on hover

### 🗂️ 4 View Modes
| Mode | Shortcut | Description |
|------|----------|-------------|
| **Details** | `Ctrl+1` | Full column list: Name, Ext, Size, Date, Attr |
| **List** | `Ctrl+2` | Small icons, multi-column horizontal scroll |
| **Large Icons** | `Ctrl+3` | 48×48 shell icons with names |
| **Thumbnails** | `Ctrl+4` | 96×96 image preview for photos, shell icon for others |

### ⚡ File Operations
- **Copy** `F5` · **Move** `F6` · **Delete** `F8` · **New Folder** `F7`
- **Rename** `F2` – inline editing directly in the list
- **Drag & Drop** between panels (hold `Shift` to Move)
- Progress dialog with cancel support for large operations

### 🔍 Quick Filter
- `Ctrl+F` – live filter bar, type to instantly filter the file list
- `Esc` to close, `Enter/↓` to return to list

### 🏷️ Marking & Selection
- `Space` – mark/unmark file (yellow highlight), auto-advance
- `Num+` mark all · `Num-` unmark all · `Num*` invert marks
- Operations act on marked files (or selected file if none marked)

### 🔄 Sorting
- Click any column header: **Name / Ext / Size / Date / Attr**
- Click again to toggle ▲ ascending / ▼ descending
- Sort persists when navigating into subfolders

### ⌨️ Keyboard Shortcuts
| Key | Action |
|-----|--------|
| `Tab` | Switch active panel |
| `F2` | Rename |
| `F5` | Copy |
| `F6` | Move |
| `F7` | New folder |
| `F8` / `Del` | Delete |
| `Ctrl+H` | Show / hide hidden files |
| `Ctrl+F` | Quick filter |
| `Ctrl+Shift+C` | Copy current path to clipboard |
| `Ctrl+1–4` | Switch view mode |
| `Ctrl+T` | New tab |
| `Ctrl+W` | Close tab |
| `Backspace` | Navigate up |
| `Enter` | Open file / enter folder |

### 🖥️ UI Polish
- Active panel highlighted with blue top border
- FileSystem watcher – auto-refresh on external changes
- Dark-styled context menu with all common operations

---

## Requirements

- Windows 10/11 (64-bit)
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

## Build & Run

```bash
git clone https://github.com/YOUR_USERNAME/SwiftPanel.git
cd SwiftPanel
dotnet run
```

Or open `SwiftPanel.sln` in Visual Studio 2022+.

---

## Tech Stack

- **WPF** (.NET 9, Windows only)
- **CommunityToolkit.Mvvm** – MVVM source generators
- **Shell32** P/Invoke – native file icons & thumbnails
- Architecture: MVVM with `FilePanelViewModel` per panel, `MainViewModel` for cross-panel operations

---

## License

MIT © 2026
