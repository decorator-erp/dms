# syntax=docker/dockerfile:1
# DEP-1: the production image of the PERA API. Build from the repository root:
#   docker build -f src/Api/Dockerfile -t pera-api .
# It also carries one EF Core migration bundle per DbContext (/app/migrations, run by /app/migrate.sh),
# so the same image both migrates the database and serves the API.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
# global.json pins the exact developer SDK; the container's own SDK is the one to build with.
RUN rm -f global.json
RUN dotnet tool install --global dotnet-ef --version 10.0.13
ENV PATH="${PATH}:/root/.dotnet/tools"

RUN dotnet publish src/Api/Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# One bundle per context, numbered in the order they must run (SharedDbContext holds the outbox/inbox
# every module writes, Identity before the modules that reference users).
RUN set -eu; mkdir -p /app/migrations; n=0; \
    for pair in \
      src/BuildingBlocks/BuildingBlocks.csproj:SharedDbContext \
      src/Modules/Identity/Identity.Infrastructure/Identity.Infrastructure.csproj:IdentityModuleDbContext \
      src/Modules/Hr/Hr.Infrastructure/Hr.Infrastructure.csproj:HrDbContext \
      src/Modules/Approvals/Approvals.Infrastructure/Approvals.Infrastructure.csproj:ApprovalsDbContext \
      src/Modules/Crm/Crm.Infrastructure/Crm.Infrastructure.csproj:CrmDbContext \
      src/Modules/Projects/Projects.Infrastructure/Projects.Infrastructure.csproj:ProjectsDbContext \
      src/Modules/Documents/Documents.Infrastructure/Documents.Infrastructure.csproj:DocumentsDbContext \
      src/Modules/Sites/Sites.Infrastructure/Sites.Infrastructure.csproj:SitesDbContext \
      src/Modules/Finance/Finance.Infrastructure/Finance.Infrastructure.csproj:FinanceDbContext \
      src/Modules/Notifications/Notifications.Infrastructure/Notifications.Infrastructure.csproj:NotificationsDbContext; \
    do \
      project="${pair%%:*}"; context="${pair##*:}"; n=$((n + 1)); \
      dotnet ef migrations bundle --project "$project" --startup-project src/Api/Api.csproj \
        --context "$context" --configuration Release --force \
        --output "/app/migrations/$(printf '%02d' "$n")-$context"; \
    done

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
COPY --from=build /app/migrations ./migrations
COPY deploy/migrate.sh ./migrate.sh
USER root
RUN chmod +x ./migrate.sh ./migrations/* && mkdir -p /app/uploads && chown -R app:app /app/uploads
USER app
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    FileStorage__RootPath=/app/uploads
EXPOSE 8080
VOLUME ["/app/uploads"]
ENTRYPOINT ["dotnet", "Api.dll"]
