using WorkoutLogger.Models;

namespace WorkoutLogger.Services.Abstraction
{
    public interface IAuthService
    {
        Task<bool> RegisterAsync(string username, string password);

        /// <summary>Null when the credentials do not match.</summary>
        Task<AuthResponse?> LoginAsync(string username, string password);

        /// <summary>
        /// Exchanges a refresh token for a new pair, invalidating the old one.
        /// Null when the token is unknown, expired or already used.
        /// </summary>
        Task<AuthResponse?> RefreshAsync(string refreshToken);

        /// <summary>Revokes a single refresh token. False when it was not active.</summary>
        Task<bool> RevokeAsync(string refreshToken);
    }
}
