# Build from the solution root:  docker build -f EmailSenderService/Dockerfile -t email-sender:dev .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore as its own layer so code edits do not re-download packages.
COPY EmailSenderService/EmailSenderService.csproj EmailSenderService/
RUN dotnet restore EmailSenderService/EmailSenderService.csproj

COPY EmailSenderService/ EmailSenderService/
RUN dotnet publish EmailSenderService/EmailSenderService.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# The aspnet image ships a non-root "app" user and defaults Kestrel to port 8080.
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "EmailSenderService.dll"]
