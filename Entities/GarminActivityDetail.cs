namespace WorkoutLogger.Entities
{
    /// <summary>
    /// The Garmin-side view of a recorded session, sitting beside the
    /// Health Connect <see cref="Activity"/> rather than replacing it.
    ///
    /// Two sources describe the same session through different handles: Health
    /// Connect's record metadata id and Garmin's activityId are unrelated, so
    /// <see cref="Activity.ExternalId"/> cannot reconcile them. They are matched
    /// on time overlap instead, which is approximate by nature - the watch and
    /// the phone disagree about the second a session started. Hence
    /// <see cref="ActivityId"/> is nullable: an unmatched row is the normal
    /// state, not a failure.
    /// </summary>
    public class GarminActivityDetail
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        public User? User { get; set; }

        /// <summary>Garmin's own activity id. Stable, and the dedupe key.</summary>
        public long GarminActivityId { get; set; }

        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string ActivityType { get; set; } = string.Empty;
        public string? Title { get; set; }
        public double? DurationSeconds { get; set; }

        // Overlaps Activity's aggregates on purpose. Keeping both means the two
        // sources can be compared rather than silently reconciled - if they
        // disagree about average heart rate, that is worth being able to see.
        public int? AverageHeartRate { get; set; }
        public int? MaxHeartRate { get; set; }
        public double? Calories { get; set; }
        public double? DistanceMeters { get; set; }

        // No Health Connect equivalent.
        public double? AerobicTrainingEffect { get; set; }
        public double? AnaerobicTrainingEffect { get; set; }
        public double? AverageWatts { get; set; }

        // Strength sessions only. ActiveSetCount is trustworthy and is what the
        // set matching is built on. TotalReps is the sum of the watch's own rep
        // counts and inherits their unreliability - see GarminExerciseSet.GarminReps -
        // so it is a raw record, not a figure to report. TotalWeightKg is null in
        // practice: the watch never knows the load.
        public int? ActiveSetCount { get; set; }
        public int? TotalReps { get; set; }
        public double? TotalWeightKg { get; set; }

        /// <summary>
        /// Null until the time-overlap match finds a Health Connect activity, and
        /// null forever for a session the phone never saw.
        /// </summary>
        public int? ActivityId { get; set; }
        public Activity? Activity { get; set; }

        /// <summary>The individual sets, with per-set heart rate and rest.</summary>
        public List<GarminExerciseSet> Sets { get; set; } = new();

        public string RawSummaryJson { get; set; } = "{}";

        /// <summary>Per-set reps, weight and exercise names. Null off strength days.</summary>
        public string? RawSetsJson { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
