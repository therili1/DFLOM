using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Launcher.Models;

namespace Launcher.Services
{
    public class AuthenticationService : IAuthenticationService
    {
        private static readonly JsonSerializerOptions ElyByJsonOptions = new() { PropertyNameCaseInsensitive = true };
        private readonly string _accountsFilePath;
        private readonly List<SavedAccount> _accounts = new();
        private string? _activeUuid;

        public bool IsAuthenticated { get; private set; }
        public string? Username { get; private set; }
        public string? UUID { get; private set; }
        public string? AccessToken { get; private set; }
        public string AuthType { get; private set; } = "Offline";

        public IReadOnlyList<SavedAccount> SavedAccounts => _accounts.OrderByDescending(a => a.LastUsedAt).ToList();

        public event Action? AccountsChanged;

        public AuthenticationService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string launcherDir = Path.Combine(appData, ".lrs_launcher");
            Directory.CreateDirectory(launcherDir);
            _accountsFilePath = Path.Combine(launcherDir, "accounts.json");

            LoadAccounts();

            // Міграція зі старого однo-акаунтного session.json, якщо він лишився з попередньої версії.
            var oldSessionPath = Path.Combine(launcherDir, "session.json");
            if (_accounts.Count == 0 && File.Exists(oldSessionPath))
            {
                MigrateOldSession(oldSessionPath);
            }

            if (_activeUuid != null)
            {
                var active = _accounts.FirstOrDefault(a => a.Uuid == _activeUuid);
                if (active != null) ApplyAccount(active, save: false);
            }
        }

        public Task<bool> LoginOfflineAsync(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return Task.FromResult(false);

            // Детермінований UUID з ніку (той самий алгоритм, що vanilla-сервер використовує для
            // offline-режиму: MD5("OfflinePlayer:" + name) з виставленими бітами версії 3).
            // Раніше тут був Guid.NewGuid() - тобто кожен логін під тим самим ніком створював
            // "новий" акаунт, і зберігати список збережених логінів не мало сенсу.
            var uuid = OfflineUuidFromName(username);

            var account = new SavedAccount
            {
                Uuid = uuid,
                Username = username,
                AccessToken = "offline_access_token",
                AuthType = "Offline",
                LastUsedAt = DateTime.Now
            };

            UpsertAccount(account);
            ApplyAccount(account, save: true);
            return Task.FromResult(true);
        }

        public Task<bool> LoginWithMicrosoftAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        // Ely.by офіційний ендпоінт автентифікації (аналог старого Mojang-протоколу),
        // задокументований на https://docs.ely.by/en/minecraft-auth.html —
        // POST /auth/authenticate з username/password/clientToken, у відповідь
        // отримуємо accessToken і selectedProfile (ім'я + UUID).
        public async Task<(bool success, string? errorMessage)> LoginWithElyByAsync(string username, string password, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return (false, "Введіть логін і пароль Ely.by.");
            }

            try
            {
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

                var clientToken = Guid.NewGuid().ToString();
                var requestBody = new
                {
                    username,
                    password,
                    clientToken,
                    requestUser = true
                };

                var response = await httpClient.PostAsJsonAsync("https://authserver.ely.by/auth/authenticate", requestBody, cancellationToken);
                var json = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var errorDoc = JsonSerializer.Deserialize<ElyByErrorResponse>(json, ElyByJsonOptions);
                    return (false, errorDoc?.ErrorMessage ?? "Не вдалося увійти через Ely.by.");
                }

                var authResponse = JsonSerializer.Deserialize<ElyByAuthResponse>(json, ElyByJsonOptions);
                if (authResponse?.SelectedProfile == null || string.IsNullOrEmpty(authResponse.AccessToken))
                {
                    return (false, "Ely.by повернув неповну відповідь — спробуй ще раз.");
                }

                var account = new SavedAccount
                {
                    Uuid = authResponse.SelectedProfile.Id ?? Guid.NewGuid().ToString(),
                    Username = authResponse.SelectedProfile.Name ?? username,
                    AccessToken = authResponse.AccessToken,
                    AuthType = "ElyBy",
                    LastUsedAt = DateTime.Now
                };

                UpsertAccount(account);
                ApplyAccount(account, save: true);
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, $"Помилка з'єднання з Ely.by: {ex.Message}");
            }
        }

        public Task<bool> RefreshSessionAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(IsAuthenticated);
        }

        public bool SwitchAccount(string uuid)
        {
            var account = _accounts.FirstOrDefault(a => a.Uuid == uuid);
            if (account == null) return false;

            account.LastUsedAt = DateTime.Now;
            ApplyAccount(account, save: true);
            return true;
        }

        public void RemoveAccount(string uuid)
        {
            var account = _accounts.FirstOrDefault(a => a.Uuid == uuid);
            if (account == null) return;

            _accounts.Remove(account);

            // Якщо видалили активний акаунт - виходимо з нього (але список лишається списком).
            if (_activeUuid == uuid)
            {
                Username = null;
                UUID = null;
                AccessToken = null;
                AuthType = "Offline";
                IsAuthenticated = false;
                _activeUuid = null;
            }

            SaveAccounts();
            AccountsChanged?.Invoke();
        }

        public void Logout()
        {
            // Вихід лише прибирає активний стан - сам акаунт лишається у SavedAccounts,
            // щоб можна було перемкнутись назад одним кліком без повторного вводу пароля/ніку.
            Username = null;
            UUID = null;
            AccessToken = null;
            AuthType = "Offline";
            IsAuthenticated = false;
            _activeUuid = null;

            SaveAccounts();
            AccountsChanged?.Invoke();
        }

        private void ApplyAccount(SavedAccount account, bool save)
        {
            Username = account.Username;
            UUID = account.Uuid;
            AccessToken = account.AccessToken;
            AuthType = account.AuthType;
            IsAuthenticated = true;
            _activeUuid = account.Uuid;

            if (save)
            {
                SaveAccounts();
                AccountsChanged?.Invoke();
            }
        }

        private void UpsertAccount(SavedAccount account)
        {
            var existing = _accounts.FirstOrDefault(a => a.Uuid == account.Uuid);
            if (existing != null)
            {
                existing.Username = account.Username;
                existing.AccessToken = account.AccessToken;
                existing.AuthType = account.AuthType;
                existing.LastUsedAt = account.LastUsedAt;
            }
            else
            {
                _accounts.Add(account);
            }
        }

        private static string OfflineUuidFromName(string name)
        {
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
            hash[6] = (byte)((hash[6] & 0x0F) | 0x30); // версія 3
            hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // варіант
            var hex = Convert.ToHexString(hash).ToLowerInvariant();
            return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
        }

        private void SaveAccounts()
        {
            try
            {
                var data = new AccountsData { Accounts = _accounts, ActiveUuid = _activeUuid };
                File.WriteAllText(_accountsFilePath, JsonSerializer.Serialize(data));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Не вдалося зберегти акаунти: {ex.Message}");
            }
        }

        private void LoadAccounts()
        {
            try
            {
                if (!File.Exists(_accountsFilePath)) return;
                var json = File.ReadAllText(_accountsFilePath);
                var data = JsonSerializer.Deserialize<AccountsData>(json);
                if (data?.Accounts != null) _accounts.AddRange(data.Accounts);
                _activeUuid = data?.ActiveUuid;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Не вдалося завантажити акаунти: {ex.Message}");
            }
        }

        private void MigrateOldSession(string oldSessionPath)
        {
            try
            {
                var json = File.ReadAllText(oldSessionPath);
                var old = JsonSerializer.Deserialize<OldSessionData>(json);
                if (old != null && old.IsAuthenticated && !string.IsNullOrEmpty(old.Username))
                {
                    var uuid = old.UUID ?? OfflineUuidFromName(old.Username);
                    var account = new SavedAccount
                    {
                        Uuid = uuid,
                        Username = old.Username,
                        AccessToken = old.AccessToken ?? "offline_access_token",
                        AuthType = old.AuthType ?? "Offline",
                        LastUsedAt = DateTime.Now
                    };
                    _accounts.Add(account);
                    _activeUuid = uuid;
                    SaveAccounts();
                }
                File.Delete(oldSessionPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Не вдалося мігрувати стару сесію: {ex.Message}");
            }
        }

        private class AccountsData
        {
            public List<SavedAccount>? Accounts { get; set; }
            public string? ActiveUuid { get; set; }
        }

        private class OldSessionData
        {
            public string? Username { get; set; }
            public string? UUID { get; set; }
            public string? AccessToken { get; set; }
            public bool IsAuthenticated { get; set; }
            public string? AuthType { get; set; }
        }

        // ==== DTO для відповіді Ely.by /auth/authenticate ====
        private class ElyByAuthResponse
        {
            public string? AccessToken { get; set; }
            public string? ClientToken { get; set; }
            public ElyByProfile? SelectedProfile { get; set; }
        }

        private class ElyByProfile
        {
            public string? Id { get; set; }
            public string? Name { get; set; }
        }

        private class ElyByErrorResponse
        {
            public string? Error { get; set; }
            public string? ErrorMessage { get; set; }
        }
    }
}