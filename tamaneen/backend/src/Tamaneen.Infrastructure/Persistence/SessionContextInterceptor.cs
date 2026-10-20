using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Tamaneen.SharedKernel;

namespace Tamaneen.Infrastructure.Persistence;

/// <summary>
/// Writes the caller into SQL Server SESSION_CONTEXT every time a connection opens. The values are
/// read-only for the rest of the session, so later SQL on the same connection cannot impersonate
/// someone else, and the pool's sp_reset_connection clears them before reuse.
/// The row-level security functions read user_id, role and patient_id from here.
/// </summary>
public sealed class SessionContextInterceptor : DbConnectionInterceptor
{
    private const string Sql =
        "EXEC sp_set_session_context @key = N'user_id', @value = @user_id, @read_only = 1; " +
        "EXEC sp_set_session_context @key = N'role', @value = @role, @read_only = 1; " +
        "EXEC sp_set_session_context @key = N'patient_id', @value = @patient_id, @read_only = 1;";

    private readonly ICurrentUser _user;

    public SessionContextInterceptor(ICurrentUser user) => _user = user;

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = Build(connection);
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var command = Build(connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private DbCommand Build(DbConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = Sql;
        command.CommandType = CommandType.Text;
        command.Parameters.Add(Parameter(command, "@user_id", _user.UserId));
        command.Parameters.Add(Parameter(command, "@role", AppRoles.IsKnown(_user.Role) ? _user.Role : null));
        command.Parameters.Add(Parameter(command, "@patient_id", _user.Role == AppRoles.Patient ? _user.PatientId : null));
        return command;
    }

    private static DbParameter Parameter(DbCommand command, string name, object? value)
    {
        var p = command.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        if (p is SqlParameter sql)
        {
            sql.SqlDbType = value is Guid ? SqlDbType.UniqueIdentifier : SqlDbType.NVarChar;
            if (value is null)
            {
                sql.SqlDbType = SqlDbType.NVarChar;
                sql.Size = 64;
            }
        }

        return p;
    }
}
