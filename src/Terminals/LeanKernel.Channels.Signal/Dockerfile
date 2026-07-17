# Dockerfile - LeanKernel Signal Channel Terminal

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/Directory.Build.props src/
COPY src/Common/LeanKernel.Core/LeanKernel.Core.csproj src/Common/LeanKernel.Core/
COPY src/Common/LeanKernel.Channels.Common/LeanKernel.Channels.Common.csproj src/Common/LeanKernel.Channels.Common/
COPY src/Common/LeanKernel.Data/LeanKernel.Data.csproj src/Common/LeanKernel.Data/
COPY src/Terminals/LeanKernel.Channels.Signal/LeanKernel.Channels.Signal.csproj src/Terminals/LeanKernel.Channels.Signal/

RUN dotnet restore src/Terminals/LeanKernel.Channels.Signal/LeanKernel.Channels.Signal.csproj

COPY src/Terminals/LeanKernel.Channels.Signal/ src/Terminals/LeanKernel.Channels.Signal/
COPY src/Common/LeanKernel.Channels.Common/ src/Common/LeanKernel.Channels.Common/
COPY src/Common/LeanKernel.Core/ src/Common/LeanKernel.Core/
COPY src/Common/LeanKernel.Data/ src/Common/LeanKernel.Data/

RUN dotnet publish src/Terminals/LeanKernel.Channels.Signal/LeanKernel.Channels.Signal.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:3979
EXPOSE 3979

RUN useradd -m -s /bin/bash leankernel \
    && chown -R leankernel:leankernel /app

USER leankernel

ENTRYPOINT ["dotnet", "LeanKernel.Channels.Signal.dll"]
