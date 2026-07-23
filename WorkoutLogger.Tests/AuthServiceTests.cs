using Microsoft.EntityFrameworkCore;
using WorkoutLogger.Context;
using WorkoutLogger.Services;
using WorkoutLogger.Services.Abstraction;
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

        private class FakeTokenService : ITokenService
        {
            public string GenerateToken(int userId, string username) => $"token-for-{userId}";
        }

        [Fact]
        public async Task RegisterAsync_ReturnsTrue_AndStoresHashedPassword()
        {
            using var db = CreateContext();
            var service = new AuthService(db, new FakeTokenService());

            var created = await service.RegisterAsync("alice", "hunter2");

            Assert.True(created);
            var stored = await db.Users.SingleAsync(u => u.Username == "alice");
            Assert.NotEqual("hunter2", stored.PasswordHash);
        }

        [Fact]
        public async Task RegisterAsync_ReturnsFalse_WhenUsernameAlreadyTaken()
        {
            using var db = CreateContext();
            var service = new AuthService(db, new FakeTokenService());
            await service.RegisterAsync("alice", "hunter2");

            var created = await service.RegisterAsync("alice", "differentPassword");

            Assert.False(created);
            Assert.Equal(1, await db.Users.CountAsync(u => u.Username == "alice"));
        }

        [Fact]
        public async Task LoginAsync_ReturnsToken_ForCorrectPassword()
        {
            using var db = CreateContext();
            var service = new AuthService(db, new FakeTokenService());
            await service.RegisterAsync("alice", "hunter2");

            var token = await service.LoginAsync("alice", "hunter2");

            Assert.NotNull(token);
        }

        [Fact]
        public async Task LoginAsync_ReturnsNull_ForWrongPassword()
        {
            using var db = CreateContext();
            var service = new AuthService(db, new FakeTokenService());
            await service.RegisterAsync("alice", "hunter2");

            var token = await service.LoginAsync("alice", "wrongPassword");

            Assert.Null(token);
        }

        [Fact]
        public async Task LoginAsync_ReturnsNull_ForUnknownUsername()
        {
            using var db = CreateContext();
            var service = new AuthService(db, new FakeTokenService());

            var token = await service.LoginAsync("nobody", "whatever");

            Assert.Null(token);
        }
    }
}
