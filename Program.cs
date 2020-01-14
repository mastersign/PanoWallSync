using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Mastersign.PanoWall.Client
{
    class Program
    {
        const string BASE_URL = "https://panowall.net/";
        const int MAX_ERRORS = 3;

        private static WebClient webClient;
        private static bool cancelled;
        private static object consoleLock = new object();
        private static ManualResetEventSlim finishedEvent = new ManualResetEventSlim();

        [DllImport("Kernel32")]
        public static extern bool SetConsoleCtrlHandler(HandlerRoutine handler, bool add);

        public delegate bool HandlerRoutine(CtrlTypes ctrlType);

        public enum CtrlTypes
        {
            CTRL_C_EVENT = 0,
            CTRL_BREAK_EVENT,
            CTRL_CLOSE_EVENT,
            CTRL_LOGOFF_EVENT = 5,
            CTRL_SHUTDOWN_EVENT
        }

        static int Main(string[] args)
        {
            SetConsoleCtrlHandler(new HandlerRoutine(ConsoleCtrlCheck), true);
            var task = Run();
            task.Wait();
            return task.Result ? 0 : 1;
        }

        private async static Task<bool> Run()
        {
            var targetFolders = TargetFolders();
            if (targetFolders.Length == 0)
            {
                Console.WriteLine("No target folders found.");
                Console.WriteLine("");
                Console.WriteLine(
                    "Create a folder in the current directory for each resolution you wish to download.\n" +
                    "The folder must have a name like \"<screens>_<vertical respolution>p\".\n" +
                    "E.g. to download wallpapers for two horizontal WQHD screens, create the folder \"h2_1440p\".");
                return true;
            }
            else
            {
                Console.WriteLine("Found " + targetFolders.Length + " target folders.");
            }
            SetupWebClient();
            var errorCounter = 0;
            foreach (var targetFolder in targetFolders)
            {
                var setup = Path.GetFileName(targetFolder).ToLowerInvariant();
                Console.WriteLine("Retrieving image URLs for " + setup + "...");
                var imageUrls = await DownloadImageURLs(BASE_URL + "data/" + setup + ".txt");
                Console.WriteLine("Found " + imageUrls.Count + " image URLs.");
                var missingImageUrls = imageUrls.Where(url => !File.Exists(FileNameFromUrl(targetFolder, url))).ToList();
                if (missingImageUrls.Count == 0)
                {
                    Console.WriteLine("All images are already there.");
                }
                else
                {
                    Console.WriteLine("Downloading " + missingImageUrls.Count + " missing images...");
                }
                foreach (var imageUrl in missingImageUrls)
                {
                    if (!(await DownloadImage(targetFolder, imageUrl))) errorCounter++;
                    if (cancelled || errorCounter > MAX_ERRORS) break;
                }
                if (cancelled || errorCounter > MAX_ERRORS) break;
            }
            if (cancelled)
            {
                Console.WriteLine("Cancelled by user.");
                return false;
            }
            else if (errorCounter > 0)
            {
                Console.WriteLine("Finished with errors.");
                return false;
            }
            else
            {
                Console.WriteLine("Finished.");
                return true;
            }
        }

        private static bool ConsoleCtrlCheck(CtrlTypes ctrlType)
        {
            if (webClient.IsBusy)
            {
                webClient.CancelAsync();
            }
            cancelled = true;
            return true;
        }

        private static void SetupWebClient()
        {
            var wc = new WebClient();
            wc.DownloadProgressChanged += WebClientDownloadProgressHandler;
            wc.DownloadStringCompleted += WebClientDownloadCompletedHandler;
            wc.DownloadFileCompleted += WebClientDownloadCompletedHandler;
            webClient = wc;
        }

        private static void WebClientDownloadCompletedHandler(object sender, AsyncCompletedEventArgs e)
        {
            NotifyDownloadEnd(e.Cancelled);
        }

        private static int lastProgress = -1;
        private static string currentUrl = null;
        private static string currentTargetFile = null;

        private static void WebClientDownloadProgressHandler(object sender, DownloadProgressChangedEventArgs e)
        {
            NotifyDownloadProgress(e.ProgressPercentage);
        }

        private static void NotifyDownloadStart(string url, string targetFile = null)
        {
            BeginProgress();
            lock (consoleLock)
            {
                Console.Write(url + " [ 00% ]");
                Console.Out.Flush();
                lastProgress = 0;
                currentUrl = url;
                currentTargetFile = targetFile;
            }
        }

        private static void NotifyDownloadProgress(int progress)
        {
            lock (consoleLock)
            {
                if (currentUrl == null || progress == lastProgress || progress >= 99) return;
                Console.Write("{0}{1:00}% ]", new string('\b', 5), progress);
                Console.Out.Flush();
                lastProgress = progress;
            }
        }

        private static void NotifyDownloadEnd(bool cancelled)
        {
            lock (consoleLock)
            {
                var p = Console.CursorLeft;
                var back = new string('\b', p);
                var white = new string(' ', p);
                Console.Write(back + white + back);
                Console.Out.Flush();
                lastProgress = -1;
                currentUrl = null;
            }
            if (cancelled && currentTargetFile != null)
            {
                File.Delete(currentTargetFile);
            }
            currentTargetFile = null;
            EndProgress();
        }

        private static void BeginProgress() => finishedEvent.Reset();

        private static void EndProgress() => finishedEvent.Set();

        private static void WaitForProgressToEnd() => finishedEvent.Wait();

        private static string[] TargetFolders()
        {
            var pathRegex = new Regex(@"^(?:\w\d+)+_(?:\d+p)$");
            var cwd = Directory.GetCurrentDirectory();
            return Directory.EnumerateDirectories(cwd)
                .Where(p => pathRegex.IsMatch(Path.GetFileName(p)))
                .ToArray();            
        }

        private async static Task<IList<string>> DownloadImageURLs(string dataUrl)
        {
            NotifyDownloadStart(dataUrl);
            try
            {
                var result = (await webClient.DownloadStringTaskAsync(new Uri(dataUrl))).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                WaitForProgressToEnd();
                return result;
            }
            catch (Exception e)
            {
                WaitForProgressToEnd();
                Console.Error.WriteLine(e.Message);
                return new string[0];
            }
        }

        private async static Task<bool> DownloadImage(string targetFolder, string url)
        {
            var targetFile = FileNameFromUrl(targetFolder, url);
            if (!File.Exists(targetFile))
            {
                NotifyDownloadStart(url, targetFile);
                try
                {
                    await webClient.DownloadFileTaskAsync(new Uri(url), targetFile);
                    WaitForProgressToEnd();
                    return true;
                }
                catch (Exception e)
                {
                    WaitForProgressToEnd();
                    Console.Error.WriteLine(e.Message);
                    return false;
                }
            }
            else
            {
                return true;
            }
        }

        private static string FileNameFromUrl(string targetFolder, string imageUrl)
        {
            var uri = new Uri(imageUrl);
            var fileName = Path.GetFileName(uri.AbsolutePath);
            return Path.Combine(targetFolder, fileName);
        }
    }
}
