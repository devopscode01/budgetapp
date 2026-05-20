# Build and run the Budget MVC app (ASP.NET Core 9).
# Build context should be the BudgetApp directory (see docker-compose.yml).

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY BudgetApp.csproj .
RUN dotnet restore BudgetApp.csproj
COPY . .
RUN dotnet publish BudgetApp.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "BudgetApp.dll"]
