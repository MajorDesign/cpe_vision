using System.Windows;

namespace VideoWall.Viewer
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // O pré-load verifica atualizações no GitHub e então abre o terminal.
            new SplashWindow().Show();
        }
    }
}
