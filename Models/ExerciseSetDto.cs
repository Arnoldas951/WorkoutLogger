using System.ComponentModel.DataAnnotations;

namespace WorkoutLogger.Models
{
    public class ExerciseSetDto
    {
        public int Id { get; set; }

        /// <summary>
        /// Assigned server side from array position, so it is deliberately not validated:
        /// clients POST this as 0 and model validation would reject a perfectly good set.
        /// </summary>
        public int SetNumber { get; set; }

        [Range(0, 1000, ErrorMessage = "Repetitions must be between 0 and 1000")]
        public int Repetitions { get; set; }

        [Range(0, 1000, ErrorMessage = "Weight must be between 0 and 1000 kg")]
        public double Weight { get; set; }

        [Range(0, 10, ErrorMessage = "RPE must be between 0 and 10")]
        public double? Rpe { get; set; }

        public bool IsWarmup { get; set; }

        [StringLength(500, ErrorMessage = "Notes cannot be longer than 500 characters")]
        public string? Notes { get; set; }
    }
}
