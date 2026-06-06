# ---- Build Stage ----
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# 複製 .csproj 並還原套件（注意路徑含子資料夾）
COPY FibonacciMVC/FibonacciMVC.csproj FibonacciMVC/
RUN dotnet restore FibonacciMVC/FibonacciMVC.csproj

# 複製所有檔案並發布
COPY . .
RUN dotnet publish FibonacciMVC/FibonacciMVC.csproj -c Release -o /app/publish

# ---- Runtime Stage ----
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:${PORT:-8080}

EXPOSE 8080

ENTRYPOINT ["dotnet", "FibonacciMVC.dll"]
