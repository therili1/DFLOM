using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Launcher.Models;

namespace Launcher.Services
{
    public interface IAuthenticationService
    {
        bool IsAuthenticated { get; }
        string? Username { get; }
        string? UUID { get; }
        string? AccessToken { get; }

        /// <summary>Усі акаунти, якими користувач колись логінився в цьому лаунчері, найновіші зверху.</summary>
        IReadOnlyList<SavedAccount> SavedAccounts { get; }

        /// <summary>Спрацьовує при вході/виході/зміні активного акаунта чи списку збережених - для оновлення UI.</summary>
        event Action? AccountsChanged;

        Task<bool> LoginWithMicrosoftAsync(CancellationToken cancellationToken = default);
        Task<(bool success, string? errorMessage)> LoginWithElyByAsync(string username, string password, CancellationToken cancellationToken = default);
        Task<bool> LoginOfflineAsync(string username);
        Task<bool> RefreshSessionAsync(CancellationToken cancellationToken = default);

        /// <summary>Перемкнутись на вже збережений акаунт без повторного вводу пароля/ніку.</summary>
        bool SwitchAccount(string uuid);

        /// <summary>Прибрати акаунт зі списку збережених назавжди (Ely.by-акаунти доведеться логінити заново).</summary>
        void RemoveAccount(string uuid);

        /// <summary>Вийти з поточного акаунта (не видаляючи його зі збережених - можна перемкнутись назад).</summary>
        void Logout();
    }
}

