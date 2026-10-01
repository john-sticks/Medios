# Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

COPY src/Medios/Medios.csproj src/Medios/
RUN dotnet restore src/Medios/Medios.csproj

COPY . .
RUN dotnet publish src/Medios/Medios.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Copiar XMLs de permisos y bower_components (excluidos por dotnet publish en SDK web)
RUN mkdir -p /app/publish/Config/Permisos && \
    cp src/Medios/Config/Permisos/*.xml /app/publish/Config/Permisos/ && \
    cp -r src/Medios/wwwroot/adminlte/bower_components /app/publish/wwwroot/adminlte/bower_components

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0

ENV TZ=America/Argentina/Buenos_Aires
RUN ln -snf /usr/share/zoneinfo/$TZ /etc/localtime && echo $TZ > /etc/timezone

WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8009

EXPOSE 8009

ENTRYPOINT ["dotnet", "Medios.dll"]
