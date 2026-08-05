[CmdletBinding()]
param([Parameter(Mandatory)][string]$MsixPath,[Parameter(Mandatory)][string]$CertificatePath,[Parameter(Mandatory)][SecureString]$CertificatePassword,[Parameter(Mandatory)][string]$ExpectedPublisher,[Parameter(Mandatory)][string]$SignToolPath,[string]$TimestampUrl)
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$msix=(Resolve-Path -LiteralPath $MsixPath).Path;$certificate=(Resolve-Path -LiteralPath $CertificatePath).Path;$tool=(Resolve-Path -LiteralPath $SignToolPath).Path
if(-not$msix.EndsWith('.msix',[StringComparison]::OrdinalIgnoreCase)){throw 'Only an existing .msix artifact can be signed.'}
if($certificate.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Release certificates must remain outside the repository.'}
$cert=[Security.Cryptography.X509Certificates.X509Certificate2]::new($certificate,$CertificatePassword)
if($cert.NotAfter-le[DateTime]::UtcNow-or$cert.NotBefore-gt[DateTime]::UtcNow){throw 'The signing certificate is not currently valid.'}
if(-not[string]::Equals($cert.Subject,$ExpectedPublisher,[StringComparison]::Ordinal)){throw 'Certificate subject does not exactly match the expected manifest publisher.'}
$password=[Net.NetworkCredential]::new('', $CertificatePassword).Password
try{$arguments=@('sign','/fd','SHA256','/f',$certificate,'/p',$password);if($TimestampUrl){$arguments+=@('/tr',$TimestampUrl,'/td','SHA256')};$arguments+=$msix;& $tool @arguments;if($LASTEXITCODE){throw 'signtool failed to sign the MSIX artifact.'}}finally{$password=$null}
& $tool verify /pa /all $msix;if($LASTEXITCODE){throw 'The signed MSIX failed trust verification.'}
Write-Host 'MSIX signature verified. No certificate or password was written to the repository.'
