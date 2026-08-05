using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using WorkoutLogger.Context;
using WorkoutLogger.Services;
using Xunit;

namespace WorkoutLogger.Tests
{
    public class AuthServiceTests
    {
        private static WorkoutDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<WorkoutDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            return new WorkoutDbContext(options);
        }

        /// <summary>
        /// The real TokenService rather than a stub, so these tests exercise the
        /// actual hashing and randomness instead of a fake that could hide a bug.
        /// </summary>
        private static TokenService CreateTokenService(int accessMinutes = 15, int refreshDays = 30)
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Key"] = "this-is-a-test-signing-key-that-is-long-enough",
                    ["Jwt:Issuer"] = "WorkoutLogger.Tests",
                    ["Jwt:Audience"] = "WorkoutLogger.Tests",
                    ["Jwt:ExpiryMinutes"] = accessMinutes.ToString(),
                    ["Jwt:RefreshTokenDays"] = refreshDays.ToString()
                })
                .Build();

            return new TokenService(config);
        }

        private static AuthService CreateService(WorkoutDbContext db) =>
            new(db, CreateTokenService());

        [Fact]
        public async Task RegisterAsync_ReturnsTrue_AndStoresHashedPassword()
        {
            using var db = CreateContext();
            var service = CreateService(db);

            var created = await service.RegisterAsync("alice", "hunter2");

            Assert.True(created);
            var stored = await db.Users.SingleAsync(u => u.Username == "alice");
            Assert.NotEqual("hunter2", stored.PasswordHash);
        }

        [Fact]
        public async Task RegisterAsync_ReturnsFalse_WhenUsernameAlreadyTaken()
        {
            using var db = CreateContext();
            var service = CreateService(db);
            await service.RegisterAsync("alice", "hunter2");

            var created = await service.RegisterAsync("alice", "differentPassword");

            Assert.False(created);
            Assert.Equal(1, await db.Users.CountAsync(u => u.Username == "alice"));
        }

        [Fact]
        public async Task LoginAsync_ReturnsBothTokens_ForCorrectPassword()
        {
            using var db = CreateContext();
            var service = CreateService(db);
            await service.RegisterAsync("alice", "hunter2");

            var response = await service.LoginAsync("alice", "hunter2");

            Assert.NotNull(response);
            Assert.NotEmpty(response!.AccessToken);
            Assert.NotEmpty(response.RefreshToken);
            Assert.Equal(15 * 60, response.ExpiresInSeconds);
        }

        [Fact]
        public async Task LoginAsync_ReturnsNull_ForWrongPassword()
        {
            using var db = CreateContext();
            var service = CreateService(db);
            await service.RegisterAsync("alice", "hunter2");

            Assert.Null(await service.LoginAsync("alice", "wrongPassword"));
        }

        [Fact]
        public async Task LoginAsync_ReturnsNull_ForUnknownUsername()
        {
            using var db = CreateContext();
            var service = CreateService(db);

            Assert.Null(await service.LoginAsync("nobody", "whatever"));
        }

        [Fact]
        public async Task LoginAsync_StoresOnlyTheHashOfTheRefreshToken()
        {
            using var db = CreateContext();
            var service = CreateService(db);
            await service.RegisterAsync("alice", "hunter2");

            var response = await service.LoginAsync("alice", "hunter2");

            var stored = await db.RefreshTokens.SingleAsync();
            Assert.NotEqual(response!.RefreshToken, stored.TokenHash);
            Assert.DoesNotContain(response.RefreshToken, stored.TokenHash);
        }

        [Fact]
        public async Task RefreshAsync_ReturnsNewPair_AndRevokesTheOldToken()
        {
            using var db = CreateContext();
            var service = CreateService(db);
            await service.RegisterAsync("alice", "hunter2");
            var first = await service.LoginAsync("alice", "hunter2");

            var second = await service.RefreshAsync(first!.RefreshToken);

            Assert.NotNull(second);
            Assert.NotEqual(first.RefreshToken, second!.RefreshToken);

            var tokens = await db.RefreshTokens.OrderBy(t => t.Id).ToListAsync();
            Assert.Equal(2, tokens.Count);
            Assert.NotNull(tokens[0].RevokedAt);
            Assert.Equal(tokens[1].Id, tokens[0].ReplacedByTokenId);
            Assert.Null(tokens[1].RevokedAt);
        }

        [Fact]
        public async Task RefreshAsync_ReturnsNull_ForUnknownToken()
        {
            using var db = CreateContext();
            var service = CreateService(db);

            Assert.Null(await service.RefreshAsync("not-a-real-token"));
        }

        [Fact]
        public async Task RefreshAsync_ReturnsNull_ForEmptyToken()
        {
            using var db = CreateContext();
            var service = CreateService(db);

            Assert.Null(await service.RefreshAsync(""));
        }

        [Fact]
        public async Task RefreshAsync_ReturnsNull_WhenTokenHasExpired()
        {
            using var db = CreateContext();
            var service = CreateService(db);
            await service.RegisterAsync("alice", "hunter2");
            var login = await service.LoginAsync("alice", "hunter2");

            var stored = await db.RefreshTokens.SingleAsync();
            stored.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();

            Assert.Null(await service.RefreshAsync(login!.RefreshToken));
        }

        [Fact]
        public async Task RefreshAsync_RevokesEveryToken_WhenAnAlreadyUsedTokenIsReplayed()
        {
            using var db = CreateContext();
            var service = CreateService(db);
            await service.RegisterAsync("alice", "hunter2");

            var first = await service.LoginAsync("alice", "hunter2");
            var second = await service.RefreshAsync(first!.RefreshToken);

            // Someone replays the token that was already exchanged. We cannot tell
            // whether the attacker or the real user holds the newer one, so both die.
            var replay = await service.RefreshAsync(first.RefreshToken);

            Assert.Null(replay);
            Assert.All(await db.RefreshTokens.ToListAsync(), t => Assert.NotNull(t.RevokedAt));

            // The token issued just before the replay is dead too.
            Assert.Null(await service.RefreshAsync(second!.RefreshToken));
        }

        [Fact]
        public async Task RefreshAsync_DoesNotTouchAnotherUsersTokens_OnReplay()
        {
            using var db = CreateContext();
            var service = CreateService(db);
            await service.RegisterAsync("alice", "hunter2");
            await service.RegisterAsync("bob", "hunter2");

            var alice = await service.LoginAsync("alice", "hunter2");
            var bob = await service.LoginAsync("bob", "hunter2");
            await service.RefreshAsync(alice!.RefreshToken);

            await service.RefreshAsync(alice.RefreshToken); // replay

            // Bob is a bystander and should still be signed in.
            Assert.NotNull(await service.RefreshAsync(bob!.RefreshToken));
        }

        [Fact]
        public async Task RevokeAsync_InvalidatesTheToken()
        {
            using var db = CreateContext();
            var service = CreateService(db);
            await service.RegisterAsync("alice", "hunter2");
            var login = await service.LoginAsync("alice", "hunter2");

            var revoked = await service.RevokeAsync(login!.RefreshToken);

            Assert.True(revoked);
            Assert.Null(await service.RefreshAsync(login.RefreshToken));
        }

        [Fact]
        public async Task RevokeAsync_ReturnsFalse_ForUnknownToken()
        {
            using var db = CreateContext();
            var service = CreateService(db);

            Assert.False(await service.RevokeAsync("not-a-real-token"));
        }

        [Fact]
        public async Task RevokeAsync_ReturnsFalse_WhenAlreadyRevoked()
        {
            using var db = CreateContext();
            var service = CreateService(db);
            await service.RegisterAsync("alice", "hunter2");
            var login = await service.LoginAsync("alice", "hunter2");
            await service.RevokeAsync(login!.RefreshToken);

            Assert.False(await service.RevokeAsync(login.RefreshToken));
        }

        [Fact]
        public async Task RevokingOneSession_LeavesOtherSessionsAlone()
        {
            using var db = CreateContext();
            var service = CreateService(db);
            await service.RegisterAsync("alice", "hunter2");

            var phone = await service.LoginAsync("alice", "hunter2");
            var laptop = await service.LoginAsync("alice", "hunter2");

            await service.RevokeAsync(phone!.RefreshToken);

            Assert.Null(await service.RefreshAsync(phone.RefreshToken));
            Assert.NotNull(await service.RefreshAsync(laptop!.RefreshToken));
        }

        [Fact]
        public async Task RetryingAfterLogout_IsRejectedWithoutKillingOtherSessions()
        {
            using var db = CreateContext();
            var service = CreateService(db);
            await service.RegisterAsync("alice", "hunter2");

            var phone = await service.LoginAsync("alice", "hunter2");
            var laptop = await service.LoginAsync("alice", "hunter2");
            await service.RevokeAsync(phone!.RefreshToken);

            // A stale client retrying after logout looks superficially like a replay,
            // but a logged-out token has no replacement, so it is not evidence that
            // anyone else holds it. Rejecting is enough.
            await service.RefreshAsync(phone.RefreshToken);
            await service.RefreshAsync(phone.RefreshToken);

            Assert.NotNull(await service.RefreshAsync(laptop!.RefreshToken));
        }

        [Fact]
        public async Task ReplayOnOneDevice_StillSignsOutTheOtherDevices()
        {
            using var db = CreateContext();
            var service = CreateService(db);
            await service.RegisterAsync("alice", "hunter2");

            var phone = await service.LoginAsync("alice", "hunter2");
            var laptop = await service.LoginAsync("alice", "hunter2");

            // The phone's token is rotated normally, then the pre-rotation value is
            // presented again. That one *is* evidence of theft, so the blast radius
            // is deliberately every session the user has.
            await service.RefreshAsync(phone!.RefreshToken);
            await service.RefreshAsync(phone.RefreshToken);

            Assert.Null(await service.RefreshAsync(laptop!.RefreshToken));
        }
    }
}
