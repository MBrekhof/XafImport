using System.Diagnostics;
using System.IO;

namespace XafImport.Module
{
    public class Initialization
    {
        const string middleTierProjectName = "XafImport.MiddleTier";
        static void KillServerProcess()
        {
            var process = Process.GetProcessesByName(middleTierProjectName).FirstOrDefault();
            if (process != null)
            {
                process.Kill();
                process.WaitForExit();
            }
        }
        static string FindMiddleTierExecutable(string startFolderPath)
        {
            string result = null;
            var currentFolderInfo = new DirectoryInfo(startFolderPath);
            for (int i = 0; i < 5; i++)
            {
                if (currentFolderInfo == null)
                {
                    break;
                }
                try
                {
                    var foundFileInfo = currentFolderInfo.EnumerateFiles(middleTierProjectName + ".exe", SearchOption.AllDirectories).FirstOrDefault();
                    if ((foundFileInfo != null) && foundFileInfo.Exists)
                    {
                        result = foundFileInfo.FullName;
                        break;
                    }
                }
                catch { }
                currentFolderInfo = currentFolderInfo.Parent;
            }
            return result;
        }
        public static Process RunSecurityServer()
        {
            KillServerProcess();
            var startSearchPath = AppDomain.CurrentDomain.SetupInformation.ApplicationBase;
            var fileName = FindMiddleTierExecutable(startSearchPath);
            if (string.IsNullOrEmpty(fileName) || !File.Exists(fileName))
            {
                throw new FileNotFoundException(
                    $"Could not start a server process. The {middleTierProjectName}.exe file is missing.\r\n" +
                    $"Please ensure that you have built the {middleTierProjectName} project.");
            }
            var process = new Process();
            process.StartInfo.UseShellExecute = true;
            process.StartInfo.WindowStyle = ProcessWindowStyle.Minimized;
            process.StartInfo.FileName = fileName;
            process.StartInfo.WorkingDirectory = Path.GetDirectoryName(fileName);
            process.Start();
            return process;
        }
    }
}
