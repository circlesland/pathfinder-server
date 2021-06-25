FROM mcr.microsoft.com/dotnet/runtime:5.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build
ENV DOTNET_EnableDiagnostics=0
WORKDIR /src
COPY . .
RUN dotnet restore "Pathfinder.Server.csproj"
RUN dotnet build "Pathfinder.Server.csproj" -c Release -o /app/build

FROM build AS publish
ENV DOTNET_EnableDiagnostics=0
RUN dotnet publish "Pathfinder.Server.csproj" -c Release -o /app/publish

FROM base AS final
LABEL org.opencontainers.image.source=https://github.com/circlesland/pathfinder-server
ENV DOTNET_EnableDiagnostics=0
WORKDIR /app
EXPOSE 7891
COPY --from=publish /app/publish .
RUN chmod +x ./pathfinder
ENTRYPOINT ["dotnet", "Pathfinder.Server.dll"]
