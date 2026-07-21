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

        [HttpGet("{id}")]
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

        [HttpPost]
        [ProducesResponseType(StatusCodes.Status200OK)]
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

            await _workoutService.CreateWorkoutAsync(dto, GetUserId());

            return Ok();
        }


        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> Delete(int id)
        {
            await _workoutService.DeleteWorkoutAsync(id, GetUserId());
            return Ok();
        }
    }
}