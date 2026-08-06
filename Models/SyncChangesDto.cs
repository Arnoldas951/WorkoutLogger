namespace WorkoutLogger.Models
{
    /// <summary>
    /// Everything a client needs to catch up, under a single watermark.
    ///
    /// Both entity types travel together deliberately. Two endpoints with two
    /// watermarks can succeed and fail independently, leaving a device that has
    /// activities newer than the workouts they are attached to.
    /// </summary>
    public class SyncChangesDto
    {
        public EntityChangesDto<WorkoutDto> Workouts { get; set; } = new();
        public EntityChangesDto<ActivityDto> Activities { get; set; } = new();

        /// <summary>
        /// Pass back as <c>since</c> next time. Server time — a client must never
        /// substitute its own clock here.
        /// </summary>
        public DateTime SyncedAt { get; set; }
    }

    public class EntityChangesDto<T>
    {
        /// <summary>Created or updated since the watermark, full payload.</summary>
        public List<T> Changed { get; set; } = new();

        /// <summary>
        /// Deleted since the watermark. PublicIds only — there is nothing left to
        /// send, and the client just needs to drop its local copy.
        /// </summary>
        public List<Guid> Deleted { get; set; } = new();
    }
}
