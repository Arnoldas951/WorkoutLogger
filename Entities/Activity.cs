namespace WorkoutLogger.Entities
{
    /// <summary>
    /// A recorded session from a wearable - what Garmin and Strava call an
    /// "activity". Deliberately independent of <see cref="Workout"/>:
    ///
    ///   run with the watch, phone left at home  -> Activity, no Workout
    ///   gym session logged on the phone, no watch -> Workout, no Activity
    ///   both                                      -> linked
    ///
    /// Health Connect is the expected source. It stores an ExerciseSessionRecord
    /// as the spine of a session, with heart rate and calories as separate
    /// records over the same window, so the aggregates here are computed on the
    /// device before being sent.
    /// </summary>
    public class Activity
    {
        public int Id { get; set; }

        /// <summary>Sync identity, same role as Workout.PublicId.</summary>
        public Guid PublicId { get; set; }

        public int UserId { get; set; }
        public User? User { get; set; }

        /// <summary>
        /// The source system's own id - for Health Connect, the record metadata id.
        /// Re-importing the same session has to update rather than duplicate, and
        /// this is the only stable handle on it.
        /// </summary>
        public string? ExternalId { get; set; }

        public ActivitySource Source { get; set; } = ActivitySource.HealthConnect;

        /// <summary>
        /// Free text rather than an enum. Health Connect's exercise type list is
        /// long, changes between versions, and Garmin adds its own; an enum here
        /// would mean a migration every time one appears.
        /// </summary>
        public string ActivityType { get; set; } = string.Empty;

        public string? Title { get; set; }

        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }

        // Aggregates. All nullable: a session with no heart rate strap has no HR,
        // and 0 would be a lie rather than a missing value.
        public int? AverageHeartRate { get; set; }
        public int? MaxHeartRate { get; set; }
        public int? MinHeartRate { get; set; }
        public double? ActiveCalories { get; set; }
        public double? TotalCalories { get; set; }
        public double? DistanceMeters { get; set; }
        public int? Steps { get; set; }

        /// <summary>
        /// Optional link. Null means this activity stands alone - a run, or a
        /// session whose sets were never logged.
        /// </summary>
        public int? WorkoutId { get; set; }
        public Workout? Workout { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? DeletedAt { get; set; }

        public TimeSpan Duration => EndTime - StartTime;
    }

    public enum ActivitySource
    {
        HealthConnect = 0,
        Manual = 1,
        Import = 2
    }
}
