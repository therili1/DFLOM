using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;
using System.Text;
using Launcher.Services;

namespace Launcher.Views
{
    public sealed partial class LogsView : Page
    {
        private readonly ILogService _logService;

        public LogsView()
        {
            // ВАЖЛИВО: _logService має бути готовий ДО InitializeComponent(), бо XAML
            // ставить SelectedIndex="0" на LogLevelCombo, що одразу тригерить
            // LogLevelCombo_SelectionChanged -> ApplyFilters -> DisplayLogs ще
            // до завершення конструктора. Якщо _logService ще null у цей момент —
            // NullReferenceException (саме це й траплялось раніше).
            _logService = App.GetService<ILogService>();
            this.InitializeComponent();

            // Не викликаємо ChangeView/скрол одразу в конструкторі —
            // ScrollViewer ще не пройшов layout-прохід (ScrollableHeight буде 0/невалідний),
            // і виклик ChangeView тут раніше провокував зависання сторінки при відкритті.
            this.Loaded += LogsView_Loaded;
        }

        private void LogsView_Loaded(object sender, RoutedEventArgs e)
        {
            DisplayLogs();
            _logService.EntryAdded += (_, entry) =>
            {
                DispatcherQueue.TryEnqueue(() => DisplayLogs());
            };
        }

        private void DisplayLogs(string query = "", string level = "all")
        {
            if (LogsTextBox == null) return; // XAML ще не встиг згенерувати елементи керування

            var sb = new StringBuilder();
            foreach (var log in _logService.Entries)
            {
                bool matchesQuery = string.IsNullOrEmpty(query) || log.Message.Contains(query, StringComparison.OrdinalIgnoreCase);
                bool matchesLevel = level == "all" || string.Equals(log.Level, level, StringComparison.OrdinalIgnoreCase);

                if (matchesQuery && matchesLevel)
                {
                    sb.AppendLine(log.FormattedLine);
                }
            }

            if (sb.Length == 0)
            {
                sb.AppendLine("Поки що немає жодної події. Тут з'являтимуться реальні логи завантажень, встановлення інстансів та запуску гри.");
            }

            LogsTextBox.Text = sb.ToString();

            // Безпечний автоскрол — тільки якщо ScrollViewer вже виміряний (ScrollableHeight валідний).
            if (LogsScroll != null && !double.IsNaN(LogsScroll.ScrollableHeight) && LogsScroll.ScrollableHeight > 0)
            {
                LogsScroll.ChangeView(null, LogsScroll.ScrollableHeight, null);
            }
        }

        private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            ApplyFilters();
        }

        private void LogLevelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            if (LogLevelCombo == null || SearchBox == null) return;

            string query = SearchBox.Text;
            var selectedItem = LogLevelCombo.SelectedItem as ComboBoxItem;
            string level = selectedItem?.Content?.ToString() ?? "all";

            if (level == "Всі Рівні логів") level = "all";

            DisplayLogs(query, level);
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            _logService.Clear();
            DisplayLogs();
        }

        private async void Copy_Click(object sender, RoutedEventArgs e)
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(LogsTextBox.Text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);

            var dialog = new ContentDialog
            {
                Title = "Логи скопійовано",
                Content = "Увесь вивід консолі успішно занесено до буфера обміну!",
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            _ = await dialog.ShowAsync();
        }
    }
}
