using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.Threading.Tasks;
using WorkoutLogger.Models;
using WorkoutLogger.Services.Abstraction;

namespace WorkoutLogger.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class WorkoutController : ControllerBase
    {
        private readonly ILogger<WorkoutController> _logger;
        private readonly IWorkoutService _workoutService;
        public WorkoutController(ILogger<WorkoutController> logger, IWorkoutService workoutService)
        {
            _logger = logger;
            _workoutService = workoutService;
        }

        private int GetUserId()
        {
            return int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        }

        /// <summary>
        /// Everything created, updated or deleted since the given watermark.
        /// Omit <paramref name="since"/> for a full sync.
        ///
        /// Declared before Get(int) so "changes" is not swallowed by the {id} route.
        /// </summary>
        [HttpGet("changes")]
        [ProducesResponseType(typeof(WorkoutChangesDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<WorkoutChangesDto>> GetChanges([FromQuery] DateTime? since)
        {
            var changes = await _workoutService.GetChangesAsync(GetUserId(), since);
            return Ok(changes);
        }

        [HttpGet("{id:int}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> Get(int id)
        {
            try
            {
                var workout = await _workoutService.GetWorkoutByIdAsync(id, GetUserId());
                return Ok(workout);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }

        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult> GetWorkouts()
        {
            var workouts = await _workoutService.GetWorkoutsAsync(GetUserId());
            return Ok(workouts);
        }

        [HttpPost]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult> Create(WorkoutDto dto)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage);

                _logger.LogWarning("Validation failed: {Errors}", string.Join(", ", errors));
                return BadRequest(new { Errors = errors });
            }

            var id = await _workoutService.CreateWorkoutAsync(dto, GetUserId());

            return CreatedAtAction(nameof(Get), new { id }, null);
        }


        [HttpDelete("{id:int}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> Delete(int id)
        {
            var deleted = await _workoutService.DeleteWorkoutAsync(id, GetUserId());

            if (!deleted)
                return NotFound();

            return NoContent();
        }

        [HttpPut("{id:int}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> UpdateWorkout(int id, WorkoutDto workout)
        {
            try
            {
                await _workoutService.UpdateWorkoutAsync(id, workout, GetUserId());
                return NoContent();
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }
    }
}