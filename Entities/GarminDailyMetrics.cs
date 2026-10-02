namespace WorkoutLogger.Entities
{
    /// <summary>
    /// A day of Garmin's own measurements - the things Health Connect does not
    /// carry at all: HRV, body battery, training readiness, sleep scoring.
    ///
    /// Per-day, not per-session, so it deliberately has no relationship to
    /// <see cref="Activity"/> or <see cref="Workout"/>. A workout screen that
    /// wants context looks up the day, it does not join through the activity.
    ///
    /// Written by the garmin-sync sidecar, never by the app. Nothing here can be
    /// authored by a user, so there is no PublicId, no outbox and no soft delete:
    /// re-running the sync for a date is the only way it changes.
    /// </summary>
    public class GarminDailyMetrics
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        public User? User { get; set; }

        public DateOnly Date { get; set; }

        // All nullable, and for the same reason as Activity's aggregates: a night
        // without the watch has no HRV, and 0 would be a lie rather than a gap.
        public int? RestingHeartRate { get; set; }
        public int? HrvLastNightAvg { get; set; }
        public string? HrvStatus { get; set; }
        public int? BodyBatteryHigh { get; set; }
        public int? BodyBatteryLow { get; set; }
        public int? SleepScore { get; set; }
        public int? SleepSeconds { get; set; }
        public int? TrainingReadinessScore { get; set; }
        public string? TrainingStatus { get; set; }
        public double? Vo2MaxRunning { get; set; }
        public int? AverageStress { get; set; }

        /// <summary>
        /// Everything the six endpoints returned, verbatim.
        ///
        /// The columns above are the ones worth indexing and querying; this is
        /// the rest. Same trade the workout payload makes: a Garmin field nobody
        /// has thought about yet is still here when someone does, and promoting
        /// one to a column later is a migration rather than a re-fetch of history
        /// that Garmin may no longer serve.
        /// </summary>
        public string RawJson { get; set; } = "{}";

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
