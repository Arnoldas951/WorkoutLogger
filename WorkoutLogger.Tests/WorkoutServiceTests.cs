using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WorkoutLogger.Context;
using WorkoutLogger.Entities;
using WorkoutLogger.Models;
using WorkoutLogger.Services;
using Xunit;

namespace WorkoutLogger.Tests
{
    public class WorkoutServiceTests
    {
        private static WorkoutDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<WorkoutDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            return new WorkoutDbContext(options);
        }

        private static WorkoutDto SampleDto() => new()
        {
            Name = "Push day",
            Date = DateTime.UtcNow,
            Description = "Chest and triceps",
            Exercises = new List<ExerciseDto>
            {
                new() { Name = "Bench press", Sets = 3, Repetitions = 10, Weight = 60 }
            }
        };

        [Fact]
        public async Task CreateWorkoutAsync_AssignsCallingUserAsOwner()
        {
            using var db = CreateContext();
            var service = new WorkoutService(NullLogger<WorkoutService>.Instance, db);

            var id = await service.CreateWorkoutAsync(SampleDto(), userId: 1);

            var stored = await db.Workouts.FirstAsync(w => w.Id == id);
            Assert.Equal(1, stored.UserId);
        }

        [Fact]
        public async Task GetWorkoutByIdAsync_ThrowsKeyNotFound_WhenWorkoutBelongsToAnotherUser()
        {
            using var db = CreateContext();
            var service = new WorkoutService(NullLogger<WorkoutService>.Instance, db);
            var id = await service.CreateWorkoutAsync(SampleDto(), userId: 1);

            await Assert.ThrowsAsync<KeyNotFoundException>(
                () => service.GetWorkoutByIdAsync(id, userId: 2));
        }

        [Fact]
        public async Task GetWorkoutByIdAsync_ThrowsKeyNotFound_WhenWorkoutDoesNotExist()
        {
            using var db = CreateContext();
            var service = new WorkoutService(NullLogger<WorkoutService>.Instance, db);

            await Assert.ThrowsAsync<KeyNotFoundException>(
                () => service.GetWorkoutByIdAsync(999, userId: 1));
        }

        [Fact]
        public async Task DeleteWorkoutAsync_ReturnsFalse_WhenWorkoutBelongsToAnotherUser()
        {
            using var db = CreateContext();
            var service = new WorkoutService(NullLogger<WorkoutService>.Instance, db);
            var id = await service.CreateWorkoutAsync(SampleDto(), userId: 1);

            var deleted = await service.DeleteWorkoutAsync(id, userId: 2);

            Assert.False(deleted);
            Assert.True(await db.Workouts.AnyAsync(w => w.Id == id));
        }

        [Fact]
        public async Task DeleteWorkoutAsync_ReturnsTrue_AndRemovesRow_ForOwner()
        {
            using var db = CreateContext();
            var service = new WorkoutService(NullLogger<WorkoutService>.Instance, db);
            var id = await service.CreateWorkoutAsync(SampleDto(), userId: 1);

            var deleted = await service.DeleteWorkoutAsync(id, userId: 1);

            Assert.True(deleted);
            Assert.False(await db.Workouts.AnyAsync(w => w.Id == id));
        }

        [Fact]
        public async Task UpdateWorkoutAsync_ThrowsKeyNotFound_WhenWorkoutBelongsToAnotherUser()
        {
            using var db = CreateContext();
            var service = new WorkoutService(NullLogger<WorkoutService>.Instance, db);
            var id = await service.CreateWorkoutAsync(SampleDto(), userId: 1);

            var update = SampleDto();
            update.Name = "Renamed";

            await Assert.ThrowsAsync<KeyNotFoundException>(
                () => service.UpdateWorkoutAsync(id, update, userId: 2));
        }

        [Fact]
        public async Task UpdateWorkoutAsync_PersistsChanges_ForOwner()
        {
            using var db = CreateContext();
            var service = new WorkoutService(NullLogger<WorkoutService>.Instance, db);
            var id = await service.CreateWorkoutAsync(SampleDto(), userId: 1);

            var update = SampleDto();
            update.Name = "Renamed";

            await service.UpdateWorkoutAsync(id, update, userId: 1);

            var stored = await db.Workouts.FirstAsync(w => w.Id == id);
            Assert.Equal("Renamed", stored.Name);
        }

        [Fact]
        public async Task GetWorkoutsAsync_OnlyReturnsCallingUsersWorkouts()
        {
            using var db = CreateContext();
            var service = new WorkoutService(NullLogger<WorkoutService>.Instance, db);
            await service.CreateWorkoutAsync(SampleDto(), userId: 1);
            await service.CreateWorkoutAsync(SampleDto(), userId: 2);
            await service.CreateWorkoutAsync(SampleDto(), userId: 1);

            var workouts = await service.GetWorkoutsAsync(userId: 1);

            Assert.Equal(2, workouts.Count);
        }
    }
}
