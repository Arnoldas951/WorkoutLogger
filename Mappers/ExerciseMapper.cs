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
                Order = dto.Order,
                Notes = dto.Notes,
                Sets = dto.Sets
                    .OrderBy(s => s.SetNumber)
                    .Select(s => s.ToEntity())
                    .ToList()
            };
        }

        public static ExerciseDto ToDto(this Exercise entity)
        {
            return new ExerciseDto
            {
                Id = entity.Id,
                Name = entity.Name,
                Order = entity.Order,
                WorkoutId = entity.WorkoutId,
                Notes = entity.Notes,
                Sets = entity.Sets
                    .OrderBy(s => s.SetNumber)
                    .Select(s => s.ToDto())
                    .ToList()
            };
        }

        public static Exercise UpdateExercise(this Exercise entity, ExerciseDto dto)
        {
            entity.Name = dto.Name;
            entity.Order = dto.Order;
            entity.Notes = dto.Notes;

            // Drop persisted sets the client no longer sends. Only ids the client
            // actually echoed back count; a dto with Id == 0 is a brand new set.
            var keptSetIds = dto.Sets.Where(s => s.Id > 0).Select(s => s.Id).ToHashSet();
            entity.Sets.RemoveAll(s => !keptSetIds.Contains(s.Id));

            foreach (var setDto in dto.Sets)
            {
                // Match on id only when the client sent a real one, otherwise two new
                // sets (both Id == 0) would collapse onto the same entity.
                var existing = setDto.Id > 0
                    ? entity.Sets.FirstOrDefault(s => s.Id == setDto.Id)
                    : null;

                if (existing != null)
                    existing.UpdateSet(setDto);
                else
                    entity.Sets.Add(setDto.ToEntity());
            }

            return entity;
        }
    }
}
