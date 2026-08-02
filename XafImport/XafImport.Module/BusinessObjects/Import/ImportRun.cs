#nullable enable
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations.Schema;
using DevExpress.ExpressApp.DC;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;

namespace XafImport.Module.BusinessObjects.Import
{
    public enum ImportRunStatus { Pending, Running, Succeeded, SucceededWithErrors, Failed }

    public enum ImportLogLevel { Info, Warning, Error }

    [NavigationItem("Import")]
    public class ImportRun : BaseObject
    {
        public virtual Guid? DefinitionId { get; set; }

        [ForeignKey(nameof(DefinitionId))]
        public virtual ImportDefinition? Definition { get; set; }

        [ToolTip("Uploaded file name, SQL query summary, or API endpoint")]
        public virtual string? SourceDescription { get; set; }

        public virtual string? StagingTableName { get; set; }
        public virtual DateTime? Started { get; set; }
        public virtual DateTime? Finished { get; set; }
        public virtual ImportRunStatus Status { get; set; }
        public virtual int RecordsRead { get; set; }
        public virtual int RecordsStaged { get; set; }
        public virtual int RecordsFailed { get; set; }

        // Export runs: the produced file, downloadable via the standard file editor.
        [VisibleInDetailView(false)]
        [VisibleInListView(false)]
        public virtual Guid? ResultFileId { get; set; }

        [ForeignKey(nameof(ResultFileId))]
        [Aggregated]
        [ExpandObjectMembers(ExpandObjectMembers.Never)]
        public virtual FileData? ResultFile { get; set; }

        [Aggregated]
        public virtual IList<ImportLogEntry> LogEntries { get; set; } = new ObservableCollection<ImportLogEntry>();
    }

    public class ImportLogEntry : BaseObject
    {
        public virtual Guid? RunId { get; set; }

        [ForeignKey(nameof(RunId))]
        public virtual ImportRun? Run { get; set; }

        public virtual DateTime Timestamp { get; set; }
        public virtual ImportLogLevel Level { get; set; }
        public virtual string? Message { get; set; }

        [ToolTip("1-based source record number, when the entry concerns a single record")]
        public virtual int? RecordNumber { get; set; }
    }
}
