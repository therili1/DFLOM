using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Launcher.ViewModels;
using Launcher.Models;
using System;

namespace Launcher.Views
{
    public sealed partial class HomeView : Page
    {
        public HomeViewModel ViewModel { get; }

        public HomeView()
        {
            this.InitializeComponent();
            this.ViewModel = App.GetService<HomeViewModel>();
        }

        private async void Play_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.LaunchGameAsync();
        }

        private void Logout_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.Logout();
        }

        private void AccountRow_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is SavedAccount account)
            {
                ViewModel.SwitchAccount(account);
            }
        }

        private void RemoveAccount_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SavedAccount account)
            {
                ViewModel.RemoveAccount(account);
            }
        }

        private async void ShowLoginDialog_Click(object sender, RoutedEventArgs e)
        {
            var txtUser = new TextBox
            {
                PlaceholderText = "Введіть нікнейм (напр., Player1)",
                Text = ViewModel.Username,
                Margin = new Thickness(0, 10, 0, 4)
            };

            var stack = new StackPanel { Spacing = 8 };
            stack.Children.Add(new TextBlock { Text = "Офлайн нікнейм, або обери спосіб входу нижче:" });
            stack.Children.Add(txtUser);
            var elyByButton = new HyperlinkButton { Content = "Увійти через Ely.by", Margin = new Thickness(0, 4, 0, 0) };
            stack.Children.Add(elyByButton);

            var dialog = new ContentDialog
            {
                Title = "Вхід до акаунту",
                Content = stack,
                PrimaryButtonText = "Офлайн вхід",
                SecondaryButtonText = "Microsoft OAuth",
                CloseButtonText = "Скасувати",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            // Ely.by відкриває власний окремий діалог з логіном/паролем поверх поточного.
            elyByButton.Click += async (_, __) =>
            {
                dialog.Hide();
                await ShowElyByLoginDialogAsync();
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                await ViewModel.LoginOfflineAsync(txtUser.Text);
            }
            else if (result == ContentDialogResult.Secondary)
            {
                await ViewModel.LoginMicrosoftAsync();
            }
        }

        private async System.Threading.Tasks.Task ShowElyByLoginDialogAsync()
        {
            var txtLogin = new TextBox { PlaceholderText = "Логін або email Ely.by", Margin = new Thickness(0, 8, 0, 4) };
            var txtPassword = new PasswordBox { PlaceholderText = "Пароль", Margin = new Thickness(0, 0, 0, 4) };
            var txtError = new TextBlock { Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.OrangeRed), TextWrapping = TextWrapping.Wrap, FontSize = 12 };

            var stack = new StackPanel { Spacing = 4 };
            stack.Children.Add(new TextBlock { Text = "Дані передаються напряму на authserver.ely.by, лаунчер їх ніде не зберігає.", FontSize = 11, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
            stack.Children.Add(txtLogin);
            stack.Children.Add(txtPassword);
            stack.Children.Add(txtError);

            var dialog = new ContentDialog
            {
                Title = "Вхід через Ely.by",
                Content = stack,
                PrimaryButtonText = "Увійти",
                CloseButtonText = "Скасувати",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            // Не закриваємо діалог автоматично при помилці — показуємо текст помилки і лишаємось.
            dialog.PrimaryButtonClick += async (_, args) =>
            {
                var deferral = args.GetDeferral();
                var (success, error) = await ViewModel.LoginElyByAsync(txtLogin.Text, txtPassword.Password);
                if (!success)
                {
                    args.Cancel = true;
                    txtError.Text = error ?? "Не вдалося увійти.";
                }
                deferral.Complete();
            };

            await dialog.ShowAsync();
        }
    }
}
