using WorkoutLogger.Models;

namespace WorkoutLogger.Services.Abstraction
{
    public interface IWorkoutService
    {
        Task<IEnumerable<WorkoutDto>> GetAllWorkoutsAsync();
        Task<WorkoutDto> GetWorkoutByIdAsync(int id);
        Task<int> CreateWorkoutAsync(WorkoutDto workoutDto);
        Task UpdateWorkoutAsync(int id, WorkoutDto updateWorkoutDto);
        Task<bool> DeleteWorkoutAsync(int id);
    }
}
