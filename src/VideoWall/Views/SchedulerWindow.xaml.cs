using System.Windows;
using VideoWall.ViewModels;

namespace VideoWall.Views
{
    /// <summary>Diálogo de gerenciamento dos agendamentos de layout.</summary>
    public partial class SchedulerWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public SchedulerWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            // Persiste eventuais alterações no estado "Ativado" feitas na lista.
            _viewModel.SaveSchedules();
            base.OnClosed(e);
        }
    }
}
