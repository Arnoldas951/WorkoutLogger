namespace WorkoutLogger.Services.Abstraction
{
    public interface ITokenService
    {
        /// <summary>Short-lived signed JWT carrying the user's identity.</summary>
        string GenerateAccessToken(int userId, string username);

        /// <summary>Lifetime of the access token, so clients can refresh ahead of expiry.</summary>
        int AccessTokenLifetimeSeconds { get; }

        /// <summary>How long a freshly issued refresh token stays valid.</summary>
        TimeSpan RefreshTokenLifetime { get; }

        /// <summary>
        /// A new opaque refresh token. Returns the raw value to hand to the client
        /// and the hash to persist; the raw value is never stored.
        /// </summary>
        (string Token, string Hash) GenerateRefreshToken();

        /// <summary>Hashes a refresh token the client presented, for lookup.</summary>
        string HashRefreshToken(string token);
    }
}
