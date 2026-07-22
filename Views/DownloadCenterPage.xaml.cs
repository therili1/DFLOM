using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Launcher.Services;
using Launcher.ViewModels;

namespace Launcher.Views
{
    public sealed partial class DownloadCenterPage : Page
    {
        public DownloadCenterViewModel ViewModel { get; }

        public DownloadCenterPage()
        {
            // ВАЖЛИВО: ViewModel має бути готовий ДО InitializeComponent(). ComboBoxItem
            // з IsSelected="True" в XAML піднімає SelectionChanged просто під час побудови
            // дерева елементів - тобто ще всередині виклику InitializeComponent(). Якщо
            // ViewModel присвоюється рядком ПІСЛЯ, обробник ловить його як null -> NullReferenceException.
            this.ViewModel = App.GetService<DownloadCenterViewModel>();
            this.InitializeComponent();
        }

        private void SortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ViewModel == null) return;
            if (SortCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                ViewModel.SortMode = tag;
            }
        }

        private void Pause_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is DownloadTask task) ViewModel.PauseTask(task.Id);
        }

        private void Resume_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is DownloadTask task) ViewModel.ResumeTask(task.Id);
        }

        private void Retry_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is DownloadTask task) ViewModel.RetryTask(task.Id);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is DownloadTask task) ViewModel.CancelTask(task.Id);
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is DownloadTask task) ViewModel.OpenFolder(task);
        }
    }
}
