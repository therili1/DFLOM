using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Launcher.ViewModels;

namespace Launcher.Views
{
    public sealed partial class MonitoringView : Page
    {
        public MonitoringViewModel ViewModel { get; }

        public MonitoringView()
        {
            this.InitializeComponent();
            this.ViewModel = App.GetService<MonitoringViewModel>();
        }

        private void Toggle_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ToggleMonitoring();
            
            if (sender is Button btn)
            {
                btn.Content = btn.Content.ToString() == "Запустити Телеметрію" ? "Зупинити Телеметрію" : "Запустити Телеметрію";
            }
        }
    }
}
