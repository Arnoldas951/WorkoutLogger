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


        public static WorkoutDto ToDto(this Workout workout)
        {
            return new WorkoutDto
            {
                Id = workout.Id,
                Name = workout.Name,
                Duration = workout.Duration,
                Date = workout.Date,
                Description = workout.Description,
                Exercises = workout.Exercises.Select(e => new ExerciseDto
                {
                    Id = e.Id,
                    Name = e.Name,
                    Repetitions = e.Repetitions,
                    Sets = e.Sets
                }).ToList()
            };
        }

        public static Workout ToEntity(this WorkoutDto workoutDto)
        {
            return new Workout
            {
                Id = workoutDto.Id,
                Name = workoutDto.Name,
                Duration = workoutDto.Duration,
                Date = EnsureUtc(workoutDto.Date),
                Description = workoutDto.Description,
                Exercises = workoutDto.Exercises.Select(e => new Exercise
                {
                    Id = e.Id,
                    Name = e.Name,
                    Repetitions = e.Repetitions,
                    Sets = e.Sets
                }).ToList()
            };
        }

        public static Workout UpdateWorkout(this Workout workout, WorkoutDto workoutDto)
        {
            workout.Name = workoutDto.Name;
            workout.Duration = workoutDto.Duration;
            workout.Date = EnsureUtc(workoutDto.Date);
            workout.Description = workoutDto.Description;

            var dtoExerciseIds = workoutDto.Exercises.Select(e => e.Id).ToHashSet();
            workout.Exercises.RemoveAll(e => !dtoExerciseIds.Contains(e.Id));

            foreach (var exerciseDto in workoutDto.Exercises)
            {
                var existingExercise = workout.Exercises.FirstOrDefault(e => e.Id == exerciseDto.Id);
                if (existingExercise != null)
                    existingExercise.UpdateExercise(exerciseDto);
                else
                    workout.Exercises.Add(exerciseDto.ToEntity());
            }

            return workout;
        }
    }
}
