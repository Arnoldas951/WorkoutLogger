using WorkoutLogger.Models;

namespace WorkoutLogger.Services.Abstraction
{
    public interface IActivityService
    {
        Task<List<ActivityDto>> GetActivitiesAsync(int userId);

        Task<ActivityDto> GetActivityByIdAsync(int id, int userId);

        /// <summary>
        /// Imports a watch session. Idempotent on ExternalId, so re-reading the
        /// same Health Connect record updates rather than duplicating.
        /// </summary>
        Task<ActivityDto> ImportAsync(ActivityDto dto, int userId);

        /// <summary>
        /// Activities overlapping a time window and not already attached to
        /// something. Used to suggest "your watch recorded this, attach it?".
        /// </summary>
        Task<List<ActivityDto>> GetOverlappingAsync(int userId, DateTime start, DateTime end);

        /// <summary>False when either side is missing or belongs to someone else.</summary>
        Task<bool> LinkToWorkoutAsync(int activityId, int workoutId, int userId);

        Task<bool> UnlinkAsync(int activityId, int userId);

        Task<bool> DeleteAsync(int id, int userId);

        /// <summary>
        /// Activities created, updated or deleted since <paramref name="since"/>,
        /// including tombstones. Null for a full sync.
        /// </summary>
        Task<EntityChangesDto<ActivityDto>> GetChangesAsync(int userId, DateTime? since);
    }
}
