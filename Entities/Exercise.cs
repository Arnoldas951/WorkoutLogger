namespace WorkoutLogger.Entities
{
    public class Exercise
    {
        public int Id { get; set; }

        public int WorkoutId { get; set; }
        public Workout? Workout { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 1-based position of this exercise within the workout.
        /// </summary>
        public int Order { get; set; }

        public string? Notes { get; set; }

        public List<ExerciseSet> Sets { get; set; } = new();
    }
}
