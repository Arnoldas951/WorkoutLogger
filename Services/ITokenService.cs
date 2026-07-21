namespace WorkoutLogger.Services
{
    public interface ITokenService
    {
        string GenerateToken(int userId, string username);
    }
}