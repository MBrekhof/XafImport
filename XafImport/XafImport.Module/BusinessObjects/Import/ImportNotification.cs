#nullable enable
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.Base.General;
using DevExpress.Persistent.BaseImpl.EF;

namespace XafImport.Module.BusinessObjects.Import
{
    // Run-completion notification shown by the XAF Notifications module (ISupportNotifications
    // + AlarmTime = run finish time; the module's poll picks it up and pops the window).
    // ponytail: shown to ALL users (DefaultNotificationsProvider) — per-user filtering needs a
    // custom provider filter, add when someone asks who may see what.
    [NavigationItem("Import")]
    [DefaultProperty(nameof(Message))]
    public class ImportNotification : BaseObject, ISupportNotifications
    {
        public virtual string Message { get; set; } = string.Empty;

        public virtual Guid? RunId { get; set; }

        [ForeignKey(nameof(RunId))]
        public virtual ImportRun? Run { get; set; }

        [Browsable(false)]
        public virtual DateTime? AlarmTime { get; set; }

        [Browsable(false)]
        public virtual bool IsPostponed { get; set; }

        public virtual TimeSpan? RemindIn { get; set; }

        [Browsable(false)]
        [NotMapped]
        public string NotificationMessage => Message;

        [Browsable(false)]
        [NotMapped]
        public object UniqueId => ID;
    }
}
