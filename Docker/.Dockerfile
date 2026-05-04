# Reusable build/publish image for any project in the solution.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT_PATH
WORKDIR /src

COPY . .
RUN dotnet restore "$PROJECT_PATH"
RUN dotnet publish "$PROJECT_PATH" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet"]

