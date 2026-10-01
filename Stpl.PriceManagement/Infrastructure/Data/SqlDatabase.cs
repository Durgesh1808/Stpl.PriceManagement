using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Stpl.PriceManagement.Infrastructure.Data
{
    /// <summary>
    /// The one place a connection is opened. Plain ADO.NET: every call runs a
    /// stored procedure, there is no inline SQL anywhere in the application.
    /// </summary>
    /// <remarks>
    /// Every repository in every area uses this, so they all share the same
    /// timeout, the same null handling and the same row mapping.
    ///
    /// Rows are mapped to classes by column name (see <see cref="RowMapper"/>),
    /// so a procedure's result columns and a row class's properties only have
    /// to share names - the same convention the stored procedures were written
    /// against.
    /// </remarks>
    public sealed class SqlDatabase
    {
        private readonly DatabaseOptions _options;

        public SqlDatabase(IOptions<DatabaseOptions> options)
        {
            _options = options.Value;
        }

        public SqlConnection CreateConnection()
        {
            if (string.IsNullOrWhiteSpace(_options.ConnectionString))
            {
                throw new InvalidOperationException(
                    "Database:ConnectionString is not configured. Set it in appsettings.json "
                    + "(or an environment variable Database__ConnectionString).");
            }

            return new SqlConnection(_options.ConnectionString);
        }

        private SqlCommand Procedure(SqlConnection connection, string procedure, Action<SqlParameterCollection> parameters)
        {
            var command = connection.CreateCommand();
            command.CommandText = procedure;
            command.CommandType = CommandType.StoredProcedure;
            command.CommandTimeout = _options.CommandTimeoutSeconds;

            if (parameters != null)
            {
                parameters(command.Parameters);
            }

            return command;
        }

        /// <summary>Runs a procedure that returns nothing the caller needs.</summary>
        public async Task ExecuteAsync(
            string procedure, Action<SqlParameterCollection> parameters, CancellationToken cancellationToken)
        {
            using (var connection = CreateConnection())
            using (var command = Procedure(connection, procedure, parameters))
            {
                await connection.OpenAsync(cancellationToken);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        /// <summary>Every row of the first result set.</summary>
        public async Task<List<T>> QueryAsync<T>(
            string procedure, Action<SqlParameterCollection> parameters, CancellationToken cancellationToken)
            where T : new()
        {
            using (var connection = CreateConnection())
            using (var command = Procedure(connection, procedure, parameters))
            {
                await connection.OpenAsync(cancellationToken);

                using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                {
                    return await RowMapper.ReadAllAsync<T>(reader, cancellationToken);
                }
            }
        }

        /// <summary>
        /// Exactly one row. Throws when there is none or more than one, because
        /// the procedures called this way always answer with one.
        /// </summary>
        public async Task<T> QuerySingleAsync<T>(
            string procedure, Action<SqlParameterCollection> parameters, CancellationToken cancellationToken)
            where T : new()
        {
            var rows = await QueryAsync<T>(procedure, parameters, cancellationToken);
            if (rows.Count != 1)
            {
                throw new InvalidOperationException(
                    procedure + " returned " + rows.Count + " rows where exactly one was expected.");
            }

            return rows[0];
        }

        /// <summary>One row or none (null). More than one is an error.</summary>
        public async Task<T> QuerySingleOrDefaultAsync<T>(
            string procedure, Action<SqlParameterCollection> parameters, CancellationToken cancellationToken)
            where T : class, new()
        {
            var rows = await QueryAsync<T>(procedure, parameters, cancellationToken);
            if (rows.Count > 1)
            {
                throw new InvalidOperationException(
                    procedure + " returned " + rows.Count + " rows where at most one was expected.");
            }

            return rows.Count == 0 ? null : rows[0];
        }

        /// <summary>
        /// The first column of the only row, or the type's default when there
        /// is no row (null for a nullable type).
        /// </summary>
        public async Task<T> QueryValueOrDefaultAsync<T>(
            string procedure, Action<SqlParameterCollection> parameters, CancellationToken cancellationToken)
        {
            using (var connection = CreateConnection())
            using (var command = Procedure(connection, procedure, parameters))
            {
                await connection.OpenAsync(cancellationToken);

                using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                {
                    var found = false;
                    var value = default(T);

                    while (await reader.ReadAsync(cancellationToken))
                    {
                        if (found)
                        {
                            throw new InvalidOperationException(
                                procedure + " returned more than one row where at most one was expected.");
                        }

                        found = true;
                        value = RowMapper.ConvertValue<T>(reader.IsDBNull(0) ? null : reader.GetValue(0));
                    }

                    return value;
                }
            }
        }

        /// <summary>The first column of the first row (ExecuteScalar).</summary>
        public async Task<T> ScalarAsync<T>(
            string procedure, Action<SqlParameterCollection> parameters, CancellationToken cancellationToken)
        {
            using (var connection = CreateConnection())
            using (var command = Procedure(connection, procedure, parameters))
            {
                await connection.OpenAsync(cancellationToken);
                var value = await command.ExecuteScalarAsync(cancellationToken);
                return RowMapper.ConvertValue<T>(value is DBNull ? null : value);
            }
        }

        /// <summary>
        /// A procedure that answers with several result sets, read in order by
        /// <paramref name="read"/>.
        /// </summary>
        public async Task<TResult> QueryMultipleAsync<TResult>(
            string procedure,
            Action<SqlParameterCollection> parameters,
            Func<ResultSets, Task<TResult>> read,
            CancellationToken cancellationToken)
        {
            using (var connection = CreateConnection())
            using (var command = Procedure(connection, procedure, parameters))
            {
                await connection.OpenAsync(cancellationToken);

                using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                {
                    return await read(new ResultSets(reader, cancellationToken));
                }
            }
        }
    }

    /// <summary>
    /// Walks the result sets of one procedure call, one ReadAsync per set.
    /// </summary>
    public sealed class ResultSets
    {
        private readonly SqlDataReader _reader;
        private readonly CancellationToken _cancellationToken;
        private bool _first = true;

        public ResultSets(SqlDataReader reader, CancellationToken cancellationToken)
        {
            _reader = reader;
            _cancellationToken = cancellationToken;
        }

        /// <summary>The next result set, mapped. Empty when the procedure sent no more sets.</summary>
        public async Task<List<T>> ReadAsync<T>() where T : new()
        {
            if (!_first && !await _reader.NextResultAsync(_cancellationToken))
            {
                return new List<T>();
            }

            _first = false;
            return await RowMapper.ReadAllAsync<T>(_reader, _cancellationToken);
        }
    }
}
