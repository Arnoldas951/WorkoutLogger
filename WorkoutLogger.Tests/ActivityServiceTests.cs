using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WorkoutLogger.Context;
using WorkoutLogger.Models;
using WorkoutLogger.Services;
using Xunit;

namespace WorkoutLogger.Tests
{
    public class ActivityServiceTests
    {
        private static WorkoutDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<WorkoutDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            return new WorkoutDbContext(options);
        }

        private static ActivityService CreateService(WorkoutDbContext db) =>
            new(NullLogger<ActivityService>.Instance, db);

        private static WorkoutService CreateWorkoutService(WorkoutDbContext db) =>
            new(NullLogger<WorkoutService>.Instance, db);

        private static readonly DateTime Noon = new(2026, 8, 5, 12, 0, 0, DateTimeKind.Utc);

        private static ActivityDto RunDto(
            string? externalId = "hc-run-1",
            DateTime? start = null,
            int durationMinutes = 45) => new()
            {
                ExternalId = externalId,
                Source = "HealthConnect",
                ActivityType = "Running",
                Title = "Morning run",
                StartTime = start ?? Noon,
                EndTime = (start ?? Noon).AddMinutes(durationMinutes),
                AverageHeartRate = 148,
                MaxHeartRate = 176,
                DistanceMeters = 8200,
            };

        private static WorkoutDto WorkoutDtoAt(DateTime date) => new()
        {
            Name = "Push day",
            Date = date,
            Exercises = new List<ExerciseDto>
            {
                new()
                {
                    Name = "Bench press",
                    Sets = new List<ExerciseSetDto> { new() { Repetitions = 10, Weight = 60 } }
                }
            }
        };

        // ------------------------------------------------------------------
        // Standing alone - the case the whole design exists for
        // ------------------------------------------------------------------

        [Fact]
        public async Task ImportAsync_StoresAnActivityWithNoWorkout()
        {
            using var db = CreateContext();
            var service = CreateService(db);

            // A run with the watch, phone left at home.
            var activity = await service.ImportAsync(RunDto(), userId: 1);

            Assert.Null(activity.WorkoutId);
            Assert.Equal("Running", activity.ActivityType);
            Assert.Equal(148, activity.AverageHeartRate);
            Assert.Equal(45, activity.DurationMinutes);
        }

        [Fact]
        public async Task AWorkoutCanExistWithNoActivity()
        {
            using var db = CreateContext();
            var workouts = CreateWorkoutService(db);
            var activities = CreateService(db);

            // A gym session logged on the phone, watch left at home.
            await workouts.CreateWorkoutAsync(WorkoutDtoAt(Noon), userId: 1);

            Assert.Single(await workouts.GetWorkoutsAsync(userId: 1));
            Assert.Empty(await activities.GetActivitiesAsync(userId: 1));
        }

        // ------------------------------------------------------------------
        // Import idempotency
        // ------------------------------------------------------------------

        [Fact]
        public async Task ImportAsync_IsIdempotentOnExternalId()
        {
            using var db = CreateContext();
            var service = CreateService(db);

            // Health Connect returns the same records on every read.
            var first = await service.ImportAsync(RunDto(), userId: 1);
            var second = await service.ImportAsync(RunDto(), userId: 1);

            Assert.Equal(first.Id, second.Id);
            Assert.Single(await service.GetActivitiesAsync(userId: 1));
        }

        [Fact]
        public async Task ImportAsync_UpdatesMetricsOnReimport()
        {
            using var db = CreateContext();
            var service = CreateService(db);
            await service.ImportAsync(RunDto(), userId: 1);

            var corrected = RunDto();
            corrected.AverageHeartRate = 151;
            corrected.DistanceMeters = 8350;
            await service.ImportAsync(corrected, userId: 1);

            var stored = Assert.Single(await service.GetActivitiesAsync(userId: 1));
            Assert.Equal(151, stored.AverageHeartRate);
            Assert.Equal(8350, stored.DistanceMeters);
        }

        [Fact]
        public async Task ImportAsync_KeepsSeparateActivitiesForDifferentUsers()
        {
            using var db = CreateContext();
            var service = CreateService(db);

            await service.ImportAsync(RunDto(), userId: 1);
            await service.ImportAsync(RunDto(), userId: 2);

            Assert.Single(await service.GetActivitiesAsync(userId: 1));
            Assert.Single(await service.GetActivitiesAsync(userId: 2));
        }

        [Fact]
        public async Task ImportAsync_TreatsActivitiesWithoutExternalIdAsDistinct()
        {
            using var db = CreateContext();
            var service = CreateService(db);

            var manual = RunDto(externalId: null);
            manual.Source = "Manual";

            await service.ImportAsync(manual, userId: 1);
            await service.ImportAsync(manual, userId: 1);

            // Nothing to deduplicate on, so two hand-entered activities are two
            // activities rather than one silently overwritten.
            Assert.Equal(2, (await service.GetActivitiesAsync(userId: 1)).Count);
        }

        // ------------------------------------------------------------------
        // Linking
        // ------------------------------------------------------------------

        [Fact]
        public async Task LinkToWorkoutAsync_AttachesTheActivity()
        {
            using var db = CreateContext();
            var workouts = CreateWorkoutService(db);
            var activities = CreateService(db);

            var workoutId = await workouts.CreateWorkoutAsync(WorkoutDtoAt(Noon), userId: 1);
            var activity = await activities.ImportAsync(RunDto(), userId: 1);

            var linked = await activities.LinkToWorkoutAsync(activity.Id, workoutId, userId: 1);

            Assert.True(linked);
            Assert.Equal(workoutId, (await activities.GetActivityByIdAsync(activity.Id, 1)).WorkoutId);
        }

        [Fact]
        public async Task LinkToWorkoutAsync_RefusesAnotherUsersWorkout()
        {
            using var db = CreateContext();
            var workouts = CreateWorkoutService(db);
            var activities = CreateService(db);

            var theirWorkout = await workouts.CreateWorkoutAsync(WorkoutDtoAt(Noon), userId: 2);
            var myActivity = await activities.ImportAsync(RunDto(), userId: 1);

            var linked = await activities.LinkToWorkoutAsync(myActivity.Id, theirWorkout, userId: 1);

            Assert.False(linked);
            Assert.Null((await activities.GetActivityByIdAsync(myActivity.Id, 1)).WorkoutId);
        }

        [Fact]
        public async Task UnlinkAsync_DetachesWithoutDeleting()
        {
            using var db = CreateContext();
            var workouts = CreateWorkoutService(db);
            var activities = CreateService(db);

            var workoutId = await workouts.CreateWorkoutAsync(WorkoutDtoAt(Noon), userId: 1);
            var activity = await activities.ImportAsync(RunDto(), userId: 1);
            await activities.LinkToWorkoutAsync(activity.Id, workoutId, userId: 1);

            Assert.True(await activities.UnlinkAsync(activity.Id, userId: 1));

            var stored = await activities.GetActivityByIdAsync(activity.Id, userId: 1);
            Assert.Null(stored.WorkoutId);
            Assert.Equal(148, stored.AverageHeartRate);
        }

        [Fact]
        public async Task UnlinkAsync_ReturnsFalse_WhenNotLinked()
        {
            using var db = CreateContext();
            var service = CreateService(db);
            var activity = await service.ImportAsync(RunDto(), userId: 1);

            Assert.False(await service.UnlinkAsync(activity.Id, userId: 1));
        }

        [Fact]
        public async Task DeletingAWorkout_ReleasesTheActivityInsteadOfDestroyingIt()
        {
            using var db = CreateContext();
            var workouts = CreateWorkoutService(db);
            var activities = CreateService(db);

            var workoutId = await workouts.CreateWorkoutAsync(WorkoutDtoAt(Noon), userId: 1);
            var activity = await activities.ImportAsync(RunDto(), userId: 1);
            await activities.LinkToWorkoutAsync(activity.Id, workoutId, userId: 1);

            await workouts.DeleteWorkoutAsync(workoutId, userId: 1);

            // The watch recording came off the device and cannot be recreated, so
            // deleting the sets typed alongside it must not take it with them.
            var stored = await activities.GetActivityByIdAsync(activity.Id, userId: 1);
            Assert.Equal(176, stored.MaxHeartRate);

            // And it goes back in the pool, rather than staying attached to a
            // workout that no longer exists.
            Assert.Null(stored.WorkoutId);
            Assert.Single(await activities.GetOverlappingAsync(1, Noon, Noon.AddHours(1)));
        }

        // ------------------------------------------------------------------
        // Overlap matching
        // ------------------------------------------------------------------

        [Fact]
        public async Task GetOverlappingAsync_FindsAPartiallyOverlappingSession()
        {
            using var db = CreateContext();
            var service = CreateService(db);

            // Watch running 12:00-12:45; sets typed into the phone 12:10-13:00.
            await service.ImportAsync(RunDto(), userId: 1);

            var matches = await service.GetOverlappingAsync(
                userId: 1,
                start: Noon.AddMinutes(10),
                end: Noon.AddMinutes(60));

            Assert.Single(matches);
        }

        [Fact]
        public async Task GetOverlappingAsync_IgnoresSessionsThatDoNotTouchTheWindow()
        {
            using var db = CreateContext();
            var service = CreateService(db);
            await service.ImportAsync(RunDto(), userId: 1);

            var matches = await service.GetOverlappingAsync(
                userId: 1,
                start: Noon.AddHours(3),
                end: Noon.AddHours(4));

            Assert.Empty(matches);
        }

        [Fact]
        public async Task GetOverlappingAsync_ExcludesAlreadyAttachedSessions()
        {
            using var db = CreateContext();
            var workouts = CreateWorkoutService(db);
            var activities = CreateService(db);

            var workoutId = await workouts.CreateWorkoutAsync(WorkoutDtoAt(Noon), userId: 1);
            var activity = await activities.ImportAsync(RunDto(), userId: 1);

            Assert.Single(await activities.GetOverlappingAsync(1, Noon, Noon.AddHours(1)));

            await activities.LinkToWorkoutAsync(activity.Id, workoutId, userId: 1);

            // Already spoken for, so it should not be offered again.
            Assert.Empty(await activities.GetOverlappingAsync(1, Noon, Noon.AddHours(1)));
        }

        [Fact]
        public async Task GetOverlappingAsync_IsScopedToTheCallingUser()
        {
            using var db = CreateContext();
            var service = CreateService(db);
            await service.ImportAsync(RunDto(), userId: 2);

            Assert.Empty(await service.GetOverlappingAsync(1, Noon, Noon.AddHours(1)));
        }

        // ------------------------------------------------------------------
        // Deletion
        // ------------------------------------------------------------------

        [Fact]
        public async Task DeleteAsync_TombstonesTheActivity()
        {
            using var db = CreateContext();
            var service = CreateService(db);
            var activity = await service.ImportAsync(RunDto(), userId: 1);

            Assert.True(await service.DeleteAsync(activity.Id, userId: 1));
            Assert.Empty(await service.GetActivitiesAsync(userId: 1));

            var tombstone = await db.Activities.IgnoreQueryFilters().SingleAsync();
            Assert.NotNull(tombstone.DeletedAt);
        }

        // ------------------------------------------------------------------
        // Delta sync
        // ------------------------------------------------------------------

        [Fact]
        public async Task GetChangesAsync_ReturnsEverything_WithNoWatermark()
        {
            using var db = CreateContext();
            var service = CreateService(db);
            await service.ImportAsync(RunDto("hc-1"), userId: 1);
            await service.ImportAsync(RunDto("hc-2"), userId: 1);

            var changes = await service.GetChangesAsync(userId: 1, since: null);

            Assert.Equal(2, changes.Changed.Count);
            Assert.Empty(changes.Deleted);
        }

        [Fact]
        public async Task GetChangesAsync_ReportsTombstonesByPublicId()
        {
            using var db = CreateContext();
            var service = CreateService(db);
            var activity = await service.ImportAsync(RunDto(), userId: 1);

            // Backdate so the watermark comparison does not depend on how fast
            // the test runs.
            var stored = await db.Activities.IgnoreQueryFilters().SingleAsync();
            stored.UpdatedAt = DateTime.UtcNow.AddHours(-2);
            await db.SaveChangesAsync();

            var watermark = DateTime.UtcNow.AddHours(-1);
            Assert.Empty((await service.GetChangesAsync(1, watermark)).Changed);

            await service.DeleteAsync(activity.Id, userId: 1);

            var delta = await service.GetChangesAsync(1, watermark);
            Assert.Empty(delta.Changed);
            Assert.Equal(activity.PublicId, Assert.Single(delta.Deleted));
        }

        [Fact]
        public async Task GetChangesAsync_IsScopedToTheCallingUser()
        {
            using var db = CreateContext();
            var service = CreateService(db);
            await service.ImportAsync(RunDto(), userId: 2);

            Assert.Empty((await service.GetChangesAsync(userId: 1, since: null)).Changed);
        }

        [Fact]
        public async Task DeleteAsync_ReturnsFalse_ForAnotherUsersActivity()
        {
            using var db = CreateContext();
            var service = CreateService(db);
            var activity = await service.ImportAsync(RunDto(), userId: 1);

            Assert.False(await service.DeleteAsync(activity.Id, userId: 2));
            Assert.Single(await service.GetActivitiesAsync(userId: 1));
        }

        [Fact]
        public async Task ReimportingADeletedActivity_RevivesIt()
        {
            using var db = CreateContext();
            var service = CreateService(db);
            var activity = await service.ImportAsync(RunDto(), userId: 1);
            await service.DeleteAsync(activity.Id, userId: 1);

            // The unique index on (UserId, ExternalId) survives the tombstone, so
            // this has to update the existing row rather than insert a second.
            var revived = await service.ImportAsync(RunDto(), userId: 1);

            Assert.Equal(activity.Id, revived.Id);
            Assert.Single(await service.GetActivitiesAsync(userId: 1));
        }
    }
}
