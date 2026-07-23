using System.ComponentModel.DataAnnotations;

namespace WorkoutLogger.Models
{
    public class ExerciseDto
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Exercise name is required")]
        [StringLength(50, ErrorMessage = "Name cannot be longer than 50 characters")]
        public string Name { get; set; } = string.Empty;

        [Range(1, 1000, ErrorMessage = "Repetitions must be between 1 and 1000")]
        public int Repetitions { get; set; }

        [Range(1, 100, ErrorMessage = "Sets must be between 1 and 100")]
        public int Sets { get; set; }

        public int WorkoutId { get; set; }

        public double Weight { get; set; } // in kilograms

        [StringLength(500, ErrorMessage = "Notes cannot be longer than 500 characters")]
        public string? Notes { get; set; } = string.Empty;
    }
}
