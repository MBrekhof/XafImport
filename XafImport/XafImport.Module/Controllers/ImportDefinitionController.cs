#nullable enable
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.Persistent.Base;
using Microsoft.Extensions.DependencyInjection;
using XafImport.Module.BusinessObjects.Import;
using XafImport.Module.Import;
using XafImport.Module.Jobs;

namespace XafImport.Module.Controllers
{
    // UI-001: "Run Import" on the ImportDefinition DetailView. Saves pending edits, then
    // dispatches RunImportCommand — Hangfire queue when Jobs:UseHangfire=true, inline otherwise.
    public class ImportDefinitionController : ObjectViewController<DetailView, ImportDefinition>
    {
        private readonly SimpleAction runImport;

        public ImportDefinitionController()
        {
            runImport = new SimpleAction(this, "RunImport", PredefinedCategory.Edit)
            {
                Caption = "Run Import",
                ImageName = "Action_SimpleAction",
                ToolTip = "Queue this import; results appear under Import Runs",
            };
            runImport.Execute += RunImport_Execute;
        }

        protected override void OnActivated()
        {
            base.OnActivated();
            UpdateActionState();
            View.CurrentObjectChanged += View_CurrentObjectChanged;
        }

        protected override void OnDeactivated()
        {
            View.CurrentObjectChanged -= View_CurrentObjectChanged;
            runImport.Enabled.RemoveItem("DefinitionEnabled");
            base.OnDeactivated();
        }

        private void View_CurrentObjectChanged(object? sender, EventArgs e) => UpdateActionState();

        private void UpdateActionState()
            => runImport.Enabled["DefinitionEnabled"] = ViewCurrentObject?.Enabled ?? false;

        private void RunImport_Execute(object sender, SimpleActionExecuteEventArgs e)
        {
            var definition = ViewCurrentObject;
            if (ObjectSpace.IsModified)
            {
                ObjectSpace.CommitChanges();
            }
            var dispatcher = Application.ServiceProvider.GetRequiredService<IJobDispatcher>();
            // Hangfire dispatch just enqueues (completes synchronously); the Direct fallback runs inline.
            dispatcher.DispatchAsync(new RunImportCommand(definition.ID)).GetAwaiter().GetResult();
            Application.ShowViewStrategy.ShowMessage("Import queued — see Import Runs for progress.", InformationType.Success);
        }
    }
}
