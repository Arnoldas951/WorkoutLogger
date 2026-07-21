using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WorkoutLogger.Context;
using WorkoutLogger.Entities;
using WorkoutLogger.Services.Abstraction;

namespace WorkoutLogger.Services
{
    public class AuthService : IAuthService
    {
        private readonly WorkoutDbContext _dbContext;
        private readonly ITokenService _tokenService;
        private readonly PasswordHasher<User> _passwordHasher = new();

        public AuthService(WorkoutDbContext dbContext, ITokenService tokenService)
        {
            _dbContext = dbContext;
            _tokenService = tokenService;
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

        public async Task<string?> LoginAsync(string username, string password)
        {
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null)
            {
                return null;
            }

            var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
            if (result == PasswordVerificationResult.Failed)
            {
                return null;
            }

            return _tokenService.GenerateToken(user.Id, user.Username);
        }
    }
}
