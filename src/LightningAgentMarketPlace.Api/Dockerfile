FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# Copy solution and project files
COPY LightningAgent.sln .
COPY Directory.Build.props .
COPY src/LightningAgent.Core/LightningAgent.Core.csproj src/LightningAgent.Core/
COPY src/LightningAgent.Data/LightningAgent.Data.csproj src/LightningAgent.Data/
COPY src/LightningAgent.Lightning/LightningAgent.Lightning.csproj src/LightningAgent.Lightning/
COPY src/LightningAgent.Chainlink/LightningAgent.Chainlink.csproj src/LightningAgent.Chainlink/
COPY src/LightningAgent.Acp/LightningAgent.Acp.csproj src/LightningAgent.Acp/
COPY src/LightningAgent.AI/LightningAgent.AI.csproj src/LightningAgent.AI/
COPY src/LightningAgent.Verification/LightningAgent.Verification.csproj src/LightningAgent.Verification/
COPY src/LightningAgent.Engine/LightningAgent.Engine.csproj src/LightningAgent.Engine/
COPY src/LightningAgent.Api/LightningAgent.Api.csproj src/LightningAgent.Api/

# Restore
RUN dotnet restore src/LightningAgent.Api/LightningAgent.Api.csproj

# Copy source and build
COPY src/ src/
RUN dotnet publish src/LightningAgent.Api/LightningAgent.Api.csproj -c Release -o /app --no-restore

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS runtime
WORKDIR /app
COPY --from=build /app .

# Create data directory for SQLite
RUN mkdir -p /app/data
ENV ConnectionStrings__Sqlite="Data Source=/app/data/lightningagent.db;Cache=Shared"

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "LightningAgent.Api.dll"]
