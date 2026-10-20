using Azure.Core;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.AlwaysEncrypted.AzureKeyVaultProvider;

namespace Tamaneen.Infrastructure.Persistence;

/// <summary>
/// Always Encrypted protects selected columns from anyone with database access, including DBAs.
/// The column master key lives in Azure Key Vault; the client driver decrypts transparently.
/// Columns are encrypted by ops/sql/always-encrypted.sql (needs the key path of the environment).
/// </summary>
public static class AlwaysEncryptedSetup
{
    private static readonly object Gate = new();
    private static bool _registered;

    public static string EnableOnConnectionString(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            ColumnEncryptionSetting = SqlConnectionColumnEncryptionSetting.Enabled,
        };
        return builder.ConnectionString;
    }

    /// <summary>Registers the Key Vault key store provider once per process (the driver allows a single registration).</summary>
    public static void RegisterAzureKeyVaultProvider(TokenCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        lock (Gate)
        {
            if (_registered)
            {
                return;
            }

            SqlConnection.RegisterColumnEncryptionKeyStoreProviders(new Dictionary<string, SqlColumnEncryptionKeyStoreProvider>(StringComparer.OrdinalIgnoreCase)
            {
                [SqlColumnEncryptionAzureKeyVaultProvider.ProviderName] = new SqlColumnEncryptionAzureKeyVaultProvider(credential),
            });
            _registered = true;
        }
    }
}
