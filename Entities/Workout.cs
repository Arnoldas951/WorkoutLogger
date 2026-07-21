namespace WorkoutLogger.Entities
{
    public class Workout
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public TimeSpan? Duration { get; set; }

        public DateTime Date { get; set; }

        public string Description { get; set; } = string.Empty;

        public int UserId { get; set; }
        public User? User { get; set; }

        public List<Exercise> Exercises { get; set; } = new();
    }
}
