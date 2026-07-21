using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkoutLogger.Entities;

namespace WorkoutLogger.Configuration
{
    public class WorkoutConfiguration : IEntityTypeConfiguration<Workout>
    {
        public void Configure(EntityTypeBuilder<Workout> builder)
        {
            builder.ToTable("Workouts");

            builder.HasKey(w => w.Id);

            builder.Property(w => w.Name)
                .IsRequired()
                .HasMaxLength(200);

            builder.Property(w => w.Description)
                .HasMaxLength(1000)
                .IsRequired(false);

            builder.Property(w => w.Date)
                .IsRequired();

            // for PostgreSQL, TimeSpan maps to 'interval'
            builder.Property(w => w.Duration)
                .HasColumnType("interval")
                .IsRequired(false);

            builder.HasOne(w => w.User)
                .WithMany()
                .HasForeignKey(w => w.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
