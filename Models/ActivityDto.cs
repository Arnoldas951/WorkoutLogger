using System.ComponentModel.DataAnnotations;

namespace WorkoutLogger.Models
{
    public class ActivityDto
    {
        public int Id { get; set; }

        public Guid PublicId { get; set; }

        /// <summary>Health Connect record id, used to make re-import idempotent.</summary>
        [StringLength(200)]
        public string? ExternalId { get; set; }

        [StringLength(30)]
        public string Source { get; set; } = "HealthConnect";

        [Required(ErrorMessage = "Activity type is required")]
        [StringLength(60)]
        public string ActivityType { get; set; } = string.Empty;

        [StringLength(200)]
        public string? Title { get; set; }

        [Required]
        public DateTime StartTime { get; set; }

        [Required]
        public DateTime EndTime { get; set; }

        [Range(20, 250, ErrorMessage = "Average heart rate looks out of range")]
        public int? AverageHeartRate { get; set; }

        [Range(20, 250, ErrorMessage = "Max heart rate looks out of range")]
        public int? MaxHeartRate { get; set; }

        [Range(20, 250, ErrorMessage = "Min heart rate looks out of range")]
        public int? MinHeartRate { get; set; }

        public double? ActiveCalories { get; set; }
        public double? TotalCalories { get; set; }
        public double? DistanceMeters { get; set; }
        public int? Steps { get; set; }

        /// <summary>Null when this activity is not attached to a workout.</summary>
        public int? WorkoutId { get; set; }

        public DateTime UpdatedAt { get; set; }

        /// <summary>Convenience for clients; derived from the timestamps.</summary>
        public double DurationMinutes => (EndTime - StartTime).TotalMinutes;
    }
}
