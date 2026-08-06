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

            builder.Property(w => w.PublicId)
                .IsRequired();

            // Clients look workouts up by this and retried syncs depend on the
            // collision being caught, so it is unique rather than just indexed.
            builder.HasIndex(w => w.PublicId)
                .IsUnique();

            builder.Property(w => w.Name)
                .IsRequired()
                .HasMaxLength(200);

            builder.Property(w => w.Description)
                .HasMaxLength(1000)
                .IsRequired(false);

            builder.Property(w => w.Date)
                .IsRequired();

            builder.Property(w => w.CreatedAt).IsRequired();
            builder.Property(w => w.UpdatedAt).IsRequired();
            builder.Property(w => w.DeletedAt).IsRequired(false);

            // Delta sync asks "everything for this user changed since T", including
            // tombstones, which is exactly this index.
            builder.HasIndex(w => new { w.UserId, w.UpdatedAt });

            // for PostgreSQL, TimeSpan maps to 'interval'
            builder.Property(w => w.Duration)
                .HasColumnType("interval")
                .IsRequired(false);

            builder.HasOne(w => w.User)
                .WithMany()
                .HasForeignKey(w => w.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Soft-deleted workouts are invisible to ordinary queries, so no read
            // path can forget to filter them. The sync endpoint deliberately opts
            // back in with IgnoreQueryFilters, because propagating a delete is the
            // one case that needs to see tombstones.
            builder.HasQueryFilter(w => w.DeletedAt == null);
        }
    }
}
