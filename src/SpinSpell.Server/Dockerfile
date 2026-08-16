FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore against the project files alone so a source-only change reuses the layer.
COPY src/SpinSpell.Domain/SpinSpell.Domain.csproj src/SpinSpell.Domain/
COPY src/SpinSpell.Server/SpinSpell.Server.csproj src/SpinSpell.Server/
RUN dotnet restore src/SpinSpell.Server/SpinSpell.Server.csproj

COPY src/SpinSpell.Domain/ src/SpinSpell.Domain/
COPY src/SpinSpell.Server/ src/SpinSpell.Server/
RUN dotnet publish src/SpinSpell.Server/SpinSpell.Server.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

EXPOSE 8080
ENTRYPOINT ["dotnet", "SpinSpell.Server.dll"]
