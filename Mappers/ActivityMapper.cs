using WorkoutLogger.Entities;
using WorkoutLogger.Models;

namespace WorkoutLogger.Mappers
{
    public static class ActivityMapper
    {
        // Npgsql requires DateTime.Kind = Utc for 'timestamp with time zone'.
        private static DateTime EnsureUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        private static ActivitySource ParseSource(string? source) =>
            Enum.TryParse<ActivitySource>(source, ignoreCase: true, out var parsed)
                ? parsed
                : ActivitySource.HealthConnect;

        public static ActivityDto ToDto(this Activity entity) => new()
        {
            Id = entity.Id,
            PublicId = entity.PublicId,
            ExternalId = entity.ExternalId,
            Source = entity.Source.ToString(),
            ActivityType = entity.ActivityType,
            Title = entity.Title,
            StartTime = entity.StartTime,
            EndTime = entity.EndTime,
            AverageHeartRate = entity.AverageHeartRate,
            MaxHeartRate = entity.MaxHeartRate,
            MinHeartRate = entity.MinHeartRate,
            ActiveCalories = entity.ActiveCalories,
            TotalCalories = entity.TotalCalories,
            DistanceMeters = entity.DistanceMeters,
            Steps = entity.Steps,
            WorkoutId = entity.WorkoutId,
            UpdatedAt = entity.UpdatedAt
        };

        public static Activity ToEntity(this ActivityDto dto) => new()
        {
            Id = dto.Id,
            PublicId = dto.PublicId == Guid.Empty ? Guid.NewGuid() : dto.PublicId,
            ExternalId = dto.ExternalId,
            Source = ParseSource(dto.Source),
            ActivityType = dto.ActivityType,
            Title = dto.Title,
            StartTime = EnsureUtc(dto.StartTime),
            EndTime = EnsureUtc(dto.EndTime),
            AverageHeartRate = dto.AverageHeartRate,
            MaxHeartRate = dto.MaxHeartRate,
            MinHeartRate = dto.MinHeartRate,
            ActiveCalories = dto.ActiveCalories,
            TotalCalories = dto.TotalCalories,
            DistanceMeters = dto.DistanceMeters,
            Steps = dto.Steps
            // WorkoutId is deliberately not taken from the DTO. Linking is an
            // explicit action with its own endpoint, so an import cannot silently
            // re-attach or detach an activity.
        };

        public static Activity UpdateFrom(this Activity entity, ActivityDto dto)
        {
            entity.ExternalId = dto.ExternalId;
            entity.Source = ParseSource(dto.Source);
            entity.ActivityType = dto.ActivityType;
            entity.Title = dto.Title;
            entity.StartTime = EnsureUtc(dto.StartTime);
            entity.EndTime = EnsureUtc(dto.EndTime);
            entity.AverageHeartRate = dto.AverageHeartRate;
            entity.MaxHeartRate = dto.MaxHeartRate;
            entity.MinHeartRate = dto.MinHeartRate;
            entity.ActiveCalories = dto.ActiveCalories;
            entity.TotalCalories = dto.TotalCalories;
            entity.DistanceMeters = dto.DistanceMeters;
            entity.Steps = dto.Steps;
            return entity;
        }
    }
}
