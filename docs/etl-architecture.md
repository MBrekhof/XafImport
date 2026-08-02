# ETL Pipeline Architecture (ETL-001 spike result)

Date: 2026-08-02. Decides the contracts for IMP-002/STG-001 and the staging strategy.

## Pipeline shape

Classic three stages, one orchestrator:

```
ISource ──records──> Transform (mapping) ──records──> IStagingLoader
   │                                                      │
   └── RecordSchema (discovered) ────────────────────────>┘  (CREATE TABLE)
```

- **Extract — `ISource`**: one implementation per source kind (file upload, SQL Server
  query, REST API). A source opens into a reader exposing a discovered `RecordSchema`
  (column name + CLR type) and a stream of records (`IDictionary<string, object?>`).
  For file sources the format parser (json/xml/xls/pdf/doc/txt) does the actual reading;
  format detection by magic bytes, never extension.
- **Transform**: a single `MappingTransformer` driven by the definition's column mapping
  (rename + type conversion). Deliberately minimal; grow it when a real import needs more
  (lookups, expressions → new card then, not now).
- **Load — `IStagingLoader`**: creates the staging table from the schema if needed and
  bulk-inserts records. Loading NEVER writes business objects directly; promote is a
  separate later step.

`ImportService` orchestrates the three stages, owns the `ImportRun` lifecycle
(status, counts), writes `ImportLogEntry` rows at stage boundaries and per-record
failures, and enforces the error policy (CFG-001). Both entry points (manual UI action,
Hangfire job) call this one service.

## Staging strategy (the XafDynamicAssemblies question)

**Decision: staging tables are plain SQL tables created via ADO.NET DDL — NOT runtime
XAF entities.**

XafDynamicAssemblies (studied 2026-08-02) generates entity source with Roslyn, loads it
in an `AssemblyLoadContext`, syncs schema with add-only DDL, and then **requires an app
restart** for XAF TypesInfo / the EF Core model to pick up the new types. That machinery
exists because its runtime entities are durable, user-facing business objects. Staging
tables are ephemeral per-definition structures; a restart per import definition is a
non-starter, and EF Core cannot extend a running model dynamically (confirmed in DX docs).

What we DO borrow from XafDynamicAssemblies: the `SchemaSynchronizer` DDL style —
quoted identifiers, CREATE TABLE from metadata, add-missing-columns on re-run, never
drop. SQL Server dialect for the POC (that is what the scaffold runs on); DDL lives
behind `IStagingLoader` so the dialect can swap later.

- Table naming: `stg_<DefinitionName>` (sanitized), one table per definition, truncated
  or appended per definition setting (POC: truncate on each run).
- All discovered columns typed from `RecordSchema` when the source provides types
  (SQL result set), `nvarchar(max)` when it does not (json/txt/...). Plus bookkeeping
  columns: `stg_Id` (identity PK), `stg_RunId`, `stg_RowNo`, `stg_Error`.
- XAF visibility: staging rows are NOT entities. The ImportRun DetailView shows counts +
  logs; a "Preview Staging" popup (non-persistent object over `SELECT TOP n`) is part of
  STG-001, not a separate metadata system.

## Contracts (target: XafImport.Module/Import/)

```csharp
public sealed record RecordSchema(IReadOnlyList<RecordColumn> Columns);
public sealed record RecordColumn(string Name, Type ClrType, bool IsNullable = true);

public interface ISource // one per SourceType: File, SqlServer, Api
{
    Task<ISourceReader> OpenAsync(ImportDefinition def, Stream? uploadedFile, CancellationToken ct);
}
public interface ISourceReader : IAsyncDisposable
{
    RecordSchema Schema { get; }
    IAsyncEnumerable<IDictionary<string, object?>> ReadAsync(CancellationToken ct);
}

public interface IFormatParser // one per format; used by the File source
{
    bool CanParse(ReadOnlySpan<byte> magicBytes, string? fileNameHint);
    ISourceReader Open(Stream stream);
}

public interface IStagingLoader
{
    Task<string> EnsureTableAsync(string definitionName, RecordSchema schema, CancellationToken ct);
    Task<int> LoadAsync(string tableName, Guid runId,
        IAsyncEnumerable<IDictionary<string, object?>> records,
        Action<int, Exception> onRecordError, CancellationToken ct);
}
```

## Out of scope for the POC (add when practice demands)

- Transform beyond rename/convert (expressions, lookups, joins)
- Automatic promote staging → business objects (stub/manual only)
- Non-SQL-Server staging dialects
- Parallel/partitioned loads
