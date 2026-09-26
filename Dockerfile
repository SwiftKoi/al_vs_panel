FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["AlegacyWebPanel.csproj", "."]
COPY ["Core/AlegacyWebPanel.Core.csproj", "Core/"]
COPY ["Modules/Users/AlegacyWebPanel.Users.csproj", "Modules/Users/"]
COPY ["Modules/Authentication/AlegacyWebPanel.Authentication.csproj", "Modules/Authentication/"]
COPY ["Modules/RemoteOperations/AlegacyWebPanel.RemoteOperations.csproj", "Modules/RemoteOperations/"]
COPY ["Modules/ServerManagement/AlegacyWebPanel.ServerManagement.csproj", "Modules/ServerManagement/"]
COPY ["Modules/FileManager/AlegacyWebPanel.FileManager.csproj", "Modules/FileManager/"]
COPY ["Modules/AutomationApi/AlegacyWebPanel.AutomationApi.csproj", "Modules/AutomationApi/"]
COPY ["Modules/Logging/AlegacyWebPanel.Logging.csproj", "Modules/Logging/"]
RUN dotnet restore

COPY . .
RUN dotnet publish "AlegacyWebPanel.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS test
WORKDIR /src
COPY . .
CMD ["bash", "/src/scripts/run-tests-in-container.sh"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime-base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

USER root
RUN apt-get update \
    && apt-get install --no-install-recommends --yes \
        docker.io \
        docker-compose-v2 \
        python3 \
    && rm -rf /var/lib/apt/lists/*

RUN mkdir -p /var/lib/alegacy/data /var/lib/alegacy/keys \
    && chown -R $APP_UID:$APP_UID /var/lib/alegacy

FROM runtime-base AS runtime
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "AlegacyWebPanel.dll"]

FROM runtime-base AS development
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "AlegacyWebPanel.dll"]

