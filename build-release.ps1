Param(
	[Parameter(Mandatory=$true)][string]$Version,
	[string]$OutputDir = "artifacts",
	[string]$Notes = "",
	[string[]]$Runtimes = @("win-x64","linux-x64")
)

Set-StrictMode -Version Latest

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $scriptRoot "PriceCheckerAvalonia/PriceCheckerAvalonia.csproj"
if (-not (Test-Path $proj)) { Write-Error "Project file not found: $proj"; exit 1 }

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

function Publish-And-Pack($rid) {
	$publishDir = Join-Path $env:TEMP "pricechecker-publish-$Version-$rid"
	if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
	New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

	Write-Host "Publishing for $rid..."
	dotnet publish $proj -c Release -r $rid --self-contained true /p:PublishSingleFile=true -o $publishDir | Write-Host

	if ($rid -like 'win-*') {
		$artifact = Join-Path $OutputDir "pricechecker-$Version-$rid.zip"
		Write-Host "Creating ZIP: $artifact"
		if (Test-Path $artifact) { Remove-Item $artifact }
		Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $artifact -Force
	}
	else {
		$artifact = Join-Path $OutputDir "pricechecker-$Version-$rid.tar.gz"
		Write-Host "Creating tar.gz: $artifact"
		if (Test-Path $artifact) { Remove-Item $artifact }
		# Use tar if available (Windows 10+ has tar), otherwise create zip as fallback
		try {
			& tar -C $publishDir -czf $artifact .
		}
		catch {
			Write-Warning "tar not available, creating zip fallback"
			$artifact = Join-Path $OutputDir "pricechecker-$Version-$rid.zip"
			Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $artifact -Force
		}
	}

	# compute sha256
	$hash = (Get-FileHash -Path $artifact -Algorithm SHA256).Hash.ToLower()

	$publishedAt = (Get-Date).ToUniversalTime().ToString("o")
	# build metadata
	$fileName = [IO.Path]::GetFileName($artifact)
	$metadata = [PSCustomObject]@{
		version = $Version
		url = "https://updates.example.com/$fileName" # change to your CDN URL
		sha256 = $hash
		publishedAt = $publishedAt
		notes = $Notes
		minClientVersion = "0.0.0"
	}

	$metaFile = Join-Path $OutputDir "metadata-$Version-$rid.json"
	$metadata | ConvertTo-Json -Depth 4 | Out-File -FilePath $metaFile -Encoding UTF8
	Write-Host "Artifact: $artifact"
	Write-Host "Metadata: $metaFile"

	# optional gpg signature if available
	if (Get-Command gpg -ErrorAction SilentlyContinue) {
		try {
			& gpg --batch --yes --detach-sign --armor --output "$artifact.sig" --sign $artifact
			Write-Host "Signed: $artifact.sig"
		}
		catch { Write-Warning "GPG signing failed: $_" }
	}
}

foreach ($rid in $Runtimes) { Publish-And-Pack $rid }

Write-Host "All done. Artifacts and metadata are in: $OutputDir"
