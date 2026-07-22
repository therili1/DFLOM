using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Launcher.ViewModels;

namespace Launcher.Views
{
    public sealed partial class ThemeEditorPage : Page
    {
        public ThemeEditorViewModel ViewModel { get; }

        public ThemeEditorPage()
        {
            this.InitializeComponent();
            this.ViewModel = App.GetService<ThemeEditorViewModel>();
        }

        private void PresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string presetName)
            {
                ViewModel.ApplyPresetCommand.Execute(presetName);
            }
        }
    }
}
