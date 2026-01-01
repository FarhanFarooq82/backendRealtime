# Use the official .NET 8 runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8000

# Use the .NET SDK image to build the app
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj files and restore dependencies
COPY ["A3ITranslator.API/A3ITranslator.API.csproj", "A3ITranslator.API/"]
COPY ["A3ITranslator.Application/A3ITranslator.Application.csproj", "A3ITranslator.Application/"]
COPY ["A3ITranslator.Infrastructure/A3ITranslator.Infrastructure.csproj", "A3ITranslator.Infrastructure/"]

RUN dotnet restore "A3ITranslator.API/A3ITranslator.API.csproj"

# Copy the entire source code
COPY . .

# Build and publish the application
WORKDIR "/src/A3ITranslator.API"
RUN dotnet build "A3ITranslator.API.csproj" -c Release -o /app/build
RUN dotnet publish "A3ITranslator.API.csproj" -c Release -o /app/publish

# Final stage: runtime image
FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .

# Set environment variables for ASP.NET Core
ENV ASPNETCORE_URLS=http://0.0.0.0:8000
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "A3ITranslator.API.dll"]
