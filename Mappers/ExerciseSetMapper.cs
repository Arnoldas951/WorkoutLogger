using WorkoutLogger.Entities;
using WorkoutLogger.Models;

namespace WorkoutLogger.Mappers
{
    public static class ExerciseSetMapper
    {
        public static ExerciseSet ToEntity(this ExerciseSetDto dto)
        {
            return new ExerciseSet
            {
                Id = dto.Id,
                SetNumber = dto.SetNumber,
                Repetitions = dto.Repetitions,
                Weight = dto.Weight,
                Rpe = dto.Rpe,
                IsWarmup = dto.IsWarmup,
                Notes = dto.Notes
            };
        }

        public static ExerciseSetDto ToDto(this ExerciseSet entity)
        {
            return new ExerciseSetDto
            {
                Id = entity.Id,
                SetNumber = entity.SetNumber,
                Repetitions = entity.Repetitions,
                Weight = entity.Weight,
                Rpe = entity.Rpe,
                IsWarmup = entity.IsWarmup,
                Notes = entity.Notes
            };
        }

        public static ExerciseSet UpdateSet(this ExerciseSet entity, ExerciseSetDto dto)
        {
            entity.SetNumber = dto.SetNumber;
            entity.Repetitions = dto.Repetitions;
            entity.Weight = dto.Weight;
            entity.Rpe = dto.Rpe;
            entity.IsWarmup = dto.IsWarmup;
            entity.Notes = dto.Notes;
            return entity;
        }
    }
}
