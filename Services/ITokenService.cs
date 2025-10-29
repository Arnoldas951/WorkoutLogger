namespace WorkoutLogger.Services
{
    public interface ITokenService
    {
        string GenerateToken(string username);
    }
}
