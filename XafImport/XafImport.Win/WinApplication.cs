using DevExpress.EntityFrameworkCore.Security;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.ApplicationBuilder;
using DevExpress.ExpressApp.EFCore;
using DevExpress.ExpressApp.Updating;
using DevExpress.ExpressApp.Win;
using DevExpress.ExpressApp.Win.Utils;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel;
using System.Data.Common;
using XafImport.Module;
using XafImport.Module.BusinessObjects;

namespace XafImport.Win
{
    // For more typical usage scenarios, be sure to check out https://docs.devexpress.com/eXpressAppFramework/DevExpress.ExpressApp.Win.WinApplication._members
    public class XafImportWindowsFormsApplication : WinApplication
    {
        public XafImportWindowsFormsApplication()
        {
            SplashScreen = new DXSplashScreen(typeof(XafSplashScreen), new DefaultOverlayFormOptions());
            ApplicationName = "XafImport";
            CheckCompatibilityType = DevExpress.ExpressApp.CheckCompatibilityType.DatabaseSchema;
            DatabaseVersionMismatch += XafImportWindowsFormsApplication_DatabaseVersionMismatch;
            CustomizeLanguagesList += XafImportWindowsFormsApplication_CustomizeLanguagesList;
        }
        void XafImportWindowsFormsApplication_CustomizeLanguagesList(object sender, CustomizeLanguagesListEventArgs e)
        {
            string userLanguageName = System.Threading.Thread.CurrentThread.CurrentUICulture.Name;
            if (userLanguageName != "en-US" && e.Languages.IndexOf(userLanguageName) == -1)
            {
                e.Languages.Add(userLanguageName);
            }
        }
        void XafImportWindowsFormsApplication_DatabaseVersionMismatch(object sender, DevExpress.ExpressApp.DatabaseVersionMismatchEventArgs e)
        {
            string message = "Application cannot connect to the specified database.";

            CompatibilityDatabaseIsOldError isOldError = e.CompatibilityError as CompatibilityDatabaseIsOldError;
            if (isOldError != null && isOldError.Module != null)
            {
                message = "The client application cannot connect to the Middle Tier Application Server and its database. " +
                          "To avoid this error, ensure that both the client and the server have the same modules set. Problematic module: " + isOldError.Module.Name +
                          ". For more information, see https://docs.devexpress.com/eXpressAppFramework/113439/concepts/security-system/middle-tier-security-wcf-service#troubleshooting";
            }
            if (e.CompatibilityError == null)
            {
                message = "You probably tried to update the database in Middle Tier Security mode from the client side. " +
                          "In this mode, the server application updates the database automatically. " +
                          "To disable the automatic database update, set the XafApplication.DatabaseUpdateMode property to the DatabaseUpdateMode.Never value in the client application.";
            }
            throw new InvalidOperationException(message);
        }
    }
}
