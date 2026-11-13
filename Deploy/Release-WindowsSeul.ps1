# Lanceur de Build-Release.ps1 : clic droit > Executer avec PowerShell.
# Publie seulement Windows : installeur, Microsoft Store, GitHub Release et site.
# La fenetre reste ouverte a la fin pour lire le resultat.
& (Join-Path $PSScriptRoot "Build-Release.ps1") -SkipAndroid
