using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Launcher.ViewModels;
using Launcher.Services;
using System;
using System.Threading.Tasks;

namespace Launcher.Views
{
    public sealed partial class MarketplaceView : Page
    {
        public MarketplaceViewModel ViewModel { get; }

        public MarketplaceView()
        {
            this.InitializeComponent();
            this.ViewModel = App.GetService<MarketplaceViewModel>();
        }

        private async void Search_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.SearchAsync();
        }

        private void ClearFilters_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ClearFiltersCommand.Execute(null);
        }

        private void ProjectsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Явний обробник поруч з x:Bind TwoWay — про всяк випадок, щоб вибір
            // елемента зі списку гарантовано доходив до ViewModel і запускав
            // завантаження деталей проєкту.
            if (sender is ListView lv && lv.SelectedItem is MarketplaceProjectHeader project)
            {
                ViewModel.SelectedProject = project;
            }
        }

        private async void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            await ViewModel.SearchAsync();
        }

        private async void InstallSelectedVersion_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.InstallSelectedVersionCommand.ExecuteAsync(null);
        }
    }
}
