Param(
    [Parameter(Mandatory=$true,Position=1)]
    [string]$path,
    [Parameter(Mandatory=$true,Position=2)]
    [string]$version
)

$file = Get-Item $path

$xml = [xml](Get-Content $path)

$xml.package.metadata.version = $version

$xml.Save((Join-Path (pwd) $path))
