using System.ComponentModel.DataAnnotations;

namespace WorkoutLogger.Models
{
    public class ExerciseDto
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Exercise name is required")]
        [StringLength(50, ErrorMessage = "Name cannot be longer than 50 characters")]
        public string Name { get; set; } = string.Empty;

        public int Order { get; set; }

        public int WorkoutId { get; set; }

        [StringLength(500, ErrorMessage = "Notes cannot be longer than 500 characters")]
        public string? Notes { get; set; }

        [MinLength(1, ErrorMessage = "Exercise must contain at least one set")]
        public List<ExerciseSetDto> Sets { get; set; } = new();

        /// <summary>
        /// Total load moved for this exercise (reps x weight, working sets only).
        /// Computed server side so every client agrees on the number.
        /// </summary>
        public double TotalVolume => Sets.Where(s => !s.IsWarmup).Sum(s => s.Repetitions * s.Weight);
    }
}
