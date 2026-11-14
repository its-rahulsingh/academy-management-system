@echo off
setlocal
cd /d "%~dp0"
echo.
echo === AcademyFlow EF Core migration repair ===
echo Uses the official EF Core 10 tool to generate the module migration from AcademyDbContext.
echo This does not drop or recreate your PostgreSQL database.
echo.
where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERROR: dotnet SDK is missing from PATH.
    exit /b 1
)
if exist "server\Academy.Api\Data\Migrations\*_AcademyModules.cs" (
    echo AcademyModules migration already present. Checking for model changes...
    goto validate
)
rem Do not trust a handcrafted model snapshot: generate from the actual runtime model.
dotnet ef --version >nul 2>&1
if errorlevel 1 (
    echo dotnet-ef missing. Installing matching EF Core 10 tool from NuGet...
    dotnet tool install --global dotnet-ef --version 10.0.13
    if errorlevel 1 (
        echo ERROR: Tool install failed. Check access to api.nuget.org.
        exit /b 1
    )
)
echo.
echo Generating official migration and model snapshot...
dotnet ef migrations add AcademyModules --project "server\Academy.Api\Academy.Api.csproj" --startup-project "server\Academy.Api\Academy.Api.csproj" --output-dir "Data\Migrations"
if errorlevel 1 (
    echo ERROR: Migration generation failed. No database reset was attempted.
    exit /b 1
)
:validate
echo.
echo Checking that the EF model matches the migration snapshot...
dotnet ef migrations has-pending-model-changes --project "server\Academy.Api\Academy.Api.csproj" --startup-project "server\Academy.Api\Academy.Api.csproj"
if errorlevel 1 (
    echo ERROR: EF model is still out of sync. Do not deploy this project.
    exit /b 1
)
echo.
echo SUCCESS: Model and migrations are in sync. Now run the backend in this folder.
exit /b 0
