# Integrating XafImport.Module into a host XAF application

The deliverable of this POC is `XafImport.Module` — a platform-agnostic XAF module with the
import/export (ETL-style) pipeline. The Blazor.Server project in this repo is only the test
harness. This is the checklist to drop the module into an existing XAF Blazor Server app.

## 1. Reference and register the module

- Reference `XafImport.Module` (project or package reference).
- The module needs `DevExpress.Document.Processor` (Excel/PDF/Word parsing) — flows in
  transitively.
- Register in the host's `Startup.ConfigureServices`:

```csharp
services.AddXaf(Configuration, builder => {
    builder.Modules
        .AddNotifications(o => o.NotificationsRefreshInterval = TimeSpan.FromSeconds(30))
        .Add<XafImport.Module.XafImportModule>();
    // ...
});
```

The Notifications module is required for run-completion notifications
(`ImportDefinition.NotifyOnCompletion`); leave the interval at its default 5 minutes if
prompt notifications don't matter.

## 2. Register the entities in the host DbContext

XAF discovers the types via the module, but EF Core needs the DbSets:

```csharp
public DbSet<ImportDefinition> ImportDefinitions { get; set; }
public DbSet<ImportRun> ImportRuns { get; set; }
public DbSet<ImportLogEntry> ImportLogEntries { get; set; }
public DbSet<ImportNotification> ImportNotifications { get; set; }
public DbSet<FileData> FileData { get; set; }   // usually already present
```

## 3. Register the pipeline services

```csharp
services.AddXafImportPipeline();   // XafImport.Module.Import — parsers, sources, Import/ExportService
```

The staging loader reads `ConnectionStrings:ConnectionString`. Staging tables
(`stg_<definition>`) are created in that database on the fly; truncated per run.

## 4. Background jobs (Hangfire)

The module itself has **zero Hangfire dependency** — it defines `IJobDispatcher`,
`IJobHandler<T>`, the commands (`RunImportCommand`, `RunExportCommand`) and a synchronous
`DirectJobDispatcher` fallback. The Hangfire side lives in the host (copy
`Jobs/HangfireJobInfrastructure.cs` from this repo's Blazor.Server, or reference it):

```csharp
services.AddJobDispatcher(Configuration);   // Jobs:UseHangfire=true -> Hangfire, else inline
services.AddJobHandler<RunImportCommand, RunImportHandler>();
services.AddJobHandler<RunExportCommand, RunExportHandler>();
```

```json
{ "Jobs": { "UseHangfire": true } }
```

Plus in `Configure`: `app.UseHangfireDashboard("/hangfire", ...)` with a proper
authorization filter (the POC filter allows Development only — replace with an XAF role
check before deploying).

### Service user

Hangfire workers authenticate into XAF as the `HangfireJob` user (see
`XafJobScopeInitializer`). The Updater seeds it. **Give it a dedicated role with the
permissions the pipeline needs (ImportDefinition/ImportRun/ImportLogEntry/
ImportNotification/FileData read-write) — the POC grants admin + empty password, which
must not survive into a host app.** Password comes from `HangfireJob:Password` config.

### Database update ordering (important)

Hangfire workers start with the host, but XAF's normal database update runs when a user
opens the app — on a fresh database the seeded service user won't exist yet and jobs fail
their logon. Two supported options (DX docs 113239):

- Deployment: run `YourApp.exe --updateDatabase --silent` before starting the app.
- Dev convenience: spawn the update as a **child process** before `host.Run()` (see this
  repo's `Program.cs`). Do NOT run `IDBUpdater` in-process and then serve — it corrupts
  XAF's model statics (`ModelNode.GetNode` index errors on every view).

## 5. What deliberately stays out of the module

- Hangfire packages and the dashboard (host concern, above).
- The `/dev/*` endpoints and `DevStubImport*` (test scaffolding, Development-gated, in the
  harness project only).
- Anything WinForms-specific — the module is platform-agnostic; the controller uses only
  `SimpleAction`.

## Known POC ceilings (marked with `ponytail:` comments in code)

| Area | Ceiling | Upgrade path |
|---|---|---|
| Secrets | `SqlConnectionString`/`ApiKey` stored plain (masked display only) | Encrypt at rest (provider_settings pattern) |
| Service user | Admin role, empty password | Dedicated role + real password |
| Dashboard | Development-only access | XAF role-based authorization filter |
| Staging | Truncate per run, one table per definition | Append + `stg_RunId`-based retention |
| File parsers | Whole payload buffered in memory | Streaming readers for large files |
| Legacy `.doc` | OLE compound files default to Excel detection | Set `FileFormat=Doc` explicitly, or add compound-directory inspection |
| Excel dates | Date cells arrive as Excel numerics | Type via cell number format when needed |
| Notifications | Shown to all users | Custom notifications provider filter |
| Transform | Rename-only column mapping | Type conversion/expressions/lookups when a real import needs them |
| Cron | `CronExpression` field exists but is not synced to Hangfire | Small startup sync via `IJobDispatcher.Schedule` |
