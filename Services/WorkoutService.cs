using Microsoft.EntityFrameworkCore;
using WorkoutLogger.Context;
using WorkoutLogger.Entities;
using WorkoutLogger.Mappers;
using WorkoutLogger.Models;
using WorkoutLogger.Services.Abstraction;

namespace WorkoutLogger.Services
{
    public class WorkoutService : IWorkoutService
    {
        private readonly ILogger<IWorkoutService> _logger;
        private readonly WorkoutDbContext _dbContext;

        public WorkoutService(ILogger<WorkoutService> logger, WorkoutDbContext dbContext)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        /// <summary>
        /// Read query with the full Workout -> Exercise -> ExerciseSet graph loaded.
        /// Untracked, because every caller projects straight to a DTO. Soft-deleted
        /// workouts are excluded by the global query filter.
        /// </summary>
        private IQueryable<Workout> WorkoutGraph(int userId) =>
            _dbContext.Workouts
                .AsNoTracking()
                .Include(w => w.Exercises)
                    .ThenInclude(e => e.Sets)
                .Where(w => w.UserId == userId);

        public async Task<int> CreateWorkoutAsync(WorkoutDto createWorkoutDto, int userId)
        {
            // A sync queue retries on failure, and a response lost on a flaky gym
            // connection is indistinguishable from a request that never arrived.
            // Keying on PublicId makes the retry land on the same row instead of
            // producing a duplicate workout.
            if (createWorkoutDto.PublicId != Guid.Empty)
            {
                var existing = await _dbContext.Workouts
                    .IgnoreQueryFilters() // a re-created workout may be sitting under a tombstone
                    .Include(w => w.Exercises)
                        .ThenInclude(e => e.Sets)
                    .FirstOrDefaultAsync(w => w.PublicId == createWorkoutDto.PublicId && w.UserId == userId);

                if (existing != null)
                {
                    existing.UpdateWorkout(createWorkoutDto);
                    existing.DeletedAt = null; // posting it again un-deletes it
                    existing.UpdatedAt = DateTime.UtcNow;

                    await _dbContext.SaveChangesAsync();
                    return existing.Id;
                }
            }

            var workout = createWorkoutDto.ToEntity();
            workout.UserId = userId;

            var now = DateTime.UtcNow;
            workout.CreatedAt = now;
            workout.UpdatedAt = now;

            _dbContext.Workouts.Add(workout);

            await _dbContext.SaveChangesAsync();

            return workout.Id;
        }

        public async Task<bool> DeleteWorkoutAsync(int id, int userId)
        {
            var workoutToDelete = await _dbContext.Workouts
                .FirstOrDefaultAsync(f => f.Id == id && f.UserId == userId);

            if (workoutToDelete == null)
            {
                return false;
            }

            var now = DateTime.UtcNow;

            // Tombstone rather than remove. A row that simply vanishes is invisible
            // to other devices, which would treat the workout as one they had not
            // synced yet and push it straight back.
            workoutToDelete.DeletedAt = now;
            workoutToDelete.UpdatedAt = now;

            // Release any attached watch recordings rather than deleting them.
            // That data came off the device and cannot be recreated, so it
            // outlives the sets someone typed alongside it and becomes available
            // to attach to something else.
            //
            // The FK is configured OnDelete(SetNull), but that only fires on a
            // hard delete - with a tombstone the link would otherwise survive and
            // the activity would stay invisible to overlap suggestions forever.
            var attached = await _dbContext.Activities
                .Where(a => a.WorkoutId == workoutToDelete.Id)
                .ToListAsync();

            foreach (var activity in attached)
            {
                activity.WorkoutId = null;
                activity.UpdatedAt = now;
            }

            await _dbContext.SaveChangesAsync();

            return true;
        }

        public async Task<WorkoutDto> GetWorkoutByIdAsync(int id, int userId)
        {
            // ToDto is a plain C# method, so materialise first and map in memory.
            // Projecting inside the IQueryable would fail translation at runtime.
            var workout = await WorkoutGraph(userId)
                .FirstOrDefaultAsync(w => w.Id == id);

            if (workout == null)
            {
                throw new KeyNotFoundException($"Workout with id {id} not found.");
            }

            return workout.ToDto();
        }

        public async Task<List<WorkoutDto>> GetWorkoutsAsync(int userId)
        {
            var workouts = await WorkoutGraph(userId)
                .OrderByDescending(w => w.Date)
                .ToListAsync();

            return workouts.Select(w => w.ToDto()).ToList();
        }

        public async Task UpdateWorkoutAsync(int id, WorkoutDto updateWorkoutDto, int userId)
        {
            // Tracked read here: the change tracker has to see the existing sets
            // so removals from the collection turn into DELETEs.
            var workout = await _dbContext.Workouts
                .Include(w => w.Exercises)
                    .ThenInclude(e => e.Sets)
                .FirstOrDefaultAsync(w => w.Id == id && w.UserId == userId);

            if (workout == null)
                throw new KeyNotFoundException($"Workout with id {id} not found.");

            workout.UpdateWorkout(updateWorkoutDto);
            workout.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();
        }

        public async Task<WorkoutChangesDto> GetChangesAsync(int userId, DateTime? since)
        {
            // Captured before reading. Anything written between the read and the
            // response would otherwise fall in the gap and never be synced; taking
            // the watermark first means at worst a row is sent twice, which is
            // harmless because applying a change is idempotent.
            var syncedAt = DateTime.UtcNow;

            var query = _dbContext.Workouts
                .AsNoTracking()
                .IgnoreQueryFilters() // tombstones are the entire point of this endpoint
                .Include(w => w.Exercises)
                    .ThenInclude(e => e.Sets)
                .Where(w => w.UserId == userId);

            if (since.HasValue)
            {
                var cutoff = DateTime.SpecifyKind(since.Value.ToUniversalTime(), DateTimeKind.Utc);
                query = query.Where(w => w.UpdatedAt > cutoff);
            }

            var changed = await query.ToListAsync();

            return new WorkoutChangesDto
            {
                Changed = changed
                    .Where(w => w.DeletedAt == null)
                    .OrderBy(w => w.UpdatedAt)
                    .Select(w => w.ToDto())
                    .ToList(),
                Deleted = changed
                    .Where(w => w.DeletedAt != null)
                    .OrderBy(w => w.UpdatedAt)
                    .Select(w => w.PublicId)
                    .ToList(),
                SyncedAt = syncedAt
            };
        }
    }
}
