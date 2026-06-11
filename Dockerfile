FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["Diplom/Diplom.csproj", "Diplom/"]
RUN dotnet restore "Diplom/Diplom.csproj"
COPY . .
WORKDIR "/src/Diplom"
RUN dotnet publish "Diplom.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
RUN apt-get update && apt-get install -y fonts-liberation && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
EXPOSE 7777
ENTRYPOINT ["dotnet", "Diplom.dll"]
