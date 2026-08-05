using WorkoutLogger.Entities;
using WorkoutLogger.Models;

namespace WorkoutLogger.Mappers
{
    public static class WorkoutMapper
    {
        // Npgsql requires DateTime.Kind = Utc for 'timestamp with time zone' columns.
        // JSON dates without an offset deserialize as Kind=Unspecified, so stamp them as UTC.
        private static DateTime EnsureUtc(DateTime date) => date.Kind switch
        {
            DateTimeKind.Utc => date,
            DateTimeKind.Local => date.ToUniversalTime(),
            _ => DateTime.SpecifyKind(date, DateTimeKind.Utc)
        };

        /// <summary>
        /// Renumbers Order and SetNumber from array position, so clients only have to
        /// send things in the right sequence rather than compute indices themselves.
        /// This makes drag-to-reorder on the client a pure array move.
        /// </summary>
        private static void Normalize(WorkoutDto dto)
        {
            for (var i = 0; i < dto.Exercises.Count; i++)
            {
                var exercise = dto.Exercises[i];
                exercise.Order = i + 1;

                for (var j = 0; j < exercise.Sets.Count; j++)
                {
                    exercise.Sets[j].SetNumber = j + 1;
                }
            }
        }

        public static WorkoutDto ToDto(this Workout workout)
        {
            return new WorkoutDto
            {
                Id = workout.Id,
                PublicId = workout.PublicId,
                UpdatedAt = workout.UpdatedAt,
                Name = workout.Name,
                Duration = workout.Duration,
                Date = workout.Date,
                Description = workout.Description,
                Exercises = workout.Exercises
                    .OrderBy(e => e.Order)
                    .ThenBy(e => e.Id)
                    .Select(e => e.ToDto())
                    .ToList()
            };
        }

        public static Workout ToEntity(this WorkoutDto workoutDto)
        {
            Normalize(workoutDto);

            return new Workout
            {
                Id = workoutDto.Id,
                // A client that logged this offline already picked an id; honour it
                // so a retry lands on the same row. Otherwise mint one here.
                PublicId = workoutDto.PublicId == Guid.Empty ? Guid.NewGuid() : workoutDto.PublicId,
                Name = workoutDto.Name,
                Duration = workoutDto.Duration,
                Date = EnsureUtc(workoutDto.Date),
                Description = workoutDto.Description,
                Exercises = workoutDto.Exercises.Select(e => e.ToEntity()).ToList()
                // CreatedAt / UpdatedAt are stamped by the service, never by the client.
            };
        }

        public static Workout UpdateWorkout(this Workout workout, WorkoutDto workoutDto)
        {
            Normalize(workoutDto);

            workout.Name = workoutDto.Name;
            workout.Duration = workoutDto.Duration;
            workout.Date = EnsureUtc(workoutDto.Date);
            workout.Description = workoutDto.Description;

            // Drop persisted exercises the client no longer sends. Only ids the client
            // actually echoed back count; a dto with Id == 0 is a brand new exercise.
            var keptExerciseIds = workoutDto.Exercises.Where(e => e.Id > 0).Select(e => e.Id).ToHashSet();
            workout.Exercises.RemoveAll(e => !keptExerciseIds.Contains(e.Id));

            foreach (var exerciseDto in workoutDto.Exercises)
            {
                // Match on id only when the client sent a real one, otherwise two new
                // exercises (both Id == 0) would collapse onto the same entity.
                var existingExercise = exerciseDto.Id > 0
                    ? workout.Exercises.FirstOrDefault(e => e.Id == exerciseDto.Id)
                    : null;

                if (existingExercise != null)
                    existingExercise.UpdateExercise(exerciseDto);
                else
                    workout.Exercises.Add(exerciseDto.ToEntity());
            }

            return workout;
        }
    }
}
