using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkoutLogger.Entities;

namespace WorkoutLogger.Configuration
{
    public class ExerciseSetConfiguration : IEntityTypeConfiguration<ExerciseSet>
    {
        public void Configure(EntityTypeBuilder<ExerciseSet> builder)
        {
            builder.ToTable("ExerciseSets");

            builder.HasKey(s => s.Id);

            builder.Property(s => s.SetNumber)
                .IsRequired();

            builder.Property(s => s.Repetitions)
                .IsRequired();

            builder.Property(s => s.Weight)
                .HasDefaultValue(0d);

            builder.Property(s => s.Rpe)
                .IsRequired(false);

            builder.Property(s => s.IsWarmup)
                .HasDefaultValue(false);

            builder.Property(s => s.Notes)
                .HasMaxLength(500)
                .IsRequired(false);

            builder.HasOne(s => s.Exercise)
                .WithMany(e => e.Sets)
                .HasForeignKey(s => s.ExerciseId)
                .OnDelete(DeleteBehavior.Cascade);

            // Serves the ordered read path (sets are always fetched ordered by SetNumber).
            // Deliberately not unique: reordering sets in a single SaveChanges would
            // otherwise risk a transient violation between the individual UPDATE statements.
            builder.HasIndex(s => new { s.ExerciseId, s.SetNumber });
        }
    }
}
