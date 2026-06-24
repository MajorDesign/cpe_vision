using System.Windows;
using VideoWall.Views;

namespace VideoWall
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // O pré-load verifica atualizações e então abre a janela principal.
            new SplashWindow().Show();
        }
    }
}
