using System.Collections.Generic;
using System.Windows;
using VideoWall.Services;

namespace VideoWall.Views
{
    /// <summary>Diálogo modal de seleção de uma janela aberta.</summary>
    public partial class WindowPickerWindow : Window
    {
        public OpenWindowInfo? SelectedWindow { get; private set; }

        public WindowPickerWindow(IEnumerable<OpenWindowInfo> windows)
        {
            InitializeComponent();
            WindowsList.ItemsSource = windows;
        }

        private void OnConfirm(object sender, RoutedEventArgs e)
        {
            Confirm();
        }

        private void OnListDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (WindowsList.SelectedItem != null)
                Confirm();
        }

        private void Confirm()
        {
            SelectedWindow = WindowsList.SelectedItem as OpenWindowInfo;
            if (SelectedWindow == null)
                return;

            DialogResult = true;
            Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
