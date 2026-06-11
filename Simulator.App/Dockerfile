FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Simulator.App/Simulator.App.csproj Simulator.App/
RUN dotnet restore Simulator.App/Simulator.App.csproj
COPY Simulator.App/ Simulator.App/
RUN dotnet publish Simulator.App/Simulator.App.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
RUN apt-get update && \
    apt-get install -y --no-install-recommends tzdata && \
    rm -rf /var/lib/apt/lists/*
ENV TZ=Europe/Belgrade
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Simulator.App.dll"]
