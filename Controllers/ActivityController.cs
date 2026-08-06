using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using WorkoutLogger.Models;
using WorkoutLogger.Services.Abstraction;

namespace WorkoutLogger.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ActivityController : ControllerBase
    {
        private readonly IActivityService _activityService;
        private readonly ILogger<ActivityController> _logger;

        public ActivityController(ILogger<ActivityController> logger, IActivityService activityService)
        {
            _logger = logger;
            _activityService = activityService;
        }

        private int GetUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        [HttpGet]
        [ProducesResponseType(typeof(List<ActivityDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<List<ActivityDto>>> GetActivities()
        {
            return Ok(await _activityService.GetActivitiesAsync(GetUserId()));
        }

        /// <summary>
        /// Unattached activities overlapping a time window. Declared before the
        /// {id} route so "overlapping" is not read as an id.
        /// </summary>
        [HttpGet("overlapping")]
        [ProducesResponseType(typeof(List<ActivityDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<List<ActivityDto>>> GetOverlapping(
            [FromQuery] DateTime start,
            [FromQuery] DateTime end)
        {
            if (end <= start)
            {
                return BadRequest("end must be after start");
            }

            return Ok(await _activityService.GetOverlappingAsync(GetUserId(), start, end));
        }

        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(ActivityDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ActivityDto>> Get(int id)
        {
            try
            {
                return Ok(await _activityService.GetActivityByIdAsync(id, GetUserId()));
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }

        /// <summary>
        /// Imports a watch session. Safe to call repeatedly with the same
        /// ExternalId - it updates rather than duplicating.
        /// </summary>
        [HttpPost("import")]
        [ProducesResponseType(typeof(ActivityDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<ActivityDto>> Import([FromBody] ActivityDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (dto.EndTime <= dto.StartTime)
            {
                return BadRequest("endTime must be after startTime");
            }

            return Ok(await _activityService.ImportAsync(dto, GetUserId()));
        }

        [HttpPost("{id:int}/link/{workoutId:int}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> Link(int id, int workoutId)
        {
            var linked = await _activityService.LinkToWorkoutAsync(id, workoutId, GetUserId());
            return linked ? NoContent() : NotFound();
        }

        [HttpDelete("{id:int}/link")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> Unlink(int id)
        {
            var unlinked = await _activityService.UnlinkAsync(id, GetUserId());
            return unlinked ? NoContent() : NotFound();
        }

        [HttpDelete("{id:int}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> Delete(int id)
        {
            var deleted = await _activityService.DeleteAsync(id, GetUserId());
            return deleted ? NoContent() : NotFound();
        }
    }
}
