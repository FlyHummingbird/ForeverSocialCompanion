param([string]$CertificateThumbprint,[switch]$AllowUnsignedTest)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if (-not $CertificateThumbprint -and -not $AllowUnsignedTest) { throw 'A public release requires a trusted code-signing certificate. Use -AllowUnsignedTest only for explicitly labeled test artifacts.' }
$root=$PSScriptRoot
$project=[xml](Get-Content -LiteralPath (Join-Path $root 'App\App.csproj') -Raw)
$version=[string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'App.csproj requires a valid release version.' }
$publish=Join-Path $root ('dist\publish-'+[guid]::NewGuid().ToString('N'))
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:MSBuildEnableWorkloadResolver='false'
$buildArgs=@('publish',(Join-Path $root 'App\App.csproj'),'-c','Release','-r','win-x64','--self-contained','true','-o',$publish,'--nologo','-m:1','-nr:false','-p:UseSharedCompilation=false','-p:PublishSingleFile=true','-p:IncludeNativeLibrariesForSelfExtract=true','-p:EnableCompressionInSingleFile=false','-p:DebugType=None','-p:DebugSymbols=false')
if ($CertificateThumbprint) { $buildArgs+='-p:DefineConstants=SIGNED_RELEASE' }
& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { throw 'Companion publish failed.' }
$ownFiles=@('ForeverSocialCompanion.exe')
if ($CertificateThumbprint) {
    $cert=Get-Item -LiteralPath "Cert:\CurrentUser\My\$CertificateThumbprint"
    if (-not $cert.HasPrivateKey -or $cert.NotAfter -lt (Get-Date) -or -not ($cert.EnhancedKeyUsageList.ObjectId -contains '1.3.6.1.5.5.7.3.3')) { throw 'A valid code-signing certificate with its private key is required.' }
    foreach ($name in $ownFiles) {
        $signed=Set-AuthenticodeSignature -LiteralPath (Join-Path $publish $name) -Certificate $cert -HashAlgorithm SHA256 -TimestampServer 'http://timestamp.digicert.com'
        if ($signed.Status -ne 'Valid') { throw "Signing failed: $name" }
    }
}
$label=if($CertificateThumbprint){$version}else{"$version-UNSIGNED-TEST"}
$stage=Join-Path $root ('dist\package-'+$label+'-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
foreach ($file in Get-ChildItem -LiteralPath $publish -File) { if($file.Extension -ne '.pdb'){ Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $stage $file.Name) -Force } }
if (@(Get-ChildItem -LiteralPath $stage -File).Count -ne 1 -or -not (Test-Path -LiteralPath (Join-Path $stage 'ForeverSocialCompanion.exe'))) { throw 'Single-file publishing produced unexpected external runtime dependencies.' }
foreach ($name in @('LICENSE','PRIVACY.md')) { Copy-Item -LiteralPath (Join-Path $root $name) -Destination $stage -Force }
Copy-Item -LiteralPath (Join-Path $root 'Docs\USER-README.md') -Destination (Join-Path $stage 'README.md')
# Preserve the licenses of the exact runtime packs selected by this publish.
$licenses=Join-Path $stage 'Licenses'; New-Item -ItemType Directory -Path $licenses | Out-Null
$assets=Get-Content -LiteralPath (Join-Path $root 'App\obj\project.assets.json') -Raw | ConvertFrom-Json
foreach($pack in @('Microsoft.NETCore.App.Runtime.win-x64','Microsoft.WindowsDesktop.App.Runtime.win-x64')) {
    $dependencies=@($assets.project.frameworks.PSObject.Properties | ForEach-Object {$_.Value.downloadDependencies} | Where-Object {$_.name -eq $pack})
    if($dependencies.Count -ne 1) { throw "Cannot identify exact runtime-pack dependency: $pack" }
    $matched=[regex]::Match([string]$dependencies[0].version,'^\[([0-9.]+),\s*\1\]$')
    if(-not $matched.Success) { throw "Cannot identify runtime-pack version: $pack" }
    $packPath=$null
    foreach($base in $assets.packageFolders.PSObject.Properties.Name) {
        $candidate=Join-Path $base ($pack.ToLowerInvariant()+'\'+$matched.Groups[1].Value)
        if(Test-Path -LiteralPath $candidate -PathType Container) { $packPath=$candidate; break }
    }
    if(-not $packPath) { throw "Runtime pack missing: $pack" }
    $notices=@(Get-ChildItem -LiteralPath $packPath -File | Where-Object {$_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)(\.TXT)?$'})
    if(-not $notices.Count) { throw "Runtime license missing: $pack" }
    foreach($notice in $notices) { Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $licenses ($pack+'-'+$notice.Name)) }
}
$manifest=@{version=$version;edition=$label;architecture='win-x64';layout='single-file';signed=[bool]$CertificateThumbprint;files=@{}}
foreach($file in Get-ChildItem -LiteralPath $stage -Recurse -File){ if($file.Name -ne 'manifest.json'){$relative=$file.FullName.Substring($stage.Length+1).Replace('\','/');$manifest.files[$relative]=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()} }
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $stage 'manifest.json') -Encoding utf8
$archive=Join-Path $root "dist\ForeverSocialCompanion-$label-win-x64.zip"
Compress-Archive -LiteralPath (Get-ChildItem -LiteralPath $stage).FullName -DestinationPath $archive -Force
Get-Item -LiteralPath $archive | Select-Object FullName,Length
Get-FileHash -LiteralPath $archive -Algorithm SHA256
