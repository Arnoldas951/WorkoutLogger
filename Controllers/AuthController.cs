using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WorkoutLogger.Models;
using WorkoutLogger.Services.Abstraction;

namespace WorkoutLogger.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(IAuthService authService, ILogger<AuthController> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        [HttpPost("register")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<ActionResult> Register([FromBody] RegisterRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest("username and password are required");
            }

            var created = await _authService.RegisterAsync(request.Username, request.Password);

            if (!created)
            {
                return Conflict("username already taken");
            }

            return Ok();
        }

        [HttpPost("login")]
        [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest("username and password are required");
            }

            var response = await _authService.LoginAsync(request.Username, request.Password);

            if (response == null)
            {
                return Unauthorized();
            }

            return Ok(response);
        }

        /// <summary>
        /// Exchanges a refresh token for a new pair. The old token stops working
        /// immediately, so clients must store whatever comes back.
        /// </summary>
        [HttpPost("refresh")]
        [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<AuthResponse>> Refresh([FromBody] RefreshRequest request)
        {
            var response = await _authService.RefreshAsync(request.RefreshToken);

            if (response == null)
            {
                // Deliberately vague: unknown, expired and replayed all look the same
                // from outside, so probing tells an attacker nothing.
                return Unauthorized();
            }

            return Ok(response);
        }

        /// <summary>
        /// Revokes a single refresh token. The access token stays valid until it
        /// expires on its own; that is the tradeoff for not tracking every JWT.
        /// </summary>
        [HttpPost("logout")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<ActionResult> Logout([FromBody] RefreshRequest request)
        {
            await _authService.RevokeAsync(request.RefreshToken);

            // Always 204: whether the token was still active is not the caller's
            // business, and the client wants to clear local state either way.
            return NoContent();
        }
    }
}
