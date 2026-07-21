using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Launcher.ViewModels;
using Launcher.Models;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Launcher.Views
{
    public sealed partial class ScreenshotManagerPage : Page
    {
        public ScreenshotManagerViewModel ViewModel { get; }

        public ScreenshotManagerPage()
        {
            this.InitializeComponent();
            this.ViewModel = App.GetService<ScreenshotManagerViewModel>();
        }

        private async void ImportScreenshot_Click(object sender, RoutedEventArgs e)
        {
            var targetInstance = ViewModel.Instances.FirstOrDefault();
            if (targetInstance == null) return;

            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");

            // FileOpenPicker у WinUI3 (без Package Identity) потребує прив'язки до вікна.
            var hwnd = WindowNative.GetWindowHandle(App.GetService<MainWindow>());
            InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                await ViewModel.ImportScreenshotAsync(file.Path, targetInstance);
            }
        }

        private async void DeleteScreenshot_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ScreenshotItem item)
            {
                await ViewModel.DeleteScreenshotAsync(item.Id);
            }
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
                var targetInstance = ViewModel.Instances.FirstOrDefault();
                if (items.Count > 0 && targetInstance != null)
                {
                    await ViewModel.ImportScreenshotAsync(items[0].Path, targetInstance);
                }
            }
        }
    }
}
