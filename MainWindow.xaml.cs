using System.Windows;
using System.Windows.Input;
using SwiftPanel.ViewModels;

namespace SwiftPanel
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
        }

        /// <summary>Global keyboard shortcuts that apply regardless of which control has focus.</summary>
        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;

            // Only handle keys not consumed by child controls
            if (e.Handled) return;

            var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            var alt  = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);

            switch (e.Key)
            {
                case Key.Tab when !ctrl && !alt:
                    // Tab: switch active panel
                    vm.SwitchActivePanelCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.W when ctrl:
                    // Ctrl+W: close current tab in active panel
                    vm.ActivePanel.CloseTabCommand.Execute(vm.ActivePanel.ActiveTabIndex);
                    e.Handled = true;
                    break;

                case Key.H when ctrl:
                    // Ctrl+H: toggle hidden files
                    vm.ToggleHiddenCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.C when ctrl && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
                    // Ctrl+Shift+C: copy path to clipboard
                    vm.CopyPathCommand.Execute(null);
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
                    // Ctrl+F: Find
                    vm.FindCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.T when ctrl:
                    // Ctrl+T: Open Terminal
                    vm.OpenTerminalCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.Enter when alt:
                    // Alt+Enter: Properties
                    vm.PropertiesCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.Add:
                    // Num+ : Mark All
                    vm.MarkAllCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.Subtract:
                    // Num- : Unmark All
                    vm.UnmarkAllCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.Multiply:
                    // Num* : Invert Marks
                    vm.InvertMarksCommand.Execute(null);
                    e.Handled = true;
                    break;
            }
        }
    }
}