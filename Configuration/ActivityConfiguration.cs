using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkoutLogger.Entities;

namespace WorkoutLogger.Configuration
{
    public class ActivityConfiguration : IEntityTypeConfiguration<Activity>
    {
        public void Configure(EntityTypeBuilder<Activity> builder)
        {
            builder.ToTable("Activities");

            builder.HasKey(a => a.Id);

            builder.Property(a => a.PublicId).IsRequired();
            builder.HasIndex(a => a.PublicId).IsUnique();

            builder.Property(a => a.ExternalId).HasMaxLength(200).IsRequired(false);

            // Health Connect ids are unique per source, but two users could in
            // principle import from the same device, so scope the uniqueness.
            // Filtered so the many rows with no ExternalId do not collide.
            builder.HasIndex(a => new { a.UserId, a.ExternalId })
                .IsUnique()
                .HasFilter("\"ExternalId\" IS NOT NULL");

            builder.Property(a => a.Source)
                .HasConversion<string>()   // readable in the database, survives enum reordering
                .HasMaxLength(30)
                .IsRequired();

            builder.Property(a => a.ActivityType).IsRequired().HasMaxLength(60);
            builder.Property(a => a.Title).HasMaxLength(200).IsRequired(false);

            builder.Property(a => a.StartTime).IsRequired();
            builder.Property(a => a.EndTime).IsRequired();

            builder.Property(a => a.CreatedAt).IsRequired();
            builder.Property(a => a.UpdatedAt).IsRequired();
            builder.Property(a => a.DeletedAt).IsRequired(false);

            // Delta sync, same shape as Workouts.
            builder.HasIndex(a => new { a.UserId, a.UpdatedAt });

            // Finding candidates to attach to a workout is a time-window overlap.
            builder.HasIndex(a => new { a.UserId, a.StartTime });

            builder.HasOne(a => a.User)
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // SetNull, not Cascade. Deleting a workout must not destroy the watch
            // recording attached to it - that data came from the device and cannot
            // be recreated. It just becomes unattached again.
            builder.HasOne(a => a.Workout)
                .WithMany(w => w.Activities)
                .HasForeignKey(a => a.WorkoutId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasQueryFilter(a => a.DeletedAt == null);
        }
    }
}
