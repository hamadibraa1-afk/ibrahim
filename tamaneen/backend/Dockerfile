# Builds the API, the crisis worker and a migration bundle from one source tree.
# Build context: the repository's tamaneen/ folder (see docker-compose.yml).
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props Tamaneen.sln .editorconfig ./
COPY .config .config
COPY backend backend
RUN dotnet tool restore
RUN dotnet publish backend/src/Tamaneen.Api -c Release -o /out/api /p:UseAppHost=false
RUN dotnet publish backend/src/Tamaneen.Crisis.Worker -c Release -o /out/worker /p:UseAppHost=false
ENV TAMANEEN_SQL="Server=localhost;Database=x;TrustServerCertificate=True"
RUN dotnet ef migrations bundle -p backend/src/Tamaneen.Infrastructure -o /out/migrate/efbundle

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS api
WORKDIR /app
COPY --from=build /out/api .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Tamaneen.Api.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS worker
WORKDIR /app
COPY --from=build /out/worker .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Tamaneen.Crisis.Worker.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS migrate
WORKDIR /app
COPY --from=build /out/migrate .
ENTRYPOINT ["/bin/sh", "-c", "./efbundle --connection \"$TAMANEEN_SQL\""]
