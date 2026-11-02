FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
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
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "PennyPath.Api.dll"]
