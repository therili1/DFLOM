using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Linq;
using Launcher.ViewModels;
using Launcher.Models;

namespace Launcher.Views
{
    public sealed partial class WorldManagerPage : Page
    {
        public WorldManagerViewModel ViewModel { get; }

        public WorldManagerPage()
        {
            this.InitializeComponent();
            this.ViewModel = App.GetService<WorldManagerViewModel>();
        }

        // Світи Minecraft створюються самою грою (через меню Create New World),
        // тому тут ми лише пропонуємо запустити інстанс, а не генеруємо файли світу вручну.
        private async void CreateWorld_Click(object sender, RoutedEventArgs e)
        {
            var instancesVm = App.GetService<InstancesViewModel>();
            if (ViewModel.SelectedInstance != null)
            {
                await instancesVm.LaunchInstanceAsync(ViewModel.SelectedInstance);
            }
        }

        private async void DeleteWorld_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is WorldItem world)
            {
                await ViewModel.DeleteWorldAsync(world);
            }
        }

        private async void BackupWorld_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is WorldItem world)
            {
                await ViewModel.BackupWorldAsync(world);
            }
        }

        private async void AddDatapack_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not WorldItem world) return;

            var instance = ViewModel.Instances.FirstOrDefault(i => i.Id == world.InstanceId);

            var dialog = new DatapackSearchDialog(world, instance)
            {
                XamlRoot = this.XamlRoot
            };
            _ = await dialog.ShowAsync();

            // Після закриття діалогу оновлюємо лічильники (кількість датапаків могла змінитись).
            await ViewModel.LoadWorldsAsync();
        }

        private void DropZone_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        }

        private async void DropZone_Drop(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                if (items.Count > 0)
                {
                    await ViewModel.ImportWorldZipAsync(items[0].Path);
                }
            }
        }
    }
}
