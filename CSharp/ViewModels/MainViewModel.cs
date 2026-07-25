using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Launcher.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _statusText = "Ready to launch";

        [ObservableProperty]
        private bool _isBusy;
    }
}
