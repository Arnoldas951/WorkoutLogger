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
                new()
                {
                    Name = "Bench press",
                    Sets = new List<ExerciseSetDto>
                    {
                        new() { Repetitions = 10, Weight = 60 },
                        new() { Repetitions = 8,  Weight = 65 },
                        new() { Repetitions = 6,  Weight = 70 }
                    }
                }
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
        public async Task DeleteWorkoutAsync_ReturnsTrue_AndHidesTheWorkout_ForOwner()
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

        [Fact]
        public async Task CreateWorkoutAsync_PersistsEachSetIndividually()
        {
            using var db = CreateContext();
            var service = new WorkoutService(NullLogger<WorkoutService>.Instance, db);

            var id = await service.CreateWorkoutAsync(SampleDto(), userId: 1);

            var workout = await service.GetWorkoutByIdAsync(id, userId: 1);
            var exercise = Assert.Single(workout.Exercises);

            Assert.Collection(exercise.Sets,
                s => { Assert.Equal(1, s.SetNumber); Assert.Equal(10, s.Repetitions); Assert.Equal(60, s.Weight); },
                s => { Assert.Equal(2, s.SetNumber); Assert.Equal(8, s.Repetitions); Assert.Equal(65, s.Weight); },
                s => { Assert.Equal(3, s.SetNumber); Assert.Equal(6, s.Repetitions); Assert.Equal(70, s.Weight); });
        }

        [Fact]
        public async Task CreateWorkoutAsync_NumbersExercisesFromListPosition()
        {
            using var db = CreateContext();
            var service = new WorkoutService(NullLogger<WorkoutService>.Instance, db);

            var dto = SampleDto();
            dto.Exercises.Add(new ExerciseDto
            {
                Name = "Overhead press",
                Sets = new List<ExerciseSetDto> { new() { Repetitions = 12, Weight = 30 } }
            });

            var id = await service.CreateWorkoutAsync(dto, userId: 1);
            var workout = await service.GetWorkoutByIdAsync(id, userId: 1);

            Assert.Equal(new[] { 1, 2 }, workout.Exercises.Select(e => e.Order));
            Assert.Equal(new[] { "Bench press", "Overhead press" }, workout.Exercises.Select(e => e.Name));
        }

        [Fact]
        public async Task TotalVolume_SumsRepsTimesWeight_AndIgnoresWarmups()
        {
            using var db = CreateContext();
            var service = new WorkoutService(NullLogger<WorkoutService>.Instance, db);

            var dto = SampleDto();
            dto.Exercises[0].Sets.Insert(0, new ExerciseSetDto { Repetitions = 15, Weight = 20, IsWarmup = true });

            var id = await service.CreateWorkoutAsync(dto, userId: 1);
            var workout = await service.GetWorkoutByIdAsync(id, userId: 1);

            // (10*60) + (8*65) + (6*70) = 600 + 520 + 420, warm-up excluded.
            Assert.Equal(1540, workout.TotalVolume);
            Assert.Equal(3, workout.TotalSets);
        }

        [Fact]
        public async Task UpdateWorkoutAsync_RemovesSetsTheClientNoLongerSends()
        {
            using var db = CreateContext();
            var service = new WorkoutService(NullLogger<WorkoutService>.Instance, db);
            var id = await service.CreateWorkoutAsync(SampleDto(), userId: 1);

            var update = await service.GetWorkoutByIdAsync(id, userId: 1);
            update.Exercises[0].Sets.RemoveAt(2);

            await service.UpdateWorkoutAsync(id, update, userId: 1);

            var reloaded = await service.GetWorkoutByIdAsync(id, userId: 1);
            Assert.Equal(2, reloaded.Exercises[0].Sets.Count);
            // The orphaned set is deleted, not just detached from the collection.
            Assert.Equal(0, await db.ExerciseSets.CountAsync(s => s.Repetitions == 6));
            Assert.Equal(2, await db.ExerciseSets.CountAsync());
        }

        [Fact]
        public async Task UpdateWorkoutAsync_AddsNewSetsWithoutCollapsingThem()
        {
            using var db = CreateContext();
            var service = new WorkoutService(NullLogger<WorkoutService>.Instance, db);
            var id = await service.CreateWorkoutAsync(SampleDto(), userId: 1);

            var update = await service.GetWorkoutByIdAsync(id, userId: 1);
            // Two brand new sets, both with Id == 0. A naive id match would merge them.
            update.Exercises[0].Sets.Add(new ExerciseSetDto { Repetitions = 5, Weight = 75 });
            update.Exercises[0].Sets.Add(new ExerciseSetDto { Repetitions = 4, Weight = 80 });

            await service.UpdateWorkoutAsync(id, update, userId: 1);

            var reloaded = await service.GetWorkoutByIdAsync(id, userId: 1);
            Assert.Equal(5, reloaded.Exercises[0].Sets.Count);
            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, reloaded.Exercises[0].Sets.Select(s => s.SetNumber));
            Assert.Equal(new[] { 10, 8, 6, 5, 4 }, reloaded.Exercises[0].Sets.Select(s => s.Repetitions));
        }

        [Fact]
        public async Task UpdateWorkoutAsync_EditsAnExistingSetInPlace()
        {
            using var db = CreateContext();
            var service = new WorkoutService(NullLogger<WorkoutService>.Instance, db);
            var id = await service.CreateWorkoutAsync(SampleDto(), userId: 1);

            var update = await service.GetWorkoutByIdAsync(id, userId: 1);
            var targetSetId = update.Exercises[0].Sets[1].Id;
            update.Exercises[0].Sets[1].Weight = 67.5;
            update.Exercises[0].Sets[1].Rpe = 8.5;

            await service.UpdateWorkoutAsync(id, update, userId: 1);

            var stored = await db.ExerciseSets.AsNoTracking().FirstAsync(s => s.Id == targetSetId);
            Assert.Equal(67.5, stored.Weight);
            Assert.Equal(8.5, stored.Rpe);
            Assert.Equal(3, await db.ExerciseSets.CountAsync());
        }

        // ------------------------------------------------------------------
        // Offline sync
        // ------------------------------------------------------------------

        [Fact]
        public async Task CreateWorkoutAsync_AssignsAPublicId_WhenTheClientDoesNotSupplyOne()
        {
            using var db = CreateContext();
            var service = new WorkoutService(NullLogger<WorkoutService>.Instance, db);

            var id = await service.CreateWorkoutAsync(SampleDto(), userId: 1);

            var workout = await service.GetWorkoutByIdAsync(id, userId: 1);
            Assert.NotEqual(Guid.Empty, workout.PublicId);
        }

        [Fact]
        public async Task CreateWorkoutAsync_HonoursAClientGeneratedPublicId()
        {
            using var db = CreateContext();
            var service = new WorkoutService(NullLogger<WorkoutService>.Instance, db);

            var publicId = Guid.NewGuid();
            var dto = SampleDto();
            dto.PublicId = publicId;

            var id = await service.CreateWorkoutAsync(dto, userId: 1);

            var workout = await service.GetWorkoutByIdAsync(id, userId: 1);
            Assert.Equal(publicId, workout.PublicId);
        }

        [Fact]
        public async Task CreateWorkoutAsync_IsIdempotent_WhenTheSamePublicIdIsPostedTwice()
        {
            using var db = CreateContext();
            var service = new WorkoutService(NullLogger<WorkoutService>.Instance, db);

            var publicId = Guid.NewGuid();
            var first = SampleDto();
            first.PublicId = publicId;

            // The response to the first call is lost, so the sync queue retries.
            var idA = await service.CreateWorkoutAsync(first, userId: 1);

            var retry = SampleDto();
            retry.PublicId = publicId;
            retry.Name = "Push day (edited before the retry landed)";
            var idB = await service.CreateWorkoutAsync(retry, userId: 1);

            Assert.Equal(idA, idB);
            Assert.Single(await service.GetWorkoutsAsync(userId: 1));

            var stored = await service.GetWorkoutByIdAsync(idA, userId: 1);
            Assert.Equal("Push day (edited before the retry landed)", stored.Name);
        }

        [Fact]
        public async Task CreateWorkoutAsync_DoesNotCollideAcrossUsers()
        {
            using var db = CreateContext();
            var service = new WorkoutService(NullLogger<WorkoutService>.Instance, db);

            var publicId = Guid.NewGuid();
            var mine = SampleDto();
            mine.PublicId = publicId;
            await service.CreateWorkoutAsync(mine, userId: 1);

            // Same id, different user: the upsert lookup is scoped by user, so this
            // must not quietly overwrite someone else's workout.
            var theirs = SampleDto();
            theirs.PublicId = publicId;
            await service.CreateWorkoutAsync(theirs, userId: 2);

            Assert.Single(await service.GetWorkoutsAsync(userId: 1));
            Assert.Single(await service.GetWorkoutsAsync(userId: 2));
        }

        [Fact]
        public async Task DeleteWorkoutAsync_KeepsTheRowAsATombstone()
        {
            using var db = CreateContext();
            var service = new WorkoutService(NullLogger<WorkoutService>.Instance, db);
            var id = await service.CreateWorkoutAsync(SampleDto(), userId: 1);

            await service.DeleteWorkoutAsync(id, userId: 1);

            // Gone from every ordinary read...
            Assert.Empty(await service.GetWorkoutsAsync(userId: 1));
            await Assert.ThrowsAsync<KeyNotFoundException>(
                () => service.GetWorkoutByIdAsync(id, userId: 1));

            // ...but still on disk, so the delete can be told to other devices.
            var tombstone = await db.Workouts.IgnoreQueryFilters().SingleAsync(w => w.Id == id);
            Assert.NotNull(tombstone.DeletedAt);
        }

        [Fact]
        public async Task GetChangesAsync_ReturnsEverything_WhenThereIsNoWatermark()
        {
            using var db = CreateContext();
            var service = new WorkoutService(NullLogger<WorkoutService>.Instance, db);
            await service.CreateWorkoutAsync(SampleDto(), userId: 1);
            await service.CreateWorkoutAsync(SampleDto(), userId: 1);

            var changes = await service.GetChangesAsync(userId: 1, since: null);

            Assert.Equal(2, changes.Changed.Count);
            Assert.Empty(changes.Deleted);
            Assert.NotEqual(default, changes.SyncedAt);
        }

        /// <summary>
        /// Backdates a workout so watermark comparisons do not depend on how fast
        /// the test runs. Two writes landing in the same clock tick would otherwise
        /// make these assertions flaky.
        /// </summary>
        private static async Task Backdate(WorkoutDbContext db, int workoutId, TimeSpan ago)
        {
            var workout = await db.Workouts.IgnoreQueryFilters().SingleAsync(w => w.Id == workoutId);
            workout.UpdatedAt = DateTime.UtcNow.Subtract(ago);
            await db.SaveChangesAsync();
        }

        [Fact]
        public async Task GetChangesAsync_OnlyReturnsWorkoutsTouchedAfterTheWatermark()
        {
            using var db = CreateContext();
            var service = new WorkoutService(NullLogger<WorkoutService>.Instance, db);

            var oldId = await service.CreateWorkoutAsync(SampleDto(), userId: 1);
            await Backdate(db, oldId, TimeSpan.FromHours(2));

            var watermark = DateTime.UtcNow.AddHours(-1);

            // Nothing has changed since the watermark yet.
            var quiet = await service.GetChangesAsync(userId: 1, since: watermark);
            Assert.Empty(quiet.Changed);
            Assert.Empty(quiet.Deleted);

            var second = SampleDto();
            second.Name = "Pull day";
            await service.CreateWorkoutAsync(second, userId: 1);

            var delta = await service.GetChangesAsync(userId: 1, since: watermark);
            Assert.Equal("Pull day", Assert.Single(delta.Changed).Name);
        }

        [Fact]
        public async Task GetChangesAsync_ReportsDeletesSeparatelyByPublicId()
        {
            using var db = CreateContext();
            var service = new WorkoutService(NullLogger<WorkoutService>.Instance, db);
            var id = await service.CreateWorkoutAsync(SampleDto(), userId: 1);
            var publicId = (await service.GetWorkoutByIdAsync(id, userId: 1)).PublicId;

            await Backdate(db, id, TimeSpan.FromHours(2));
            var watermark = DateTime.UtcNow.AddHours(-1);

            await service.DeleteWorkoutAsync(id, userId: 1);

            var delta = await service.GetChangesAsync(userId: 1, since: watermark);

            Assert.Empty(delta.Changed);
            Assert.Equal(publicId, Assert.Single(delta.Deleted));
        }

        [Fact]
        public async Task GetChangesAsync_IsScopedToTheCallingUser()
        {
            using var db = CreateContext();
            var service = new WorkoutService(NullLogger<WorkoutService>.Instance, db);
            await service.CreateWorkoutAsync(SampleDto(), userId: 1);
            await service.CreateWorkoutAsync(SampleDto(), userId: 2);

            var changes = await service.GetChangesAsync(userId: 1, since: null);

            Assert.Single(changes.Changed);
        }

        [Fact]
        public async Task UpdateWorkoutAsync_MovesTheWorkoutIntoTheNextDelta()
        {
            using var db = CreateContext();
            var service = new WorkoutService(NullLogger<WorkoutService>.Instance, db);
            var id = await service.CreateWorkoutAsync(SampleDto(), userId: 1);

            await Backdate(db, id, TimeSpan.FromHours(2));
            var watermark = DateTime.UtcNow.AddHours(-1);

            Assert.Empty((await service.GetChangesAsync(userId: 1, since: watermark)).Changed);

            var update = await service.GetWorkoutByIdAsync(id, userId: 1);
            update.Name = "Renamed";
            await service.UpdateWorkoutAsync(id, update, userId: 1);

            var delta = await service.GetChangesAsync(userId: 1, since: watermark);
            Assert.Equal("Renamed", Assert.Single(delta.Changed).Name);
        }

        [Fact]
        public async Task RecreatingADeletedWorkout_RevivesItRatherThanFailing()
        {
            using var db = CreateContext();
            var service = new WorkoutService(NullLogger<WorkoutService>.Instance, db);

            var publicId = Guid.NewGuid();
            var dto = SampleDto();
            dto.PublicId = publicId;
            var id = await service.CreateWorkoutAsync(dto, userId: 1);
            await service.DeleteWorkoutAsync(id, userId: 1);

            // A device that deleted while offline, changed its mind, and pushed the
            // workout again. The unique index on PublicId means this has to update
            // the tombstoned row rather than insert a second one.
            var again = SampleDto();
            again.PublicId = publicId;
            again.Name = "Back from the dead";
            var revivedId = await service.CreateWorkoutAsync(again, userId: 1);

            Assert.Equal(id, revivedId);
            var workout = Assert.Single(await service.GetWorkoutsAsync(userId: 1));
            Assert.Equal("Back from the dead", workout.Name);
        }
    }
}
