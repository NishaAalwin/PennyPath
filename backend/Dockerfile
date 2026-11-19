FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src
COPY PennyPath.sln ./
COPY src/ src/
COPY tests/ tests/
RUN dotnet restore PennyPath.sln
RUN dotnet publish src/PennyPath.Api/PennyPath.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
# Writable by the unprivileged user: the compose volume for the Data Protection key ring is mounted here.
RUN mkdir -p /var/dpkeys && chown $APP_UID /var/dpkeys
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "PennyPath.Api.dll"]
