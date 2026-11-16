# Собирать из корня решения:  docker build -f EmailSenderService/Dockerfile -t email-sender:dev .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore отдельным слоем, чтобы правки кода не приводили к повторной загрузке пакетов.
COPY EmailSenderService/EmailSenderService.csproj EmailSenderService/
RUN dotnet restore EmailSenderService/EmailSenderService.csproj

COPY EmailSenderService/ EmailSenderService/
RUN dotnet publish EmailSenderService/EmailSenderService.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# В образе aspnet есть непривилегированный пользователь "app", а Kestrel по умолчанию слушает порт 8080.
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "EmailSenderService.dll"]
