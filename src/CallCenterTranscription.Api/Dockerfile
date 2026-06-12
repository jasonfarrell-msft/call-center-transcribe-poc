FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["src/CallCenterTranscription.Api/CallCenterTranscription.Api.csproj", "src/CallCenterTranscription.Api/"]
COPY ["src/CallCenterTranscription.Shared/CallCenterTranscription.Shared.csproj", "src/CallCenterTranscription.Shared/"]
COPY ["src/CallCenterTranscription.Ai/CallCenterTranscription.Ai.csproj", "src/CallCenterTranscription.Ai/"]
COPY ["src/CallCenterTranscription.Telephony/CallCenterTranscription.Telephony.csproj", "src/CallCenterTranscription.Telephony/"]
RUN dotnet restore "src/CallCenterTranscription.Api/CallCenterTranscription.Api.csproj"

COPY src/ src/
COPY samples/ samples/
RUN dotnet publish "src/CallCenterTranscription.Api/CallCenterTranscription.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
USER $APP_UID

ENTRYPOINT ["dotnet", "CallCenterTranscription.Api.dll"]
