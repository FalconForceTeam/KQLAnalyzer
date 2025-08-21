# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /app

# Copy csproj and restore dependencies
COPY src/KQLAnalyzer.csproj ./src/
RUN dotnet restore ./src/KQLAnalyzer.csproj

# Copy everything and build
COPY . .
RUN dotnet publish ./src/KQLAnalyzer.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .

# Copy the required JSON files to the parent directory where the app expects them
COPY environments.json /environments.json
COPY additional_columns.json /additional_columns.json

# Expose port 8000 (default port from README)
EXPOSE 8000

# Run the REST API service with explicit environments file path
ENTRYPOINT ["dotnet", "KQLAnalyzer.dll", "--rest", "--bind-address=http://0.0.0.0:8000", "--environments-file=/environments.json"]