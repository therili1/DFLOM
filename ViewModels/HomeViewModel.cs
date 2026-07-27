
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Models;
using Launcher.Services;

namespace Launcher.ViewModels
{
    public partial class HomeViewModel : ObservableObject
    {
        private readonly IAuthenticationService _authService;
        private readonly IMinecraftService _minecraftService;
        private readonly IInstanceStore _instanceStore;
        private readonly ILogService _log;

        [ObservableProperty]
        private string _username = "Steve";

        [ObservableProperty]
        private bool _isAuthenticated;

        [ObservableProperty]
        private string _loginErrorMessage = string.Empty;

        public ObservableCollection<MinecraftInstance> Instances => _instanceStore.Instances;

        [ObservableProperty]
        private MinecraftInstance? _selectedInstance;

        [ObservableProperty]
        private bool _isLaunching;

        [ObservableProperty]
        private double _launchProgress;

        [ObservableProperty]
        private string _launchStatus = "Встановіть профіль та грайте";

        [ObservableProperty]
        private ObservableCollection<string> _newsFeed = new();

        public ObservableCollection<SavedAccount> SavedAccounts { get; } = new();

        public HomeViewModel()
        {
            _authService = App.GetService<IAuthenticationService>();
            _minecraftService = App.GetService<IMinecraftService>();
            _instanceStore = App.GetService<IInstanceStore>();
            _log = App.GetService<ILogService>();

            // AuthenticationService вже завантажив сесію з диска у своєму конструкторі
            // (Singleton створюється до цього ViewModel) - підтягуємо той стан сюди,
            // інакше UI завжди показуватиме "не залогінений" навіть при валідній сесії.
            if (_authService.IsAuthenticated)
            {
                Username = _authService.Username ?? "Steve";
                IsAuthenticated = true;
            }
            RefreshSavedAccounts();
            _authService.AccountsChanged += OnAuthAccountsChanged;

            SelectedInstance = Instances.FirstOrDefault();

            NewsFeed.Add("🔥 Стабільний запуск через ядро CmlLib.Core активовано!");
            NewsFeed.Add("🛠️ Покращено роботу нашого менеджера світів: додано стиснення копій.");
            NewsFeed.Add("🎨 Доступні нові теми у редакторі Fluent Design. Спробуйте Mica.");
        }

        private void OnAuthAccountsChanged()
        {
            Username = _authService.Username ?? "Steve";
            IsAuthenticated = _authService.IsAuthenticated;
            RefreshSavedAccounts();
        }

        private void RefreshSavedAccounts()
        {
            SavedAccounts.Clear();
            foreach (var acc in _authService.SavedAccounts)
            {
                SavedAccounts.Add(acc);
            }
        }

        [RelayCommand]
        public void SwitchAccount(SavedAccount account)
        {
            if (account == null) return;
            _authService.SwitchAccount(account.Uuid);
            // OnAuthAccountsChanged підхопить оновлення через подію AccountsChanged.
        }

        [RelayCommand]
        public void RemoveAccount(SavedAccount account)
        {
            if (account == null) return;
            _authService.RemoveAccount(account.Uuid);
        }

        [RelayCommand]
        public async Task LoginOfflineAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            var success = await _authService.LoginOfflineAsync(name);
            if (success)
            {
                Username = name;
                IsAuthenticated = true;
                LoginErrorMessage = string.Empty;
            }
        }

        [RelayCommand]
        public async Task LoginMicrosoftAsync()
        {
            LoginErrorMessage = "Ініціалізація Microsoft OAuth...";
            var success = await _authService.LoginWithMicrosoftAsync();
            if (success)
            {
                Username = _authService.Username ?? "User";
                IsAuthenticated = true;
                LoginErrorMessage = string.Empty;
            }
            else
            {
                LoginErrorMessage = "Помилка авторизації Microsoft.";
            }
        }

        public async Task<(bool success, string? errorMessage)> LoginElyByAsync(string login, string password)
        {
            var (success, error) = await _authService.LoginWithElyByAsync(login, password);
            if (success)
            {
                Username = _authService.Username ?? "User";
                IsAuthenticated = true;
                LoginErrorMessage = string.Empty;
            }
            return (success, error);
        }

        [RelayCommand]
        public void Logout()
        {
            _authService.Logout();
            IsAuthenticated = false;
            Username = "Steve";
        }

        [RelayCommand]
        public async Task LaunchGameAsync()
        {
            if (SelectedInstance == null)
            {
                LaunchStatus = "Помилка: Спершу оберіть профіль!";
                return;
            }

            IsLaunching = true;
            LaunchProgress = 0;
            LaunchStatus = "Аналіз файлів та підготовка до завантаження...";

            try
            {
                // Створюємо об'єкт Progress, який прийматиме відсотки завантаження від MinecraftService
                var progressReporter = new Progress<double>(percent =>
                {
                    // Оновлюємо властивості в UI потоці
                    LaunchProgress = Math.Round(percent, 1);
                    LaunchStatus = $"Завантаження ресурсів гри: {LaunchProgress}%";
                });

                // Крок 1. Завантажуємо гру (ліби, ассети, natives)
                LaunchStatus = "Перевірка файлів гри (assets, libraries)...";
                await _minecraftService.InstallInstanceAsync(SelectedInstance, progressReporter);

                // Крок 2. Запускаємо гру!
                LaunchProgress = 100;
                LaunchStatus = "Формування параметрів та запуск клієнта...";
                
                await _minecraftService.LaunchInstanceAsync(SelectedInstance);

                SelectedInstance.LastLaunched = DateTime.Now;
                await _instanceStore.SaveAsync();

                LaunchStatus = $"Грається: {SelectedInstance.Name} ({SelectedInstance.Version})";
            }
            catch (Exception ex)
            {
                LaunchStatus = $"Помилка запуску: {ex.Message}";
                _log.Error("HomeViewModel", $"Запуск '{SelectedInstance?.Name}' провалився: {ex}");
            }
            finally
            {
                IsLaunching = false;
            }
        }

    }
}