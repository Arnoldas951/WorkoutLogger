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
        public WorkoutService(ILogger<WorkoutService> logger, WorkoutDbContext dbContext) {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task<int> CreateWorkoutAsync(WorkoutDto createWorkoutDto, int userId)
        {
            var workout = createWorkoutDto.ToEntity();
            workout.UserId = userId;

            _dbContext.Workouts.Add(workout);

            await _dbContext.SaveChangesAsync();

            return workout.Id;
        }

        public async Task<bool> DeleteWorkoutAsync(int id, int userId)
        {
            var workoutToDelete = await _dbContext.Workouts.FirstOrDefaultAsync(f => f.Id == id && f.UserId == userId);

            if(workoutToDelete == null)
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

        public async Task<IEnumerable<WorkoutDto>> GetAllWorkoutsAsync(int userId)
        {
            return await _dbContext.Workouts.Include(w => w.Exercises)
                .Where(w => w.UserId == userId)
                .Select(w => w.ToDto())
                .ToListAsync();
        }

        public async Task<WorkoutDto> GetWorkoutByIdAsync(int id, int userId)
        {
            var workout = await _dbContext.Workouts.Include(w => w.Exercises)
                .Where(w => w.Id == id && w.UserId == userId)
                .Select(w => w.ToDto())
                .FirstOrDefaultAsync();

            if(workout == null)
            {
                throw new KeyNotFoundException($"Workout with id {id} not found.");
            }

            return workout;
        }

        public async Task UpdateWorkoutAsync(int id, WorkoutDto updateWorkoutDto, int userId)
        {
            var workout = await _dbContext.Workouts.Include(w => w.Exercises)
                .FirstOrDefaultAsync(w => w.Id == id && w.UserId == userId);

            if(workout == null)
                throw new KeyNotFoundException($"Workout with id {id} not found.");

            workout = workout.UpdateWorkout(updateWorkoutDto);

            await _dbContext.SaveChangesAsync();
        }

        public async Task<List<WorkoutDto>> GetWorkoutsAsync(int userId)
        {
            return await _dbContext.Workouts.Include(w => w.Exercises)
                .Where(w => w.UserId == userId)
                .Select(w => w.ToDto())
                .ToListAsync();
        }
    }
}
