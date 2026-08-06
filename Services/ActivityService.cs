using Microsoft.EntityFrameworkCore;
using WorkoutLogger.Context;
using WorkoutLogger.Entities;
using WorkoutLogger.Mappers;
using WorkoutLogger.Models;
using WorkoutLogger.Services.Abstraction;

namespace WorkoutLogger.Services
{
    public class ActivityService : IActivityService
    {
        private readonly WorkoutDbContext _dbContext;
        private readonly ILogger<ActivityService> _logger;

        public ActivityService(ILogger<ActivityService> logger, WorkoutDbContext dbContext)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        private static DateTime EnsureUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        public async Task<List<ActivityDto>> GetActivitiesAsync(int userId)
        {
            var activities = await _dbContext.Activities
                .AsNoTracking()
                .Where(a => a.UserId == userId)
                .OrderByDescending(a => a.StartTime)
                .ToListAsync();

            return activities.Select(a => a.ToDto()).ToList();
        }

        public async Task<ActivityDto> GetActivityByIdAsync(int id, int userId)
        {
            var activity = await _dbContext.Activities
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);

            if (activity == null)
            {
                throw new KeyNotFoundException($"Activity with id {id} not found.");
            }

            return activity.ToDto();
        }

        public async Task<ActivityDto> ImportAsync(ActivityDto dto, int userId)
        {
            var now = DateTime.UtcNow;

            // Health Connect gets re-read on every sync and returns the same
            // records again. Without keying on ExternalId, every sync would add
            // another copy of last Tuesday's run.
            if (!string.IsNullOrWhiteSpace(dto.ExternalId))
            {
                var existing = await _dbContext.Activities
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(a => a.UserId == userId && a.ExternalId == dto.ExternalId);

                if (existing != null)
                {
                    existing.UpdateFrom(dto);
                    existing.DeletedAt = null;
                    existing.UpdatedAt = now;

                    await _dbContext.SaveChangesAsync();
                    return existing.ToDto();
                }
            }

            var activity = dto.ToEntity();
            activity.UserId = userId;
            activity.CreatedAt = now;
            activity.UpdatedAt = now;

            _dbContext.Activities.Add(activity);
            await _dbContext.SaveChangesAsync();

            return activity.ToDto();
        }

        public async Task<List<ActivityDto>> GetOverlappingAsync(int userId, DateTime start, DateTime end)
        {
            var from = EnsureUtc(start);
            var to = EnsureUtc(end);

            // Standard interval overlap: the two ranges intersect unless one ends
            // before the other begins. Deliberately not "contained within" - a
            // watch session rarely lines up exactly with when you started tapping
            // sets into the phone.
            var activities = await _dbContext.Activities
                .AsNoTracking()
                .Where(a => a.UserId == userId
                            && a.WorkoutId == null
                            && a.StartTime < to
                            && a.EndTime > from)
                .OrderBy(a => a.StartTime)
                .ToListAsync();

            return activities.Select(a => a.ToDto()).ToList();
        }

        public async Task<bool> LinkToWorkoutAsync(int activityId, int workoutId, int userId)
        {
            var activity = await _dbContext.Activities
                .FirstOrDefaultAsync(a => a.Id == activityId && a.UserId == userId);

            if (activity == null)
            {
                return false;
            }

            // Checked separately so a user cannot attach their activity to someone
            // else's workout by guessing an id.
            var workoutExists = await _dbContext.Workouts
                .AnyAsync(w => w.Id == workoutId && w.UserId == userId);

            if (!workoutExists)
            {
                return false;
            }

            activity.WorkoutId = workoutId;
            activity.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();
            return true;
        }

        public async Task<bool> UnlinkAsync(int activityId, int userId)
        {
            var activity = await _dbContext.Activities
                .FirstOrDefaultAsync(a => a.Id == activityId && a.UserId == userId);

            if (activity == null || activity.WorkoutId == null)
            {
                return false;
            }

            activity.WorkoutId = null;
            activity.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();
            return true;
        }

        public async Task<EntityChangesDto<ActivityDto>> GetChangesAsync(int userId, DateTime? since)
        {
            var query = _dbContext.Activities
                .AsNoTracking()
                .IgnoreQueryFilters() // tombstones are the point
                .Where(a => a.UserId == userId);

            if (since.HasValue)
            {
                var cutoff = EnsureUtc(since.Value);
                query = query.Where(a => a.UpdatedAt > cutoff);
            }

            var changed = await query.ToListAsync();

            return new EntityChangesDto<ActivityDto>
            {
                Changed = changed
                    .Where(a => a.DeletedAt == null)
                    .OrderBy(a => a.UpdatedAt)
                    .Select(a => a.ToDto())
                    .ToList(),
                Deleted = changed
                    .Where(a => a.DeletedAt != null)
                    .OrderBy(a => a.UpdatedAt)
                    .Select(a => a.PublicId)
                    .ToList()
            };
        }

        public async Task<bool> DeleteAsync(int id, int userId)
        {
            var activity = await _dbContext.Activities
                .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);

            if (activity == null)
            {
                return false;
            }

            // Tombstone, same reasoning as workouts: a hard delete cannot be told
            // to other devices, so they would re-import it from Health Connect on
            // their next sync and it would come back.
            activity.DeletedAt = DateTime.UtcNow;
            activity.UpdatedAt = activity.DeletedAt.Value;

            await _dbContext.SaveChangesAsync();
            return true;
        }
    }
}
