namespace WorkoutLogger.Models
{
    public class AuthResponse
    {
        public string AccessToken { get; set; } = string.Empty;

        /// <summary>
        /// Opaque, single use. Presenting it returns a new pair and invalidates this one.
        /// </summary>
        public string RefreshToken { get; set; } = string.Empty;

        /// <summary>
        /// Lifetime of the access token, so clients can refresh proactively
        /// instead of waiting to be surprised by a 401.
        /// </summary>
        public int ExpiresInSeconds { get; set; }
    }
}
