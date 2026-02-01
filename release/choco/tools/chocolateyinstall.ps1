
$ErrorActionPreference = 'Stop';

$toolsDir   = "$(Split-Path -parent $MyInvocation.MyCommand.Definition)"
<<<<<<< HEAD
$url64      = 'https://github.com/pyrevitlabs/pyRevit/releases/download/v4.8.16.24121%2B2117/pyRevit_CLI_4.8.16.24121_admin_signed.exe'
=======
$url64      = 'https://github.com/pyrevitlabs/pyRevit/releases/download/v4.8.15.24089%2B0912/pyRevit_CLI_4.8.15.24089_admin_signed.exe'
>>>>>>> origin/docs

$packageArgs = @{
  packageName   = $env:ChocolateyPackageName
  unzipLocation = $toolsDir
  fileType      = 'exe'
  url64bit      = $url64

  softwareName  = 'pyrevit-cli*'

<<<<<<< HEAD
  checksum64    = '1A46DAD7AF5ADB3BD0C9E589B6E51FBCF06BD6348CF520E8142FA3781456FECA'
=======
  checksum64    = '694284EE38EE7A448D7D7FBF9A62CA9820EAB4AD0FEE9FE5E829D6552DA64D4D'
>>>>>>> origin/docs
  checksumType64= 'sha256'

  silentArgs    = "/VERYSILENT"
  validExitCodes= @(0, 3010, 1641)
}

Install-ChocolateyPackage @packageArgs










    








