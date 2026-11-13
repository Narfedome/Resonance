# Lanceur de Build-Release.ps1 : clic droit > Executer avec PowerShell.
# Publie seulement Android sur Google Play (ni GitHub Release ni site).
# La fenetre reste ouverte a la fin pour lire le resultat.
& (Join-Path $PSScriptRoot "Build-Release.ps1") -SkipWindows
