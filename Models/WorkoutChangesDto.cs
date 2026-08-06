namespace WorkoutLogger.Models
{
    /// <summary>
    /// Everything that changed for one user since a given watermark.
    /// </summary>
    public class WorkoutChangesDto
    {
        /// <summary>Created or updated since the watermark, full payload.</summary>
        public List<WorkoutDto> Changed { get; set; } = new();

        /// <summary>
        /// Deleted since the watermark. Ids only - there is nothing left to send,
        /// and the client just needs to know to drop its local copy.
        /// </summary>
        public List<Guid> Deleted { get; set; } = new();

        /// <summary>
        /// Watermark to pass as <c>since</c> on the next call. Server time, so
        /// clients never have to trust their own clock.
        /// </summary>
        public DateTime SyncedAt { get; set; }
    }
}
