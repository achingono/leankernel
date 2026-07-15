# Dockerfile - LeanKernel Teams Channel Terminal

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/Directory.Build.props src/
COPY src/Common/LeanKernel.Core/LeanKernel.Core.csproj src/Common/LeanKernel.Core/
COPY src/Common/LeanKernel.Data/LeanKernel.Data.csproj src/Common/LeanKernel.Data/
COPY src/Terminals/LeanKernel.Channels.Teams/LeanKernel.Channels.Teams.csproj src/Terminals/LeanKernel.Channels.Teams/

RUN dotnet restore src/Terminals/LeanKernel.Channels.Teams/LeanKernel.Channels.Teams.csproj

COPY src/Terminals/LeanKernel.Channels.Teams/ src/Terminals/LeanKernel.Channels.Teams/
COPY src/Common/LeanKernel.Core/ src/Common/LeanKernel.Core/
COPY src/Common/LeanKernel.Data/ src/Common/LeanKernel.Data/

RUN dotnet publish src/Terminals/LeanKernel.Channels.Teams/LeanKernel.Channels.Teams.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

RUN useradd -m -s /bin/bash leankernel \
    && chown -R leankernel:leankernel /app

USER leankernel

ENV ASPNETCORE_URLS=http://+:3978
EXPOSE 3978

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 CMD curl -f http://localhost:3978/health || exit 1

ENTRYPOINT ["dotnet", "LeanKernel.Channels.Teams.dll"]
