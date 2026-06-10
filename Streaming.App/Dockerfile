FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Streaming.App/Streaming.App.csproj Streaming.App/
RUN dotnet restore Streaming.App/Streaming.App.csproj
COPY Streaming.App/ Streaming.App/
RUN dotnet publish Streaming.App/Streaming.App.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Streaming.App.dll"]
