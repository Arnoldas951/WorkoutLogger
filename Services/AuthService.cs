using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WorkoutLogger.Context;
using WorkoutLogger.Entities;
using WorkoutLogger.Models;
using WorkoutLogger.Services.Abstraction;

namespace WorkoutLogger.Services
{
    public class AuthService : IAuthService
    {
        private readonly WorkoutDbContext _dbContext;
        private readonly ITokenService _tokenService;
        private readonly ILogger<AuthService>? _logger;
        private readonly PasswordHasher<User> _passwordHasher = new();

        public AuthService(WorkoutDbContext dbContext, ITokenService tokenService, ILogger<AuthService>? logger = null)
        {
            _dbContext = dbContext;
            _tokenService = tokenService;
            _logger = logger;
        }

        public async Task<bool> RegisterAsync(string username, string password)
        {
            var exists = await _dbContext.Users.AnyAsync(u => u.Username == username);
            if (exists)
            {
                return false;
            }

            var user = new User { Username = username };
            user.PasswordHash = _passwordHasher.HashPassword(user, password);

            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();

            return true;
        }

        public async Task<AuthResponse?> LoginAsync(string username, string password)
        {
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null)
            {
                // Hash anyway so an unknown username and a wrong password take roughly
                // the same time, rather than advertising which accounts exist.
                _passwordHasher.HashPassword(new User { Username = username }, password);
                return null;
            }

            var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
            if (result == PasswordVerificationResult.Failed)
            {
                return null;
            }

            if (result == PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.PasswordHash = _passwordHasher.HashPassword(user, password);
            }

            return await IssueAsync(user);
        }

        public async Task<AuthResponse?> RefreshAsync(string refreshToken)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return null;
            }

            var hash = _tokenService.HashRefreshToken(refreshToken);
            var stored = await _dbContext.RefreshTokens
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.TokenHash == hash);

            if (stored == null)
            {
                return null;
            }

            var now = DateTime.UtcNow;

            if (stored.RevokedAt is not null)
            {
                // Two ways a token can be revoked, and they mean very different things.
                //
                // Rotated out (it has a replacement) and then presented again: two
                // parties hold the same token. We cannot tell the attacker from the
                // user, so every session for that user dies and they sign in again.
                //
                // Revoked by an explicit logout (no replacement): someone retried
                // after signing out. Unremarkable. Reject it and leave the user's
                // other devices alone - logging out on a phone must not sign them
                // out on a laptop.
                if (stored.ReplacedByTokenId is not null)
                {
                    _logger?.LogWarning(
                        "Refresh token replay detected for user {UserId}; revoking all active tokens.",
                        stored.UserId);

                    await RevokeAllForUserAsync(stored.UserId, now);
                    await _dbContext.SaveChangesAsync();
                }

                return null;
            }

            if (now >= stored.ExpiresAt || stored.User == null)
            {
                return null;
            }

            return await IssueAsync(stored.User, rotating: stored, now: now);
        }

        public async Task<bool> RevokeAsync(string refreshToken)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return false;
            }

            var hash = _tokenService.HashRefreshToken(refreshToken);
            var stored = await _dbContext.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);

            var now = DateTime.UtcNow;
            if (stored == null || !stored.IsActive(now))
            {
                return false;
            }

            stored.RevokedAt = now;
            await _dbContext.SaveChangesAsync();

            return true;
        }

        /// <summary>
        /// Issues a fresh access + refresh pair. When <paramref name="rotating"/> is
        /// supplied, that token is revoked and linked to its replacement so the
        /// rotation chain stays walkable.
        /// </summary>
        private async Task<AuthResponse> IssueAsync(User user, RefreshToken? rotating = null, DateTime? now = null)
        {
            var issuedAt = now ?? DateTime.UtcNow;
            var (rawToken, tokenHash) = _tokenService.GenerateRefreshToken();

            var refreshToken = new RefreshToken
            {
                UserId = user.Id,
                TokenHash = tokenHash,
                CreatedAt = issuedAt,
                ExpiresAt = issuedAt.Add(_tokenService.RefreshTokenLifetime)
            };

            _dbContext.RefreshTokens.Add(refreshToken);

            if (rotating != null)
            {
                rotating.RevokedAt = issuedAt;
            }

            // Saved once so the replacement gets its identity value, then linked and
            // saved again. Two round trips, but it keeps ReplacedByTokenId honest
            // without the caller having to invent ids.
            await _dbContext.SaveChangesAsync();

            if (rotating != null)
            {
                rotating.ReplacedByTokenId = refreshToken.Id;
                await _dbContext.SaveChangesAsync();
            }

            return new AuthResponse
            {
                AccessToken = _tokenService.GenerateAccessToken(user.Id, user.Username),
                RefreshToken = rawToken,
                ExpiresInSeconds = _tokenService.AccessTokenLifetimeSeconds
            };
        }

        private async Task RevokeAllForUserAsync(int userId, DateTime now)
        {
            var active = await _dbContext.RefreshTokens
                .Where(t => t.UserId == userId && t.RevokedAt == null)
                .ToListAsync();

            foreach (var token in active)
            {
                token.RevokedAt = now;
            }
        }
    }
}
