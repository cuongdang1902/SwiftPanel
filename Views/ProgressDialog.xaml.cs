using System;
using System.Threading;
using System.Windows;

namespace SwiftPanel.Views
{
    public partial class ProgressDialog : Window
    {
        private CancellationTokenSource _cts = new();
        public CancellationToken CancellationToken => _cts.Token;

        public ProgressDialog(string operation)
        {
            InitializeComponent();
            OperationLabel.Text = operation;
        }

        public void UpdateProgress(int percent, string currentFile)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressBar.Value = percent;
                CurrentFileLabel.Text = currentFile;
            });
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            _cts.Cancel();
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _cts.Cancel();
        }
    }
}
