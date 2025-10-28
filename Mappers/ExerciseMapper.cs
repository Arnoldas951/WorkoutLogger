using WorkoutLogger.Entities;
using WorkoutLogger.Models;

namespace WorkoutLogger.Mappers
{
    public static class ExerciseMapper
    {
        public static Exercise ToEntity(this ExerciseDto dto)
        {
            return new Exercise
            {
                Id = dto.Id,
                Name = dto.Name,
                Repetitions = dto.Repetitions,
                Sets = dto.Sets,
                WorkoutId = dto.WorkoutId,
                Weight = dto.Weight,
                Notes = dto.Notes
            };
        }
        public static ExerciseDto ToDto(this Exercise entity)
        {
            return new ExerciseDto
            {
                Id = entity.Id,
                Name = entity.Name,
                Repetitions = entity.Repetitions,
                Sets = entity.Sets,
                WorkoutId = entity.WorkoutId,
                Weight = entity.Weight,
                Notes = entity.Notes
            };
        }

        public static Exercise UpdateExercise(this Exercise entity, ExerciseDto dto)
        {
            entity.Name = dto.Name;
            entity.Repetitions = dto.Repetitions;
            entity.Sets = dto.Sets;
            entity.WorkoutId = dto.WorkoutId;
            entity.Weight = dto.Weight;
            entity.Notes = dto.Notes;
            return entity;
        }
    }
}
