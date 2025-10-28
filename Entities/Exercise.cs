using System.ComponentModel.DataAnnotations;

namespace WorkoutLogger.Entities
{
    public class Exercise
    {
        public int Id { get; set; }

        public int WorkoutId { get; set; }
        public Workout? Workout { get; set; }

        public string Name { get; set; } = string.Empty;

        public int Sets { get; set; }

        public int Repetitions { get; set; }

        public double Weight { get; set; } // in kilograms

        public string Notes { get; set; }
    }
}
