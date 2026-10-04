namespace WorkoutLogger.Entities
{
    /// <summary>
    /// One set as the watch recorded it, optionally paired with the set you
    /// logged by hand.
    ///
    /// The division of labour is deliberate and comes from looking at real data:
    /// Garmin's exercise detection is unreliable (a third of a probed session
    /// came back as UNKNOWN) and it never knows the load, while the logged set
    /// has the exercise name, the reps you intended and the weight. What the
    /// watch does know is timing and heart rate. So the exercise, reps and weight
    /// come from <see cref="ExerciseSet"/>; this row contributes
    /// <see cref="AverageHeartRate"/>, <see cref="MaxHeartRate"/> and
    /// <see cref="RestSecondsAfter"/>.
    ///
    /// Garmin's own reps and category are kept as a raw record only. Both turned
    /// out to be unusable: the category is the literal string UNKNOWN on a third
    /// of a real session, and the rep counter returns 0 for movements it cannot
    /// recognise. Neither is used for matching, and neither should be displayed
    /// beside the logged values, where a wrong number reads as a correction.
    /// </summary>
    public class GarminExerciseSet
    {
        public int Id { get; set; }

        public int GarminActivityDetailId { get; set; }
        public GarminActivityDetail? GarminActivityDetail { get; set; }

        /// <summary>
        /// 0-based position among the ACTIVE sets of this activity, in time order.
        /// This is what the ordinal match aligns on, so it must stay stable: a
        /// re-sync of the same activity has to produce the same indices.
        /// </summary>
        public int SetIndex { get; set; }

        public DateTime StartTime { get; set; }
        public double? DurationSeconds { get; set; }

        /// <summary>
        /// Garmin's rep count. **Not authoritative and not for display.**
        ///
        /// The watch's rep counter does not merely drift, it fails: 10 kettlebell
        /// adductors are reported as 0. Reps come from <see cref="ExerciseSet.Repetitions"/>,
        /// which the user enters after finishing the set. This column is retained
        /// as a raw record of what the device claimed, nothing more - it is not
        /// used for matching and must not be shown next to the logged reps, where
        /// it reads as a correction.
        /// </summary>
        public int? GarminReps { get; set; }

        /// <summary>
        /// Garmin's guess at the movement, from `exercises[0].category` - the
        /// `name` field is null in practice. Null when the watch had no idea,
        /// which happened on 11 of 30 sets in the probed session.
        /// </summary>
        public string? GarminCategory { get; set; }

        /// <summary>Confidence Garmin reports in that category, 0-100.</summary>
        public double? GarminCategoryProbability { get; set; }

        // Windowed out of the activity's heart-rate series over this set's own
        // start..end. The series itself is not stored - it is ~174KB per session
        // and these three numbers are the whole reason to fetch it.
        public int? AverageHeartRate { get; set; }
        public int? MaxHeartRate { get; set; }
        public int? MinHeartRate { get; set; }

        /// <summary>
        /// Length of the REST set that followed, in seconds. Null on the last set
        /// of a session, or when the watch recorded no rest after it.
        /// </summary>
        public int? RestSecondsAfter { get; set; }

        /// <summary>
        /// The logged set this was matched to, or null when the alignment could
        /// not place it - the watch detected a set you did not log, or the counts
        /// disagreed badly enough that guessing would be worse than admitting it.
        /// Null is a normal outcome and must stay readable as "unmatched" rather
        /// than "no heart rate".
        /// </summary>
        public int? ExerciseSetId { get; set; }
        public ExerciseSet? ExerciseSet { get; set; }

        /// <summary>
        /// 1.0 when the set counts agreed exactly and the reps matched; lower as
        /// the alignment had to stretch. Below ~0.5 means treat the pairing as a
        /// suggestion. Null when unmatched.
        /// </summary>
        public double? MatchConfidence { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
