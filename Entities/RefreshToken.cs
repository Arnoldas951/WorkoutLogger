namespace WorkoutLogger.Entities
{
    public class RefreshToken
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        public User? User { get; set; }

        /// <summary>
        /// SHA-256 of the raw token, never the token itself. A refresh token is a
        /// bearer credential: if this table leaks, hashes alone are not usable.
        /// Plain SHA-256 is enough here because the token is 256 bits of CSPRNG
        /// output, so there is nothing to brute force.
        /// </summary>
        public string TokenHash { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime ExpiresAt { get; set; }

        /// <summary>
        /// Set when the token is rotated out on refresh, or on explicit logout.
        /// </summary>
        public DateTime? RevokedAt { get; set; }

        /// <summary>
        /// Points at the token that replaced this one. Lets us walk the rotation
        /// chain when an already-used token is presented again.
        /// </summary>
        public int? ReplacedByTokenId { get; set; }

        public bool IsActive(DateTime utcNow) => RevokedAt is null && utcNow < ExpiresAt;
    }
}
