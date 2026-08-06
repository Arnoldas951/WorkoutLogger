using System.ComponentModel.DataAnnotations;

namespace WorkoutLogger.Models
{
    public class WorkoutDto
    {
        public int Id { get; set; }

        /// <summary>
        /// Sync identity. Clients generate this when creating a workout offline;
        /// posting the same PublicId twice updates rather than duplicating, which
        /// is what makes a retrying sync queue safe.
        /// Left empty on create, the server generates one.
        /// </summary>
        public Guid PublicId { get; set; }

        /// <summary>
        /// Server-stamped, read only. Clients pass the newest value they have back
        /// as the <c>since</c> watermark on the next delta sync.
        /// </summary>
        public DateTime UpdatedAt { get; set; }

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

        /// <summary>
        /// Total load moved across the whole workout, working sets only.
        /// </summary>
        public double TotalVolume => Exercises.Sum(e => e.TotalVolume);

        /// <summary>
        /// Number of working sets across the whole workout.
        /// </summary>
        public int TotalSets => Exercises.Sum(e => e.Sets.Count(s => !s.IsWarmup));
    }
}
