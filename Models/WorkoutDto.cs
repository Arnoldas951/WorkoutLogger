using System.ComponentModel.DataAnnotations;

namespace WorkoutLogger.Models
{
    public class WorkoutDto
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Workout name is required")]
        [StringLength(200, ErrorMessage = "Name cannot be longer than 200 characters")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Date is required")]
        public DateTime Date { get; set; }

        public TimeSpan? Duration { get; set; }

        [StringLength(1000, ErrorMessage = "Description cannot be longer than 1000 characters")]
        public string Description { get; set; } = string.Empty;

        [Required(ErrorMessage = "At least one exercise is required")]
        [MinLength(1, ErrorMessage = "Workout must contain at least one exercise")]
        public List<ExerciseDto> Exercises { get; set; } = new();

    }
}
