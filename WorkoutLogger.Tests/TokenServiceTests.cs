using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using WorkoutLogger.Services;
using Xunit;

namespace WorkoutLogger.Tests
{
    public class TokenServiceTests
    {
        private static TokenService CreateService(
            string? expiryMinutes = "60",
            string? refreshTokenDays = "30")
        {
            var settings = new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "this-is-a-test-signing-key-that-is-long-enough",
                ["Jwt:Issuer"] = "WorkoutLogger.Tests",
                ["Jwt:Audience"] = "WorkoutLogger.Tests",
                ["Jwt:ExpiryMinutes"] = expiryMinutes,
                ["Jwt:RefreshTokenDays"] = refreshTokenDays
            };

            var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

            return new TokenService(config);
        }

        [Fact]
        public void GenerateAccessToken_EmbedsUserIdAsNameIdentifierClaim()
        {
            var service = CreateService();

            var token = service.GenerateAccessToken(userId: 42, username: "alice");

            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            var nameIdentifier = jwt.Claims.Single(c => c.Type == ClaimTypes.NameIdentifier).Value;
            Assert.Equal("42", nameIdentifier);
        }

        [Fact]
        public void GenerateAccessToken_EmbedsUsername()
        {
            var service = CreateService();

            var token = service.GenerateAccessToken(userId: 42, username: "alice");

            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            Assert.Equal("alice", jwt.Claims.Single(c => c.Type == ClaimTypes.Name).Value);
        }

        [Fact]
        public void GenerateAccessToken_SetsExpiryFromConfiguration()
        {
            var service = CreateService(expiryMinutes: "15");

            var token = service.GenerateAccessToken(userId: 1, username: "alice");

            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            var lifetime = jwt.ValidTo - DateTime.UtcNow;

            Assert.InRange(lifetime.TotalMinutes, 14, 15.1);
        }

        [Fact]
        public void GenerateAccessToken_IssuesAUniqueJtiEachTime()
        {
            var service = CreateService();
            var handler = new JwtSecurityTokenHandler();

            var first = handler.ReadJwtToken(service.GenerateAccessToken(1, "alice"));
            var second = handler.ReadJwtToken(service.GenerateAccessToken(1, "alice"));

            Assert.NotEqual(
                first.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Jti).Value,
                second.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Jti).Value);
        }

        [Fact]
        public void LifetimesFallBackToSafeDefaults_WhenConfigurationIsMissing()
        {
            var service = CreateService(expiryMinutes: null, refreshTokenDays: null);

            Assert.Equal(15 * 60, service.AccessTokenLifetimeSeconds);
            Assert.Equal(TimeSpan.FromDays(30), service.RefreshTokenLifetime);
        }

        [Fact]
        public void GenerateRefreshToken_ReturnsADifferentValueEveryTime()
        {
            var service = CreateService();

            var tokens = Enumerable.Range(0, 200)
                .Select(_ => service.GenerateRefreshToken().Token)
                .ToList();

            Assert.Equal(tokens.Count, tokens.Distinct().Count());
        }

        [Fact]
        public void GenerateRefreshToken_ReturnsAHashThatIsNotTheToken()
        {
            var service = CreateService();

            var (token, hash) = service.GenerateRefreshToken();

            Assert.NotEqual(token, hash);
            Assert.Equal(64, hash.Length); // SHA-256 as hex
            Assert.Equal(hash, service.HashRefreshToken(token));
        }

        [Fact]
        public void GenerateRefreshToken_IsUrlSafe()
        {
            var service = CreateService();

            var (token, _) = service.GenerateRefreshToken();

            // Base64Url output only: no +, / or = to escape in a URL or JSON body.
            Assert.DoesNotContain("+", token);
            Assert.DoesNotContain("/", token);
            Assert.DoesNotContain("=", token);
        }

        [Fact]
        public void HashRefreshToken_IsStable()
        {
            var service = CreateService();

            Assert.Equal(service.HashRefreshToken("abc"), service.HashRefreshToken("abc"));
            Assert.NotEqual(service.HashRefreshToken("abc"), service.HashRefreshToken("abd"));
        }
    }
}
