# ---------- BUILD STAGE ----------
FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src

# Copy solution and project files
COPY *.sln ./
COPY AF_mobile_web_api/AF_mobile_web_api.csproj AF_mobile_web_api/
COPY ApplicationDatabase/ApplicationDatabase.csproj ApplicationDatabase/

# Restore dependencies
RUN dotnet restore

# Copy the rest of the source code
COPY . .

# Build and publish the startup project
WORKDIR /src/AF_mobile_web_api
RUN dotnet publish -c Release -o /app/publish

# ---------- RUNTIME STAGE ----------
FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS runtime
WORKDIR /app

# Copy published output from build stage
COPY --from=build /app/publish .

# Expose port (Render sets $PORT dynamically)
ENV ASPNETCORE_URLS=http://+:$PORT

ENTRYPOINT ["dotnet", "AF_mobile_web_api.dll"]
