using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkoutLogger.Entities;

namespace WorkoutLogger.Configuration
{
    public class GarminExerciseSetConfiguration : IEntityTypeConfiguration<GarminExerciseSet>
    {
        public void Configure(EntityTypeBuilder<GarminExerciseSet> builder)
        {
            builder.ToTable("GarminExerciseSets");

            builder.HasKey(s => s.Id);

            // One row per set per activity, and the target of the sidecar's
            // ON CONFLICT - a re-sync updates rather than duplicating.
            builder.HasIndex(s => new { s.GarminActivityDetailId, s.SetIndex }).IsUnique();

            builder.Property(s => s.GarminCategory).HasMaxLength(60).IsRequired(false);
            builder.Property(s => s.StartTime).IsRequired();

            builder.Property(s => s.CreatedAt).IsRequired();
            builder.Property(s => s.UpdatedAt).IsRequired();

            builder.HasOne(s => s.GarminActivityDetail)
                .WithMany(a => a.Sets)
                .HasForeignKey(s => s.GarminActivityDetailId)
                .OnDelete(DeleteBehavior.Cascade);

            // SetNull, consistent with the rest of this schema: deleting or
            // re-logging an exercise must not destroy the watch's recording of
            // it. The set simply becomes unmatched and the next matcher run can
            // place it again.
            builder.HasOne(s => s.ExerciseSet)
                .WithMany()
                .HasForeignKey(s => s.ExerciseSetId)
                .OnDelete(DeleteBehavior.SetNull);

            // Looking up "which Garmin set belongs to this logged set" is the
            // read the training-context view actually performs.
            builder.HasIndex(s => s.ExerciseSetId);
        }
    }
}
