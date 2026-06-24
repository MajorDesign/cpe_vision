using System.Windows;
using System.Windows.Controls;

namespace VideoWall.Views
{
    /// <summary>
    /// Hospeda um WebView2 com URL e fator de zoom controláveis. O zoom é aplicado
    /// ao CoreWebView2 assim que ele fica pronto (e a cada alteração).
    /// </summary>
    public partial class BrowserView : UserControl
    {
        public BrowserView()
        {
            InitializeComponent();
            Web.CoreWebView2InitializationCompleted += (_, _) => ApplyZoom();
        }

        public static readonly DependencyProperty UrlProperty =
            DependencyProperty.Register(nameof(Url), typeof(string), typeof(BrowserView),
                new PropertyMetadata(string.Empty, OnUrlChanged));

        public string Url
        {
            get => (string)GetValue(UrlProperty);
            set => SetValue(UrlProperty, value);
        }

        public static readonly DependencyProperty ZoomFactorProperty =
            DependencyProperty.Register(nameof(ZoomFactor), typeof(double), typeof(BrowserView),
                new PropertyMetadata(1.0, OnZoomChanged));

        public double ZoomFactor
        {
            get => (double)GetValue(ZoomFactorProperty);
            set => SetValue(ZoomFactorProperty, value);
        }

        private static void OnUrlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var view = (BrowserView)d;
            if (Uri.TryCreate(view.Url, UriKind.Absolute, out var uri))
                view.Web.Source = uri;
        }

        private static void OnZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((BrowserView)d).ApplyZoom();
        }

        private void ApplyZoom()
        {
            try
            {
                if (Web.CoreWebView2 != null)
                    Web.ZoomFactor = ZoomFactor;
            }
            catch
            {
                // CoreWebView2 ainda não pronto; será aplicado em InitializationCompleted.
            }
        }
    }
}
