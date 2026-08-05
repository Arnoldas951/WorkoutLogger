namespace WorkoutLogger.Entities
{
    public class ExerciseSet
    {
        public int Id { get; set; }

        public int ExerciseId { get; set; }
        public Exercise? Exercise { get; set; }

        /// <summary>
        /// 1-based position of this set within the exercise.
        /// </summary>
        public int SetNumber { get; set; }

        public int Repetitions { get; set; }

        /// <summary>
        /// Load in kilograms. Zero for bodyweight movements.
        /// </summary>
        public double Weight { get; set; }

        /// <summary>
        /// Rate of perceived exertion, 0-10 in half steps. Null when not recorded.
        /// </summary>
        public double? Rpe { get; set; }

        public bool IsWarmup { get; set; }

        public string? Notes { get; set; }
    }
}
