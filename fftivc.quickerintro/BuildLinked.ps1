# Set Working Directory
Split-Path $MyInvocation.MyCommand.Path | Push-Location
[Environment]::CurrentDirectory = $PWD

Remove-Item "$env:RELOADEDIIMODS/fftivc.quickerintro/*" -Force -Recurse
dotnet publish "./fftivc.quickerintro.csproj" -c Release -o "$env:RELOADEDIIMODS/fftivc.quickerintro" /p:OutputPath="./bin/Release" /p:ReloadedILLink="true"

# Restore Working Directory
Pop-Location