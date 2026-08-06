using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using WorkoutLogger.Context;
using WorkoutLogger.Services;
using WorkoutLogger.Services.Abstraction;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddDbContext<WorkoutDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// register application services
builder.Services.AddScoped<IWorkoutService, WorkoutService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddSingleton<ITokenService, TokenService>();

// ---------------------------------------------------------------------------
// JWT authentication
//
// Fail fast when the signing key is missing. Previously this only logged a
// warning and skipped registering authentication entirely, which meant a
// misconfigured deploy would start up and serve [Authorize] endpoints without
// any authentication wired in. An API that silently stops checking credentials
// is worse than one that refuses to boot.
// ---------------------------------------------------------------------------
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSection["Key"];

if (string.IsNullOrWhiteSpace(jwtKey))
{
    throw new InvalidOperationException(
        "Jwt:Key is not configured. Set it with 'dotnet user-secrets set \"Jwt:Key\" \"<value>\"' " +
        "for local development, or the Jwt__Key environment variable when deployed. " +
        "See SECURITY.md.");
}

// HMAC-SHA256 needs at least 256 bits of key material; a shorter key throws
// deep inside the token handler at first login rather than here.
if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException(
        "Jwt:Key must be at least 32 bytes for HMAC-SHA256. Generate one with: " +
        "openssl rand -base64 48");
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.MapInboundClaims = false; // keep claim types as issued (avoid "sub" being remapped onto NameIdentifier)
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSection["Issuer"],
        ValidAudience = jwtSection["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        // Default is 5 minutes of leeway, which quietly extends every short-lived
        // access token. We want expiry to mean expiry.
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization();

// ---------------------------------------------------------------------------
// CORS. Only the browser client needs this; native apps do not send an Origin.
// Origins are configurable so a phone hitting the LAN IP can be allowed without
// a code change.
// ---------------------------------------------------------------------------
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:5173", "http://localhost:5175" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("WorkoutWebClient", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddOpenApi();

var app = builder.Build();

app.UseCors("WorkoutWebClient");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WorkoutDbContext>();
    db.Database.Migrate();
}

// Only redirect to HTTPS outside development. On a phone, a 307 to
// https://localhost:7053 points at the phone itself, so the request dies with a
// connection error that looks like the API being down.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
