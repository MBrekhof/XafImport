#nullable enable
using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace XafImport.Module.Import
{
    // Staging = plain SQL tables (docs/etl-architecture.md), SchemaSynchronizer DDL style:
    // quoted identifiers, CREATE from metadata, add-only columns, never drop.
    public sealed class SqlServerStagingLoader : IStagingLoader
    {
        private const int BatchSize = 1000;
        private readonly string connectionString;

        public SqlServerStagingLoader(IConfiguration configuration)
        {
            connectionString = configuration.GetConnectionString("ConnectionString")
                ?? throw new InvalidOperationException("ConnectionStrings:ConnectionString is required.");
        }

        public async Task<string> EnsureTableAsync(string definitionName, RecordSchema schema, CancellationToken ct)
        {
            var tableName = "stg_" + Sanitize(definitionName);
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);

            var exists = (int?)await ExecScalarAsync(conn,
                $"SELECT COUNT(*) FROM sys.tables WHERE name = '{tableName}'", ct) > 0;
            if (!exists)
            {
                var cols = new StringBuilder();
                cols.Append("[stg_Id] bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, [stg_RunId] uniqueidentifier NOT NULL, [stg_RowNo] int NOT NULL");
                foreach (var c in schema.Columns)
                {
                    cols.Append($", [{Sanitize(c.Name)}] {SqlType(c.ClrType)} NULL");
                }
                await ExecAsync(conn, $"CREATE TABLE [{tableName}] ({cols})", ct);
            }
            else
            {
                foreach (var c in schema.Columns)
                {
                    await ExecAsync(conn,
                        $"IF COL_LENGTH('{tableName}', '{Sanitize(c.Name)}') IS NULL ALTER TABLE [{tableName}] ADD [{Sanitize(c.Name)}] {SqlType(c.ClrType)} NULL", ct);
                }
                // ponytail: truncate-per-run; append + per-run retention when a real scenario needs history.
                await ExecAsync(conn, $"TRUNCATE TABLE [{tableName}]", ct);
            }
            return tableName;
        }

        public async Task<int> LoadAsync(string tableName, RecordSchema schema, Guid runId,
            IAsyncEnumerable<IDictionary<string, object?>> records,
            Action<int, Exception> onRecordError, CancellationToken ct)
        {
            var table = new DataTable();
            table.Columns.Add("stg_RunId", typeof(Guid));
            table.Columns.Add("stg_RowNo", typeof(int));
            foreach (var c in schema.Columns)
            {
                table.Columns.Add(Sanitize(c.Name), Nullable.GetUnderlyingType(c.ClrType) ?? c.ClrType);
            }

            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);
            using var bulk = new SqlBulkCopy(conn) { DestinationTableName = $"[{tableName}]" };
            foreach (DataColumn col in table.Columns)
            {
                bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);
            }

            int rowNo = 0, staged = 0;
            await foreach (var record in records.WithCancellation(ct))
            {
                rowNo++;
                try
                {
                    var row = table.NewRow();
                    row["stg_RunId"] = runId;
                    row["stg_RowNo"] = rowNo;
                    foreach (var c in schema.Columns)
                    {
                        row[Sanitize(c.Name)] = record.TryGetValue(c.Name, out var v) && v != null ? v : DBNull.Value;
                    }
                    table.Rows.Add(row);
                }
                catch (Exception ex)
                {
                    onRecordError(rowNo, ex);
                    continue;
                }
                if (table.Rows.Count >= BatchSize)
                {
                    await bulk.WriteToServerAsync(table, ct);
                    staged += table.Rows.Count;
                    table.Clear();
                }
            }
            if (table.Rows.Count > 0)
            {
                await bulk.WriteToServerAsync(table, ct);
                staged += table.Rows.Count;
            }
            return staged;
        }

        internal static string Sanitize(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (var ch in name)
            {
                sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
            }
            return sb.Length == 0 ? "_" : sb.ToString();
        }

        private static string SqlType(Type t) => (Nullable.GetUnderlyingType(t) ?? t) switch
        {
            var x when x == typeof(int) => "int",
            var x when x == typeof(long) => "bigint",
            var x when x == typeof(short) => "smallint",
            var x when x == typeof(byte) => "tinyint",
            var x when x == typeof(decimal) => "decimal(28,10)",
            var x when x == typeof(double) || x == typeof(float) => "float",
            var x when x == typeof(bool) => "bit",
            var x when x == typeof(DateTime) => "datetime2",
            var x when x == typeof(DateTimeOffset) => "datetimeoffset",
            var x when x == typeof(Guid) => "uniqueidentifier",
            var x when x == typeof(byte[]) => "varbinary(max)",
            _ => "nvarchar(max)",
        };

        private static async Task ExecAsync(SqlConnection conn, string sql, CancellationToken ct)
        {
            await using var cmd = new SqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        private static async Task<object?> ExecScalarAsync(SqlConnection conn, string sql, CancellationToken ct)
        {
            await using var cmd = new SqlCommand(sql, conn);
            return await cmd.ExecuteScalarAsync(ct);
        }
    }
}
