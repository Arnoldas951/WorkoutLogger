# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# copy csproj and restore to leverage layer caching
COPY ["WorkoutLogger.csproj", "./"]
RUN dotnet restore "WorkoutLogger.csproj"

# copy everything and publish
COPY . .
RUN dotnet publish "WorkoutLogger.csproj" -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# copy published app
COPY --from=build /app/publish ./

# copy entrypoint script
COPY docker-entrypoint.sh /app/docker-entrypoint.sh
RUN chmod +x /app/docker-entrypoint.sh

# make Kestrel listen on port 80 inside the container
ENV ASPNETCORE_URLS=http://+:80

# default command: run the entrypoint which starts the app
ENTRYPOINT ["/app/docker-entrypoint.sh"]
