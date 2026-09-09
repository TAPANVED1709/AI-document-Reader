FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY apps/api/AI.DocumentReader.Api.csproj apps/api/
RUN dotnet restore apps/api/AI.DocumentReader.Api.csproj
COPY apps/api apps/api
RUN dotnet publish apps/api/AI.DocumentReader.Api.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /out .
EXPOSE 8080
ENTRYPOINT ["dotnet", "AI.DocumentReader.Api.dll"]
