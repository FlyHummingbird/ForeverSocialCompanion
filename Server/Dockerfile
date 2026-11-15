FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Shared/ Shared/
COPY Server/ Server/
RUN dotnet publish Server/Server.csproj -c Release -o /out --nologo -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out/ ./
ENV ASPNETCORE_URLS=http://+:8080 FS_DATA=/data DOTNET_EnableDiagnostics=0
USER 1654
EXPOSE 8080
ENTRYPOINT ["dotnet", "Server.dll"]
