# ============================================================
#  Wasnie dev helper — levanta/apaga API + Stripe listen + UI
#  Comandos:  run app   |  run stop   |  run status
#
#  `api run|stop|status` sigue funcionando como alias del anterior.
#
#  Carga automática: dot-sourced desde $PROFILE.
#  Para recargar manualmente: . "$env:USERPROFILE\Documents\Sales\Wasnie\wasnie-dev.ps1"
# ============================================================

$global:WasnieApiPath   = "C:\Users\fillo\Documents\Sales\Wasnie\WasnieApi\src\Wasnie.Api"
$global:WasnieUiPath    = "C:\Users\fillo\Documents\Sales\Wasnie\WasnieUi"
$global:WasnieStripeFwd = "http://localhost:5091/api/subscription/webhook"

# Abre una ventana PowerShell con título y un comando dentro. El título es lo que
# `run stop` usa para encontrarla después, así que siempre empieza por "WASNIE ".
function Start-WasnieWindow {
    param(
        [Parameter(Mandatory)][string]$Title,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string]$Command
    )

    Start-Process powershell -ArgumentList @(
        "-NoExit",
        "-Command",
        "`$host.UI.RawUI.WindowTitle='$Title'; cd '$WorkingDirectory'; $Command"
    )
}

# Los procesos node que son ESTE proyecto, y no cualquier node de la máquina.
# Se filtran por la ruta de WasnieUi en su línea de comandos: `run stop` mata todos
# los dotnet (ya avisaba de eso), pero matar todos los node se llevaría por delante
# cualquier otra cosa abierta, así que aquí sí se afina.
function Get-WasnieUiProcess {
    Get-CimInstance Win32_Process -Filter "Name='node.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -like "*WasnieUi*" }
}

function run {
    param([Parameter(Position=0)][string]$cmd = "")

    switch ($cmd.ToLower()) {

        "app" {
            Write-Host "Levantando Wasnie: API + Stripe listen + UI..." -ForegroundColor Cyan

            # Ventana 1 — API (dotnet watch run)
            Start-WasnieWindow -Title "WASNIE API" -WorkingDirectory $global:WasnieApiPath `
                -Command "dotnet watch run"

            # Ventana 2 — Stripe listen
            Start-WasnieWindow -Title "WASNIE STRIPE" -WorkingDirectory $global:WasnieApiPath `
                -Command "stripe listen --forward-to $global:WasnieStripeFwd"

            # Ventana 3 — UI. `-o` abre el navegador solo; el proxy a la API lo pone
            # angular.json (proxy.conf.json), así que no hace falta pasar nada más.
            Start-WasnieWindow -Title "WASNIE UI" -WorkingDirectory $global:WasnieUiPath `
                -Command "npx ng serve -o"

            Write-Host "Listo. Tres ventanas: 'WASNIE API', 'WASNIE STRIPE' y 'WASNIE UI'." -ForegroundColor Green
            Write-Host "La UI tarda unos segundos en compilar antes de abrir el navegador (http://localhost:4200)." -ForegroundColor DarkGray
            Write-Host "Usa 'run stop' para apagar todo." -ForegroundColor DarkGray
        }

        "stop" {
            Write-Host "Apagando Wasnie: API + Stripe listen + UI..." -ForegroundColor Yellow

            $killed = 0
            Get-Process -Name "dotnet" -ErrorAction SilentlyContinue | ForEach-Object {
                try { $_.Kill(); $killed++ } catch {}
            }
            Get-Process -Name "stripe" -ErrorAction SilentlyContinue | ForEach-Object {
                try { $_.Kill(); $killed++ } catch {}
            }
            Get-WasnieUiProcess | ForEach-Object {
                try { Stop-Process -Id $_.ProcessId -Force -ErrorAction Stop; $killed++ } catch {}
            }

            # Cierra las ventanas PowerShell con título de Wasnie
            Get-Process -Name "powershell","pwsh" -ErrorAction SilentlyContinue | Where-Object {
                $_.MainWindowTitle -like "WASNIE *"
            } | ForEach-Object {
                try { $_.CloseMainWindow() | Out-Null; $killed++ } catch {}
            }

            Write-Host "Apagado. ($killed procesos/ventanas cerrados)" -ForegroundColor Green
            Write-Host "NOTA: 'run stop' mata TODOS los procesos dotnet. Si tienes otro proyecto .NET corriendo, cierralo a mano." -ForegroundColor DarkYellow
            Write-Host "      Los node SI se filtran por ruta, asi que otros proyectos JS no se tocan." -ForegroundColor DarkGray
        }

        "status" {
            $api    = Get-Process -Name "dotnet" -ErrorAction SilentlyContinue
            $stripe = Get-Process -Name "stripe" -ErrorAction SilentlyContinue
            $ui     = Get-WasnieUiProcess
            Write-Host "Estado Wasnie dev:" -ForegroundColor Cyan
            Write-Host ("  API (dotnet):   " + $(if ($api)    { "CORRIENDO ($(@($api).Count) proceso/s)" } else { "apagado" })) -ForegroundColor $(if ($api)    { "Green" } else { "DarkGray" })
            Write-Host ("  Stripe listen:  " + $(if ($stripe) { "CORRIENDO" }                             else { "apagado" })) -ForegroundColor $(if ($stripe) { "Green" } else { "DarkGray" })
            Write-Host ("  UI (ng serve):  " + $(if ($ui)     { "CORRIENDO ($(@($ui).Count) proceso/s)" }  else { "apagado" })) -ForegroundColor $(if ($ui)     { "Green" } else { "DarkGray" })
        }

        default {
            Write-Host "Comandos disponibles:" -ForegroundColor Cyan
            Write-Host "  run app     " -NoNewline -ForegroundColor White; Write-Host "Levanta API (dotnet watch run) + Stripe listen + UI (ng serve -o), una ventana cada uno"
            Write-Host "  run stop    " -NoNewline -ForegroundColor White; Write-Host "Apaga los tres"
            Write-Host "  run status  " -NoNewline -ForegroundColor White; Write-Host "Muestra cuales estan corriendo"
            Write-Host ""
            Write-Host "  (alias) 'api run|stop|status' sigue funcionando y hace lo mismo." -ForegroundColor DarkGray
        }
    }
}

# Alias hacia atras: `api run` era el comando de siempre y esta en la memoria de los
# dedos. Se mantiene, y ahora levanta tambien la UI — es el mismo `run app`.
function api {
    param([Parameter(Position=0)][string]$cmd = "")

    switch ($cmd.ToLower()) {
        "run"    { run app }
        "stop"   { run stop }
        "status" { run status }
        default  { run }
    }
}
