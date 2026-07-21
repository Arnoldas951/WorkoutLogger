namespace WorkoutLogger.Services.Abstraction
{
    public interface ITokenService
    {
        string GenerateToken(int userId, string username);
    }
}