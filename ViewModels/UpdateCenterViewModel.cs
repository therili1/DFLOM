using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Launcher.ViewModels
{
    public partial class UpdateCenterViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _currentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
    }
}
