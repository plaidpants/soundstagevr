# Builds the SoundStage native DSP plugin for Quest (see build_plugin.cmd).
param([string]$Ndk)
$ErrorActionPreference = 'Stop'
$unityVersion = '2022.3.62f3'
$ndkSuffix = "Editor\Data\PlaybackEngines\AndroidPlayer\NDK"

$candidates = @()
if ($Ndk) { $candidates += $Ndk }
if ($env:ANDROID_NDK_ROOT) { $candidates += $env:ANDROID_NDK_ROOT }
# Unity Hub's custom install location, if one is set
$hubJson = Join-Path $env:APPDATA 'UnityHub\secondaryInstallPath.json'
if (Test-Path $hubJson) {
  $hubRoot = (Get-Content $hubJson -Raw | ConvertFrom-Json)
  if ($hubRoot) { $candidates += (Join-Path (Join-Path $hubRoot $unityVersion) $ndkSuffix) }
}
$candidates += "C:\Program Files\Unity\Hub\Editor\$unityVersion\$ndkSuffix"
$candidates += "C:\Program Files\Unity $unityVersion\$ndkSuffix"

$ndkRoot = $null
foreach ($c in $candidates) {
  if ($c -and (Test-Path (Join-Path $c 'toolchains\llvm\prebuilt\windows-x86_64\bin\clang++.exe'))) { $ndkRoot = $c; break }
}
if (-not $ndkRoot) {
  Write-Host "Could not find the Android NDK. Looked in:"; $candidates | ForEach-Object { Write-Host "  $_" }
  Write-Host "Pass the NDK folder as an argument, e.g. build_plugin.cmd `"J:\Unity\$unityVersion\$ndkSuffix`""
  exit 1
}
Write-Host "Using NDK: $ndkRoot"
$clang = Join-Path $ndkRoot 'toolchains\llvm\prebuilt\windows-x86_64\bin\clang++.exe'

$src = $PSScriptRoot
$sources = @((Join-Path $src 'main.cpp'), (Join-Path $src 'Filter.cpp'))
$targets = [ordered]@{
  'arm64-v8a'   = 'aarch64-linux-android29'
  'armeabi-v7a' = 'armv7a-linux-androideabi29'
}
foreach ($abi in $targets.Keys) {
  $outDir = Join-Path $src "..\libs\$abi"
  New-Item -ItemType Directory -Force -Path $outDir | Out-Null
  $out = Join-Path $outDir 'libSoundStageNative.so'
  Write-Host "Building $abi ..."
  & $clang "--target=$($targets[$abi])" -shared -fPIC -O2 -Wall -s `
      -static-libstdc++ "-Wl,-z,max-page-size=16384" "-Wl,--build-id" `
      -o $out $sources -lm
  if ($LASTEXITCODE -ne 0 -or -not (Test-Path $out)) { Write-Host "Failed building $abi"; exit 1 }
  Write-Host ("  -> {0} ({1:N0} bytes)" -f (Resolve-Path $out), (Get-Item $out).Length)
}
Write-Host "Done."
