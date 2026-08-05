using Microsoft.EntityFrameworkCore;
using WorkoutLogger.Entities;

namespace WorkoutLogger.Context
{
    public class WorkoutDbContext : DbContext
    {
        public WorkoutDbContext(DbContextOptions<WorkoutDbContext> options) : base(options)
        {
        }

        public DbSet<Workout> Workouts { get; set; }
        public DbSet<Exercise> Exercises { get; set; }
        public DbSet<ExerciseSet> ExerciseSets { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<RefreshToken> RefreshTokens { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Automatically apply all IEntityTypeConfiguration<T> implementations from this assembly
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(WorkoutDbContext).Assembly);

            base.OnModelCreating(modelBuilder);
        }
    }
}
