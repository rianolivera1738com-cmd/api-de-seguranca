FROM rust:1-bookworm AS native
WORKDIR /src
COPY native/security-core/ ./
RUN cargo build --release --locked

FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src
COPY global.json ./
COPY api/SecurityApi.csproj api/
RUN dotnet restore api/SecurityApi.csproj
COPY api/ api/
RUN dotnet publish api/SecurityApi.csproj -c Release --no-restore -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble
WORKDIR /app
COPY --from=build /out/ ./
COPY --from=native /src/target/release/libsecurity_core.so ./
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Database__Path=/data/security.db
RUN mkdir /data && chown app:app /data
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "SecurityApi.dll"]
