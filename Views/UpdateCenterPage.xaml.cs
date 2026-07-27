using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Launcher.ViewModels;

namespace Launcher.Views
{
    public sealed partial class UpdateCenterPage : Page
    {
        public UpdateCenterViewModel ViewModel { get; }

        public UpdateCenterPage()
        {
            this.InitializeComponent();
            this.ViewModel = App.GetService<UpdateCenterViewModel>();
        }

        private async void OpenReleasesPage_Click(object sender, RoutedEventArgs e)
        {
            await Windows.System.Launcher.LaunchUriAsync(new System.Uri("https://github.com/therili1/DFLOM"));
        }
    }
}
