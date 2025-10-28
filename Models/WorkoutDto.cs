namespace WorkoutLogger.Models
{
    public class WorkoutDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public DateTime Date { get; set; }

        public TimeSpan? Duration { get; set; }

        public string Description { get; set; }

        public List<ExerciseDto> Exercises { get; set; } = new();

    }
}
