using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VideoWall.Models;

namespace VideoWall.Views
{
    /// <summary>Diálogo para escrever/editar uma fonte de Texto (conteúdo, tamanho e cor).</summary>
    public partial class TextEditWindow : Window
    {
        public string ResultText { get; private set; } = string.Empty;
        public double ResultFontSize { get; private set; } = 64;
        public string ResultColorHex { get; private set; } = "#FFFFFF";

        public TextEditWindow(TextElement element)
        {
            InitializeComponent();
            TextBox.Text = element.Text;
            SizeBox.Text = ((int)element.FontSize).ToString();
            ResultColorHex = string.IsNullOrWhiteSpace(element.ForegroundHex) ? "#FFFFFF" : element.ForegroundHex;
            HighlightColor();
            Loaded += (_, _) => { TextBox.SelectAll(); TextBox.Focus(); };
        }

        private void OnColor(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string hex)
            {
                ResultColorHex = hex;
                HighlightColor();
            }
        }

        private void HighlightColor()
        {
            foreach (var b in new[] { CW, CG, CR, CGr, CB })
                b.BorderThickness = new Thickness(
                    string.Equals((string)b.Tag, ResultColorHex, System.StringComparison.OrdinalIgnoreCase) ? 2 : 0);
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            ResultText = TextBox.Text;
            ResultFontSize = double.TryParse(SizeBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var s) && s >= 1
                ? s : 64;
            DialogResult = true;
            Close();
        }
    }
}
