using WorkoutLogger.Models;

namespace WorkoutLogger.Services.Abstraction
{
    public interface IWorkoutService
    {
        Task<IEnumerable<WorkoutDto>> GetAllWorkoutsAsync(int userId);
        Task<WorkoutDto> GetWorkoutByIdAsync(int id, int userId);
        Task<int> CreateWorkoutAsync(WorkoutDto workoutDto, int userId);
        Task UpdateWorkoutAsync(int id, WorkoutDto updateWorkoutDto, int userId);
        Task<bool> DeleteWorkoutAsync(int id, int userId);
    }
}
