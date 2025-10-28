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

        public async Task<int> CreateWorkoutAsync(WorkoutDto createWorkoutDto)
        {
            var workout = createWorkoutDto.ToEntity();

            _dbContext.Workouts.Add(workout);

            await _dbContext.SaveChangesAsync();

            return workout.Id;
        }

        public async Task<bool> DeleteWorkoutAsync(int id)
        {
            var workoutToDelete = await _dbContext.Workouts.FirstOrDefaultAsync(f => f.Id == id);

            if(workoutToDelete == null)
            {
                return false;
            }

            _dbContext.Workouts.Remove(workoutToDelete);

            await _dbContext.SaveChangesAsync();

            return true;
        }

        public async Task<IEnumerable<WorkoutDto>> GetAllWorkoutsAsync()
        {
            return await _dbContext.Workouts.Include(w => w.Exercises)
                .Select(w => w.ToDto())
                .ToListAsync();
        }

        public async Task<WorkoutDto> GetWorkoutByIdAsync(int id)
        {
            var workout = await _dbContext.Workouts.Include(w => w.Exercises)
                .Where(w => w.Id == id)
                .Select(w => w.ToDto())
                .FirstOrDefaultAsync();

            if(workout == null)
            {
                throw new KeyNotFoundException($"Workout with id {id} not found.");
            }

            return workout;
        }

        public async Task UpdateWorkoutAsync(int id, WorkoutDto updateWorkoutDto)
        {
            var workout = await _dbContext.Workouts.Include(w => w.Exercises)
                .FirstOrDefaultAsync(w => w.Id == id);

            if(workout == null)
                throw new KeyNotFoundException($"Workout with id {id} not found.");

            workout = workout.UpdateWorkout(updateWorkoutDto);

            await _dbContext.SaveChangesAsync();
        }
    }
}
