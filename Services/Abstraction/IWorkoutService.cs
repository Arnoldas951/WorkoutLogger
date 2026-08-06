using WorkoutLogger.Models;

namespace WorkoutLogger.Services.Abstraction
{
    public interface IWorkoutService
    {
        Task<WorkoutDto> GetWorkoutByIdAsync(int id, int userId);
        Task<List<WorkoutDto>> GetWorkoutsAsync(int userId);

        /// <summary>
        /// Creates a workout, or updates the existing one when its PublicId is
        /// already known. Idempotent by PublicId so a sync queue can retry safely.
        /// </summary>
        Task<int> CreateWorkoutAsync(WorkoutDto workoutDto, int userId);

        Task UpdateWorkoutAsync(int id, WorkoutDto updateWorkoutDto, int userId);

        /// <summary>Records a tombstone rather than removing the row.</summary>
        Task<bool> DeleteWorkoutAsync(int id, int userId);

        /// <summary>
        /// Everything created, updated or deleted since <paramref name="since"/>.
        /// Pass null for a full sync.
        /// </summary>
        Task<WorkoutChangesDto> GetChangesAsync(int userId, DateTime? since);
    }
}
