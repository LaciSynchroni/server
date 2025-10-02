FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build-common
WORKDIR /src

COPY --link Directory.*.props .
COPY --link Common/*.csproj Common/
RUN dotnet restore Common

COPY --link Common Common/
RUN dotnet build Common \
        --no-restore \
        -c Release


FROM build-common AS build-shared

COPY --link Shared/*.csproj Shared/
RUN dotnet restore Shared

COPY --link Shared Shared/
RUN dotnet build Shared \
        --no-restore \
        -c Release


FROM build-shared AS build-authservice
WORKDIR /src/AuthService/

COPY --link AuthService/*.csproj .
RUN dotnet restore

COPY --link AuthService .
RUN dotnet publish --no-restore -o /publish


FROM build-shared AS build-server
WORKDIR /src/Server/

COPY --link Server/*.csproj .
RUN dotnet restore

COPY --link Server .
RUN dotnet publish --no-restore -o /publish


FROM build-shared AS build-services
WORKDIR /src/Services/

COPY --link Services/*.csproj .
RUN dotnet restore

COPY --link Services .
RUN dotnet publish --no-restore -o /publish


FROM build-shared AS build-staticfilesserver
WORKDIR /src/StaticFilesServer/

COPY --link StaticFilesServer/*.csproj .
RUN dotnet restore

COPY --link StaticFilesServer .
RUN dotnet publish --no-restore -o /publish


FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
LABEL org.opencontainers.image.source="https://github.com/LaciSynchroni/server" \
      org.opencontainers.image.licenses="AGPL-3.0-only"


FROM runtime AS authservice
WORKDIR /opt/LaciSynchroni/AuthService/
COPY --link --from=build-authservice /publish .
USER ${APP_UID}:${APP_UID}
ENTRYPOINT ["./LaciSynchroni.AuthService"]


FROM runtime AS server
WORKDIR /opt/LaciSynchroni/Server/
COPY --link --from=build-server /publish .
USER ${APP_UID}:${APP_UID}
ENTRYPOINT ["./LaciSynchroni.Server"]


FROM runtime AS services
WORKDIR /opt/LaciSynchroni/Services/
COPY --link --from=build-services /publish .
USER ${APP_UID}:${APP_UID}
ENTRYPOINT ["./LaciSynchroni.Services"]


FROM runtime AS staticfilesserver
WORKDIR /opt/LaciSynchroni/StaticFilesServer/
COPY --link --from=build-staticfilesserver /publish .

RUN mkdir ./data && chown -R ${APP_UID}:${APP_UID} ./data 
VOLUME ["/opt/LaciSynchroni/StaticFilesServer/data"]

USER ${APP_UID}:${APP_UID}
ENTRYPOINT ["./LaciSynchroni.StaticFilesServer"]
