# Dockerfile — LeanKernel Engine
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files for restore
COPY src/LeanKernel.sln .
COPY src/Directory.Build.props .
COPY src/LeanKernel.Abstractions/LeanKernel.Abstractions.csproj LeanKernel.Abstractions/
COPY src/LeanKernel.Agents/LeanKernel.Agents.csproj LeanKernel.Agents/
COPY src/LeanKernel.Context/LeanKernel.Context.csproj LeanKernel.Context/
COPY src/LeanKernel.Knowledge/LeanKernel.Knowledge.csproj LeanKernel.Knowledge/
COPY src/LeanKernel.Tools/LeanKernel.Tools.csproj LeanKernel.Tools/
COPY src/LeanKernel.Persistence/LeanKernel.Persistence.csproj LeanKernel.Persistence/
COPY src/LeanKernel.Diagnostics/LeanKernel.Diagnostics.csproj LeanKernel.Diagnostics/
COPY src/LeanKernel.Gateway/LeanKernel.Gateway.csproj LeanKernel.Gateway/
COPY src/LeanKernel.Tests.Unit/LeanKernel.Tests.Unit.csproj LeanKernel.Tests.Unit/
COPY src/LeanKernel.Tests.Integration/LeanKernel.Tests.Integration.csproj LeanKernel.Tests.Integration/

RUN dotnet restore LeanKernel.sln

# Copy source and build
COPY src/ .
RUN dotnet publish LeanKernel.Gateway/LeanKernel.Gateway.csproj -c Release -o /app/publish --no-restore

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install curl for health checks
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

RUN useradd -m -s /bin/bash leankernel && chown -R leankernel:leankernel /app
USER leankernel

ENV ASPNETCORE_URLS=http://+:5080
EXPOSE 5080

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 CMD curl -f http://localhost:5080/api/health || exit 1

ENTRYPOINT ["dotnet", "LeanKernel.Gateway.dll"]
