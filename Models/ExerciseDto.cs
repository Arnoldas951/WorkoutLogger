namespace WorkoutLogger.Models
{
    public class ExerciseDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Repetitions { get; set; }
        public int Sets { get; set; }

        public int WorkoutId { get; set; }

        public double Weight { get; set; } // in kilograms

        public string Notes { get; set; } = string.Empty;
    }
}
