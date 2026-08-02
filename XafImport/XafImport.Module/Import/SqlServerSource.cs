#nullable enable
using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;
using XafImport.Module.BusinessObjects.Import;

namespace XafImport.Module.Import
{
    // ISource for SQL Server: connection string + query on the definition, result-set schema
    // drives the staging table with real column types. Streams rows — no buffering.
    // ponytail: SqlConnectionString stored plain in the POC; encrypt per provider_settings pattern before real use.
    public sealed class SqlServerSource : ISource
    {
        public async Task<ISourceReader> OpenAsync(ImportDefinition definition, Stream? uploadedFile, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(definition.SqlConnectionString) || string.IsNullOrWhiteSpace(definition.SqlQuery))
            {
                throw new InvalidOperationException("SqlConnectionString and SqlQuery are required for a SQL Server source.");
            }
            var connection = new SqlConnection(definition.SqlConnectionString);
            await connection.OpenAsync(ct);
            var command = new SqlCommand(definition.SqlQuery, connection);
            var reader = await command.ExecuteReaderAsync(ct);
            var columns = Enumerable.Range(0, reader.FieldCount)
                .Select(i => new RecordColumn(reader.GetName(i), reader.GetFieldType(i)))
                .ToList();
            return new SqlReader(connection, command, reader, new RecordSchema(columns));
        }

        private sealed class SqlReader : ISourceReader
        {
            private readonly SqlConnection connection;
            private readonly SqlCommand command;
            private readonly SqlDataReader reader;

            public SqlReader(SqlConnection connection, SqlCommand command, SqlDataReader reader, RecordSchema schema)
            {
                this.connection = connection;
                this.command = command;
                this.reader = reader;
                Schema = schema;
            }

            public RecordSchema Schema { get; }

            public async IAsyncEnumerable<IDictionary<string, object?>> ReadAsync([EnumeratorCancellation] CancellationToken ct)
            {
                while (await reader.ReadAsync(ct))
                {
                    var record = new Dictionary<string, object?>(reader.FieldCount);
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        var value = reader.GetValue(i);
                        record[reader.GetName(i)] = value is DBNull ? null : value;
                    }
                    yield return record;
                }
            }

            public async ValueTask DisposeAsync()
            {
                await reader.DisposeAsync();
                await command.DisposeAsync();
                await connection.DisposeAsync();
            }
        }
    }
}
