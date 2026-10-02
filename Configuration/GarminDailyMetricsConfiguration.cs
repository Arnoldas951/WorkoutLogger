using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkoutLogger.Entities;

namespace WorkoutLogger.Configuration
{
    public class GarminDailyMetricsConfiguration : IEntityTypeConfiguration<GarminDailyMetrics>
    {
        public void Configure(EntityTypeBuilder<GarminDailyMetrics> builder)
        {
            builder.ToTable("GarminDailyMetrics");

            builder.HasKey(g => g.Id);

            builder.Property(g => g.Date).IsRequired();

            // One row per user per day. This is also what the sidecar's
            // ON CONFLICT clause targets, so it is load-bearing rather than
            // merely defensive - re-running a sync for a date has to update.
            builder.HasIndex(g => new { g.UserId, g.Date }).IsUnique();

            builder.Property(g => g.HrvStatus).HasMaxLength(40).IsRequired(false);
            builder.Property(g => g.TrainingStatus).HasMaxLength(40).IsRequired(false);

            // jsonb rather than text: queryable with -> if a field ever needs
            // reading before someone gets around to promoting it to a column.
            builder.Property(g => g.RawJson).HasColumnType("jsonb").IsRequired();

            builder.Property(g => g.CreatedAt).IsRequired();
            builder.Property(g => g.UpdatedAt).IsRequired();

            builder.HasOne(g => g.User)
                .WithMany()
                .HasForeignKey(g => g.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
