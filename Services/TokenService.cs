using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Configuration;
using WorkoutLogger.Services.Abstraction;

namespace WorkoutLogger.Services
{
    public class TokenService : ITokenService
    {
        private readonly IConfiguration _configuration;

        public TokenService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private IConfigurationSection JwtSection => _configuration.GetSection("Jwt");

        /// <summary>
        /// Short by design. An access token cannot be revoked once issued, so its
        /// lifetime is the window an attacker gets with a stolen one. Refresh tokens
        /// are what keep the user signed in across that boundary.
        /// </summary>
        public int AccessTokenLifetimeSeconds =>
            int.TryParse(JwtSection["ExpiryMinutes"], out var minutes) ? minutes * 60 : 15 * 60;

        public TimeSpan RefreshTokenLifetime =>
            TimeSpan.FromDays(int.TryParse(JwtSection["RefreshTokenDays"], out var days) ? days : 30);

        public string GenerateAccessToken(int userId, string username)
        {
            var key = JwtSection["Key"] ?? throw new InvalidOperationException("Jwt:Key configuration value is missing.");

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, username),
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Name, username),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var keyBytes = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
            var creds = new SigningCredentials(keyBytes, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: JwtSection["Issuer"],
                audience: JwtSection["Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddSeconds(AccessTokenLifetimeSeconds),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public (string Token, string Hash) GenerateRefreshToken()
        {
            // 256 bits of CSPRNG output, url-safe so it survives a JSON body,
            // a header or a query string without escaping.
            var bytes = RandomNumberGenerator.GetBytes(32);
            var token = Base64UrlEncoder.Encode(bytes);

            return (token, HashRefreshToken(token));
        }

        public string HashRefreshToken(string token)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return Convert.ToHexString(hash);
        }
    }
}
