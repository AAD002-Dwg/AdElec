@echo off
setlocal

:: ── AD-ELEC build script ─────────────────────────────────────────────────────
:: Compila la solución en Release y copia las DLLs a esta carpeta.
:: Si AutoCAD tiene los DLLs bloqueados, cerrar AutoCAD antes de ejecutar.
:: ─────────────────────────────────────────────────────────────────────────────

set SLN=%~dp0..\AdElec.sln
set DEPLOY=%~dp0

echo.
echo  AD-ELEC ^| Build Release
echo  Solucion : %SLN%
echo  Deploy   : %DEPLOY%
echo.

:: Verificar que dotnet esté disponible
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] dotnet no encontrado en PATH.
    echo         Instala .NET SDK desde https://dot.net
    pause & exit /b 1
)

:: Build
dotnet build "%SLN%" --configuration Release --nologo
if errorlevel 1 (
    echo.
    echo [ERROR] Build fallido. Revisa los errores de arriba.
    echo         Si el error es "file locked", cerrá AutoCAD y volvé a ejecutar.
    pause & exit /b 1
)

echo.
echo  Build exitoso. DLLs actualizadas en %DEPLOY%
echo.
pause
