# ============================================================
#  Wasnie dev helper — levanta/apaga API + Stripe listen
#  Comandos:  api run   |  api stop   |  api status
#
#  Carga automática: dot-sourced desde $PROFILE.
#  Para recargar manualmente: . "$env:USERPROFILE\Documents\Sales\Wasnie\wasnie-dev.ps1"
# ============================================================

$global:WasnieApiPath   = "C:\Users\fillo\Documents\Sales\Wasnie\WasnieApi\src\Wasnie.Api"
$global:WasnieStripeFwd = "http://localhost:5091/api/subscription/webhook"

function api {
    param([Parameter(Position=0)][string]$cmd = "")

    switch ($cmd.ToLower()) {

        "run" {
            Write-Host "Levantando Wasnie API + Stripe listen..." -ForegroundColor Cyan

            # Ventana 1 — API (dotnet watch run)
            Start-Process powershell -ArgumentList @(
                "-NoExit",
                "-Command",
                "`$host.UI.RawUI.WindowTitle='WASNIE API'; cd '$global:WasnieApiPath'; dotnet watch run"
            )

            # Ventana 2 — Stripe listen
            Start-Process powershell -ArgumentList @(
                "-NoExit",
                "-Command",
                "`$host.UI.RawUI.WindowTitle='WASNIE STRIPE'; stripe listen --forward-to $global:WasnieStripeFwd"
            )

            Write-Host "Listo. Dos ventanas abiertas: 'WASNIE API' y 'WASNIE STRIPE'." -ForegroundColor Green
            Write-Host "Usa 'api stop' para apagar ambas." -ForegroundColor DarkGray
        }

        "stop" {
            Write-Host "Apagando Wasnie API + Stripe listen..." -ForegroundColor Yellow

            $killed = 0
            Get-Process -Name "dotnet" -ErrorAction SilentlyContinue | ForEach-Object {
                try { $_.Kill(); $killed++ } catch {}
            }
            Get-Process -Name "stripe" -ErrorAction SilentlyContinue | ForEach-Object {
                try { $_.Kill(); $killed++ } catch {}
            }

            # Cierra las ventanas PowerShell con título de Wasnie
            Get-Process -Name "powershell","pwsh" -ErrorAction SilentlyContinue | Where-Object {
                $_.MainWindowTitle -like "WASNIE *"
            } | ForEach-Object {
                try { $_.CloseMainWindow() | Out-Null; $killed++ } catch {}
            }

            Write-Host "Apagado. ($killed procesos/ventanas cerrados)" -ForegroundColor Green
            Write-Host "NOTA: 'api stop' mata TODOS los procesos dotnet. Si tienes otro proyecto .NET corriendo, ciérralo a mano." -ForegroundColor DarkYellow
        }

        "status" {
            $api    = Get-Process -Name "dotnet" -ErrorAction SilentlyContinue
            $stripe = Get-Process -Name "stripe" -ErrorAction SilentlyContinue
            Write-Host "Estado Wasnie dev:" -ForegroundColor Cyan
            Write-Host ("  API (dotnet):   " + $(if ($api)    { "CORRIENDO ($($api.Count) proceso/s)" } else { "apagado" })) -ForegroundColor $(if ($api)    { "Green" } else { "DarkGray" })
            Write-Host ("  Stripe listen:  " + $(if ($stripe) { "CORRIENDO" }                          else { "apagado" })) -ForegroundColor $(if ($stripe) { "Green" } else { "DarkGray" })
        }

        default {
            Write-Host "Comandos disponibles:" -ForegroundColor Cyan
            Write-Host "  api run     " -NoNewline -ForegroundColor White; Write-Host "Levanta API (dotnet watch run) + Stripe listen en dos ventanas"
            Write-Host "  api stop    " -NoNewline -ForegroundColor White; Write-Host "Apaga ambos"
            Write-Host "  api status  " -NoNewline -ForegroundColor White; Write-Host "Muestra si estan corriendo"
        }
    }
}
