[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$ServerInstance,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_-]{1,128}$')][string]$DatabaseName,
    [Parameter(Mandatory)][ValidatePattern('^/var/opt/mssql/backup/[A-Za-z0-9_./-]+$')][string]$SqlBackupDirectory,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$SqlUser,
    [Parameter(Mandatory)][Security.SecureString]$SqlPassword
)

$ErrorActionPreference = 'Stop'
$stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$backupPath = "$($SqlBackupDirectory.TrimEnd('/'))/$DatabaseName-$stamp.bak"
$escapedDatabase = $DatabaseName.Replace(']', ']]')
$escapedPath = $backupPath.Replace("'", "''")
$query = "BACKUP DATABASE [$escapedDatabase] TO DISK = N'$escapedPath' WITH COPY_ONLY, COMPRESSION, CHECKSUM, INIT, STATS = 10; RESTORE VERIFYONLY FROM DISK = N'$escapedPath' WITH CHECKSUM;"
$credential = [Net.NetworkCredential]::new('', $SqlPassword)
try {
    $env:SQLCMDPASSWORD = $credential.Password
    & sqlcmd -S $ServerInstance -U $SqlUser -C -b -Q $query
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed with exit code $LASTEXITCODE" }
    Write-Output "Verified backup created at $backupPath"
}
finally {
    Remove-Item Env:\SQLCMDPASSWORD -ErrorAction SilentlyContinue
    $credential.Password = ''
}
