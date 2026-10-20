<#
.SYNOPSIS
  Enables Always Encrypted on Tamaneen columns that the database must never see in plaintext.

.DESCRIPTION
  Creates a column master key (CMK) that points at an RSA key in Azure Key Vault Managed HSM, a column
  encryption key (CEK) wrapped by it, and encrypts:
    identity.UserAccounts.ContactLookupHash  (deterministic: login lookups compare by equality)
  so that a database administrator or a stolen backup cannot run a dictionary attack on phone numbers,
  even with the HMAC pepper.

  Patient profile fields are already encrypted by the application with a per-patient key (needed for
  crypto-shredding) and are not double-encrypted here.

  After running, set Database:AlwaysEncrypted=true for the API so the driver decrypts transparently.

.PARAMETER Server            SQL Server or Azure SQL MI host name.
.PARAMETER Database          Database name.
.PARAMETER KeyVaultKeyUri    Versioned key URI, e.g. https://tamaneen-prod.managedhsm.azure.net/keys/ae-cmk/<version>
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory)] [string] $Server,
  [Parameter(Mandatory)] [string] $Database,
  [Parameter(Mandatory)] [string] $KeyVaultKeyUri
)

$ErrorActionPreference = 'Stop'
Import-Module SqlServer -MinimumVersion 22.0
Import-Module Az.Accounts

# Uses the operator's Entra ID sign-in for both SQL and Key Vault.
$token = (Get-AzAccessToken -ResourceUrl 'https://database.windows.net/').Token
$kvToken = (Get-AzAccessToken -ResourceUrl 'https://managedhsm.azure.net').Token
$connection = "Server=$Server;Database=$Database;Encrypt=True;TrustServerCertificate=False"
$db = Get-SqlDatabase -ConnectionString $connection -AccessToken $token

$cmkName = 'CMK_Tamaneen'
$cekName = 'CEK_Tamaneen_Identity'

if (-not ($db.ColumnMasterKeys | Where-Object Name -eq $cmkName)) {
  $settings = New-SqlAzureKeyVaultColumnMasterKeySettings -KeyUrl $KeyVaultKeyUri -AllowEnclaveComputations:$false
  New-SqlColumnMasterKey -Name $cmkName -InputObject $db -ColumnMasterKeySettings $settings | Out-Null
  Write-Host "Created column master key $cmkName"
}

if (-not ($db.ColumnEncryptionKeys | Where-Object Name -eq $cekName)) {
  New-SqlColumnEncryptionKey -Name $cekName -InputObject $db -ColumnMasterKeyName $cmkName -KeyVaultAccessToken $kvToken | Out-Null
  Write-Host "Created column encryption key $cekName"
}

$columns = @(
  New-SqlColumnEncryptionSettings -ColumnName 'identity.UserAccounts.ContactLookupHash' -EncryptionType 'Deterministic' -EncryptionKey $cekName
)

# Online encryption copies the table; run in a maintenance window. The unique index stays valid
# because deterministic encryption preserves equality.
Set-SqlColumnEncryption -InputObject $db -ColumnEncryptionSettings $columns -KeyVaultAccessToken $kvToken -LogFileDirectory .
Write-Host 'Always Encrypted is enabled. Set Database:AlwaysEncrypted=true and restart the API.'
