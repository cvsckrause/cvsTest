# Kept so older instructions still work. The deploy no longer uses OIDC; see bootstrap-github-deploy.ps1.
& (Join-Path $PSScriptRoot "bootstrap-github-deploy.ps1")
exit $LASTEXITCODE
