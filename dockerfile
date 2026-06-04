# ---- Build Stage ----
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# 複製並還原套件
COPY *.csproj ./
RUN dotnet restore

# 複製所有檔案並發布
COPY . .
RUN dotnet publish -c Release -o /app/publish

# ---- Runtime Stage ----
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# 複製發布結果
COPY --from=build /app/publish .

# Render 會動態指定 PORT，這裡設定監聽
ENV ASPNETCORE_URLS=http://+:${PORT:-8080}

EXPOSE 8080

ENTRYPOINT ["dotnet", "FibonacciMVC.dll"]
