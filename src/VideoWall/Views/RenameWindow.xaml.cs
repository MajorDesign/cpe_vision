using System.Windows;

namespace VideoWall.Views
{
    /// <summary>Diálogo simples para renomear uma fonte.</summary>
    public partial class RenameWindow : Window
    {
        public string NewName => NameBox.Text;

        public RenameWindow(string currentName)
        {
            InitializeComponent();
            NameBox.Text = currentName;
            Loaded += (_, _) =>
            {
                NameBox.SelectAll();
                NameBox.Focus();
            };
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
