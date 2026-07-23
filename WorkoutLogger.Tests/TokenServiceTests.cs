using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using WorkoutLogger.Services;
using Xunit;

namespace WorkoutLogger.Tests
{
    public class TokenServiceTests
    {
        private static TokenService CreateService()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Key"] = "this-is-a-test-signing-key-that-is-long-enough",
                    ["Jwt:Issuer"] = "WorkoutLogger.Tests",
                    ["Jwt:Audience"] = "WorkoutLogger.Tests",
                    ["Jwt:ExpiryMinutes"] = "60"
                })
                .Build();

            return new TokenService(config);
        }

        [Fact]
        public void GenerateToken_EmbedsUserIdAsNameIdentifierClaim()
        {
            var service = CreateService();

            var token = service.GenerateToken(userId: 42, username: "alice");

            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            var nameIdentifier = jwt.Claims.Single(c => c.Type == ClaimTypes.NameIdentifier).Value;
            Assert.Equal("42", nameIdentifier);
        }

        [Fact]
        public void GenerateToken_EmbedsUsername()
        {
            var service = CreateService();

            var token = service.GenerateToken(userId: 42, username: "alice");

            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            Assert.Equal("alice", jwt.Claims.Single(c => c.Type == ClaimTypes.Name).Value);
        }
    }
}
