# Lanceur de Build-Release.ps1 : clic droit > Executer avec PowerShell.
# Publie la release complete (Windows + Android) et remplace les images des fiches Google Play et
# Microsoft Store par celles de StoreListing\output\ (a regenerer avant avec StoreListing\Build-StoreListing.ps1).
# La fenetre reste ouverte a la fin pour lire le resultat.
& (Join-Path $PSScriptRoot "Build-Release.ps1") -UploadListingImages
