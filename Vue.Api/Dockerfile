FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Vue.Api/Vue.Api.csproj Vue.Api/
RUN dotnet restore Vue.Api/Vue.Api.csproj
COPY Vue.Api/ Vue.Api/
RUN dotnet publish Vue.Api/Vue.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
RUN apt-get update && \
    apt-get install -y --no-install-recommends tzdata && \
    rm -rf /var/lib/apt/lists/*
ENV TZ=Europe/Belgrade
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:5099
EXPOSE 5099
ENTRYPOINT ["dotnet", "Vue.Api.dll"]
