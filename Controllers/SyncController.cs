using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using WorkoutLogger.Models;
using WorkoutLogger.Services.Abstraction;

namespace WorkoutLogger.Controllers
{
    /// <summary>
    /// One endpoint for a client to catch up on everything.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class SyncController : ControllerBase
    {
        private readonly IWorkoutService _workoutService;
        private readonly IActivityService _activityService;
        private readonly ILogger<SyncController> _logger;

        public SyncController(
            ILogger<SyncController> logger,
            IWorkoutService workoutService,
            IActivityService activityService)
        {
            _logger = logger;
            _workoutService = workoutService;
            _activityService = activityService;
        }

        private int GetUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        /// <summary>
        /// Everything changed since the watermark. Omit <paramref name="since"/>
        /// for a full sync.
        /// </summary>
        [HttpGet("changes")]
        [ProducesResponseType(typeof(SyncChangesDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<SyncChangesDto>> GetChanges([FromQuery] DateTime? since)
        {
            var userId = GetUserId();

            // Captured before either read, and used for both. Anything written
            // while this request is in flight then lands in the next delta rather
            // than falling in the gap — applying a change twice is harmless,
            // missing one is silent data loss.
            var syncedAt = DateTime.UtcNow;

            var workouts = await _workoutService.GetChangesAsync(userId, since);
            var activities = await _activityService.GetChangesAsync(userId, since);

            return Ok(new SyncChangesDto
            {
                // The workout service returns its own SyncedAt, deliberately
                // discarded here so both entity types share one watermark. A
                // client that stored two would eventually skew them.
                Workouts = new EntityChangesDto<WorkoutDto>
                {
                    Changed = workouts.Changed,
                    Deleted = workouts.Deleted,
                },
                Activities = activities,
                SyncedAt = syncedAt,
            });
        }
    }
}
