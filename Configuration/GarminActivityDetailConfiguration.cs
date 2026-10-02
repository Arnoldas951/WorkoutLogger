using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkoutLogger.Entities;

namespace WorkoutLogger.Configuration
{
    public class GarminActivityDetailConfiguration : IEntityTypeConfiguration<GarminActivityDetail>
    {
        public void Configure(EntityTypeBuilder<GarminActivityDetail> builder)
        {
            builder.ToTable("GarminActivityDetails");

            builder.HasKey(g => g.Id);

            builder.Property(g => g.GarminActivityId).IsRequired();

            // Garmin's id is unique globally, but scope it per user anyway: the
            // same reasoning as Activities.ExternalId, and it is what the
            // sidecar upserts against.
            builder.HasIndex(g => new { g.UserId, g.GarminActivityId }).IsUnique();

            builder.Property(g => g.ActivityType).IsRequired().HasMaxLength(60);
            builder.Property(g => g.Title).HasMaxLength(200).IsRequired(false);

            builder.Property(g => g.StartTime).IsRequired();
            builder.Property(g => g.EndTime).IsRequired();

            // The matcher searches by time window, same as the existing
            // overlapping-candidates lookup on Activities.
            builder.HasIndex(g => new { g.UserId, g.StartTime });

            builder.Property(g => g.RawSummaryJson).HasColumnType("jsonb").IsRequired();
            builder.Property(g => g.RawSetsJson).HasColumnType("jsonb").IsRequired(false);

            builder.Property(g => g.CreatedAt).IsRequired();
            builder.Property(g => g.UpdatedAt).IsRequired();

            builder.HasOne(g => g.User)
                .WithMany()
                .HasForeignKey(g => g.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // SetNull for the same reason Activity.Workout is SetNull: deleting
            // the Health Connect activity must not destroy the Garmin recording
            // attached to it. It just becomes unmatched again, and the next sync
            // will leave it that way.
            builder.HasOne(g => g.Activity)
                .WithMany()
                .HasForeignKey(g => g.ActivityId)
                .OnDelete(DeleteBehavior.SetNull);
        }
    }
}
