using WorkoutLogger.Entities;
using WorkoutLogger.Models;

namespace WorkoutLogger.Mappers
{
    public static class WorkoutMapper
    {
        public static WorkoutDto ToDto(this Workout workout)
        {
            return new WorkoutDto
            {
                Id = workout.Id,
                Name = workout.Name,
                Duration = workout.Duration,
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
            workout.Exercises.ForEach(f =>
            {
                var updatedExercise = workoutDto.Exercises.FirstOrDefault(e => e.Id == f.Id);
                if(updatedExercise != null)
                    f.UpdateExercise(updatedExercise);
            });
            return workout;
        }
    }
}
