using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SwiftPanel.Controls
{
    public partial class ToolbarButton : UserControl
    {
        public static readonly DependencyProperty IconProperty =
            DependencyProperty.Register("Icon", typeof(string), typeof(ToolbarButton), new PropertyMetadata("📄"));

        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register("Label", typeof(string), typeof(ToolbarButton), new PropertyMetadata(""));

        public static readonly DependencyProperty CommandProperty =
            DependencyProperty.Register("Command", typeof(ICommand), typeof(ToolbarButton), new PropertyMetadata(null));

        public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
        public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
        public ICommand Command { get => (ICommand)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }

        public ToolbarButton() => InitializeComponent();
    }
}
