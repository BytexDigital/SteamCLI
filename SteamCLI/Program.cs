using BytexDigital.Steam.ContentDelivery;
using BytexDigital.Steam.ContentDelivery.Exceptions;
using BytexDigital.Steam.ContentDelivery.Models;
using BytexDigital.Steam.ContentDelivery.Models.Downloading;
using BytexDigital.Steam.Core;
using BytexDigital.Steam.Core.Enumerations;

using CommandLine;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BytexDigital.Steam.Clients.CLI
{
    class Program
    {
        public class Options
        {
            public const string ANONYMOUS_USERNAME = "anonymous";

            [Option("username", Required = true, HelpText = "Username to use when logging into Steam. Use \"" + ANONYMOUS_USERNAME + "\" for anonymous login.")]
            public string Username { get; set; }

            [Option("password", Required = true, HelpText = "Password to use when logging into Steam.")]
            public string Password { get; set; }

            [Option("sentrydir", Required = false, HelpText = "Directory in which sentry files will be saved.", Default = ".\\sentries")]
            public string SentryDirectory { get; set; }

            [Option("targetdir", HelpText = "Specifies a directory to perform an action in.")]
            public string TargetDirectory { get; set; }

            [Option("branch", HelpText = "Specifies a product branch.")]
            public string Branch { get; set; }

            [Option("branchpassword", HelpText = "Specifies a product banch password.")]
            public string BranchPassword { get; set; }

            [Option("os", Required = false, HelpText = "Specifies an operating system.")]
            public string OS { get; set; }

            [Option("workers", HelpText = "Specifies how many download workers should work on one download task at a time.", Default = 50)]
            public int WorkerCount { get; set; }

            [Option("buffersize", HelpText = "Specifies how big the buffer for downloaded chunks is (in bytes) before being written to disk.", Default = 3221225472)]
            public ulong ChunkBufferSizeBytes { get; set; }

            [Option("bufferusagethreshold", HelpText = "Specifies how empty the chunk buffer has to be before writing to it again.", Default = 1)]
            public double ChunkBufferUsageThreshold { get; set; }

            [Option("appid", HelpText = "Specifies an app ID to use.")]
            public uint? AppId { get; set; }

            [Option("depotid", HelpText = "Specifies an depot ID to use.")]
            public uint? DepotId { get; set; }

            [Option("synctarget", HelpText = "If enabled, only changed files in the target directory will be downloaded (only changed files will be deleted before download).")]
            public bool SyncTarget { get; set; }

            [Option("enablesyncdelete", HelpText = "If enabled, synctarget will also delete files that are not in the original download, making the download target folder a 1 to 1 copy of the original.")]
            public bool SyncTargetDeleteEnabled { get; set; }

            [Option("manifestid", HelpText = "Specifies an manifest ID to use.")]
            public ulong? ManifestId { get; set; }

            [Option("workshop-download", HelpText = "If set, downloads a workshop item.", Default = false)]
            public bool DownloadWorkshopItem { get; set; }

            [Option("workshopid", HelpText = "Specifies a workshop item ID to use.")]
            public uint? WorkshopFileId { get; set; }

            [Option("app-download", HelpText = "If set, downloads an application.", Default = false)]
            public bool DownloadApp { get; set; }

        }

        private static SteamClient _steamClient = null;
        private static SteamContentClient _steamContentClient = null;
        private static SteamCredentials _steamCredentials = null;


        static async Task Main(string[] args)
        {
            var result = await Parser.Default.ParseArguments<Options>(args)
                .WithParsedAsync(RunOptions);

            result.WithNotParsed(HandleParsingError);
        }

        private class AuthCodeProvider : SteamAuthenticationCodesProvider
        {
            public override string GetEmailAuthenticationCode(SteamCredentials steamCredentials)
            {
                Console.Write("Please enter your email auth code: ");

                string input = Console.ReadLine();

                Console.Write("Retrying... ");

                return input;
            }

            public override string GetTwoFactorAuthenticationCode(SteamCredentials steamCredentials)
            {
                Console.Write("Please enter your 2FA code: ");

                string input = Console.ReadLine();

                Console.Write("Retrying... ");

                return input;
            }
        }

        static async Task RunOptions(Options opt)
        {
            SteamAuthenticationFilesProvider sentryFileProvider = default;

            if (!string.IsNullOrEmpty(opt.SentryDirectory))
            {
                sentryFileProvider = new DirectorySteamAuthenticationFilesProvider(opt.SentryDirectory);
            }

            if (opt.Username == Options.ANONYMOUS_USERNAME)
            {
                _steamCredentials = SteamCredentials.Anonymous;
            }
            else
            {
                _steamCredentials = new SteamCredentials(opt.Username, opt.Password);
            }

            _steamClient = new SteamClient(_steamCredentials, new AuthCodeProvider(), sentryFileProvider);
            _steamContentClient = new SteamContentClient(_steamClient, null, opt.WorkerCount, opt.ChunkBufferSizeBytes, opt.ChunkBufferUsageThreshold);

            if (string.IsNullOrEmpty(opt.OS))
            {
                opt.OS = _steamClient.GetSteamOs().Identifier;
            }

            Console.Write($"Connecting to Steam as \"{opt.Username}\"... ");

            try
            {
                await _steamClient.ConnectAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed! Error: {ex.Message}");

                if (ex is SteamLogonException logonEx)
                {
                    if (logonEx.Result == SteamKit2.EResult.InvalidPassword)
                    {
                        Console.WriteLine($"Warning: The logon may have failed due to expired sentry-data. " +
                            $"If you are sure, that the provided username and password are correct, consider deleting the .bin and .key file for the user \"{_steamClient.Credentials.Username}\" in the sentries directory.");
                    }
                }

                Environment.Exit(3);
            }

            Console.WriteLine("OK.");

            if (opt.DownloadApp)
            {
                await DownloadApp(opt);
            }
            else if (opt.DownloadWorkshopItem)
            {
                await DownloadWorkshopItem(opt);
            }
            else
            {
                Console.WriteLine("No action to run specified, exiting.");
            }

            _steamClient.Shutdown();
        }

        static void HandleParsingError(IEnumerable<Error> errors)
        {
            Console.WriteLine("Error parsing arguments");
            Environment.Exit(1);
        }

        static void SyncDeleteRemovedFiles(Options opt, Manifest manifest)
        {
            Console.WriteLine("Checking for unnecessary files in target directory...");

            foreach (var localFilePath in Directory.GetFiles(opt.TargetDirectory, "*", SearchOption.AllDirectories))
            {
                var relativeLocalPath = Path.GetRelativePath(opt.TargetDirectory, localFilePath);

                if (manifest.Files.Count(x => x.FileName.ToLowerInvariant() == relativeLocalPath.ToLowerInvariant()) == 0)
                {
                    Console.WriteLine($"Deleting local file {relativeLocalPath}");
                    File.Delete(localFilePath);
                }
            }
        }

        static async Task DownloadApp(Options opt)
        {
            if (string.IsNullOrEmpty(opt.TargetDirectory))
            {
                Console.WriteLine("Error: Please specify a target directory.");
                Environment.Exit(201);
            }

            if (!opt.AppId.HasValue)
            {
                Console.WriteLine("Error: Please specify an app id.");
                Environment.Exit(202);
            }

            if (!opt.AppId.HasValue)
            {
                Console.WriteLine("Error: Please specify an app id.");
                Environment.Exit(202);
            }

            if (!opt.DepotId.HasValue)
            {
                Console.WriteLine("Error: Please specify a depot id.");
                Environment.Exit(203);
            }

            if (string.IsNullOrEmpty(opt.OS))
            {
                opt.OS = _steamClient.GetSteamOs().Identifier;

                Console.WriteLine($"Warning: No OS was specified, defaulting to current OS = {opt.OS}");
            }

            if (string.IsNullOrEmpty(opt.Branch))
            {
                opt.Branch = "public";

                Console.WriteLine($"Warning: No branch was specified, defaulting to branch = {opt.Branch}");
            }

            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                SteamOs steamOs = new SteamOs(opt.OS);

                if (opt.SyncTargetDeleteEnabled)
                {
                    if (!opt.ManifestId.HasValue)
                    {
                        opt.ManifestId = await _steamContentClient.GetDepotDefaultManifestIdAsync(opt.AppId.Value, opt.DepotId.Value, opt.Branch, opt.BranchPassword);
                    }

                    Manifest manifest = await _steamContentClient.GetManifestAsync(opt.AppId.Value, opt.DepotId.Value, opt.ManifestId.Value);

                    SyncDeleteRemovedFiles(opt, manifest);
                }

                Console.Write($"Attempting to start download of app {opt.AppId.Value}, depot {opt.DepotId}... ");

                var downloadHandler = await _steamContentClient.GetAppDataAsync(opt.AppId.Value, opt.DepotId.Value, opt.ManifestId, opt.Branch, opt.BranchPassword, opt.OS);

                await Download(downloadHandler, opt);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}{(ex.InnerException != null ? $" Inner Exception: {ex.InnerException.Message}" : "")}");
                Environment.Exit(210);
            }

            sw.Stop();
            Console.WriteLine($"Download completed, it took {sw.Elapsed:hh\\:mm\\:ss}");
        }

        static async Task DownloadWorkshopItem(Options opt)
        {
            if (string.IsNullOrEmpty(opt.TargetDirectory))
            {
                Console.WriteLine("Error: Please specify a target directory.");
                Environment.Exit(101);
            }

            if (!opt.WorkshopFileId.HasValue)
            {
                Console.WriteLine("Error: Please specify a workshop item id.");
                Environment.Exit(102);
            }

            if (string.IsNullOrEmpty(opt.Branch))
            {
                opt.Branch = "public";
                //Console.WriteLine($"Warning: No branch was specified, using default branch = {opt.Branch}");
            }

            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                SteamOs steamOs = new SteamOs(opt.OS);

                if (opt.SyncTargetDeleteEnabled)
                {
                    if (!opt.ManifestId.HasValue)
                    {
                        opt.ManifestId = (await _steamContentClient.GetPublishedFileDetailsAsync(opt.WorkshopFileId.Value)).hcontent_file;
                    }

                    Manifest manifest = await _steamContentClient.GetManifestAsync(opt.AppId.Value, opt.AppId.Value, opt.ManifestId.Value);

                    SyncDeleteRemovedFiles(opt, manifest);
                }

                Console.Write($"Attempting to start download of item {opt.WorkshopFileId.Value}... ");

                var downloadHandler = await _steamContentClient.GetPublishedFileDataAsync(
                    opt.WorkshopFileId.Value,
                    opt.ManifestId,
                    opt.Branch,
                    opt.BranchPassword,
                    steamOs);

                await Download(downloadHandler, opt);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}{(ex.InnerException != null ? $" Inner Exception: {ex.InnerException.Message}" : "")}");
                Environment.Exit(110);
            }

            sw.Stop();
            Console.WriteLine($"Download completed, it took {sw.Elapsed:hh\\:mm\\:ss}");
        }

        static async Task Download(IDownloadHandler downloadHandler, Options opt)
        {
            Task downloadTask = default;
            bool showSyncNotSupportedWarning = false;

            if (opt.SyncTarget)
            {
                if (downloadHandler is MultipleFilesHandler multipleFilesHandler)
                {
                    Console.Write("Hashing local files (this may take some time)... ");
                    downloadTask = multipleFilesHandler.DownloadChangesToFolderAsync(opt.TargetDirectory);
                }
                else
                {
                    showSyncNotSupportedWarning = true;
                }
            }

            if (downloadTask == default)
            {
                downloadTask = downloadHandler.DownloadToFolderAsync(opt.TargetDirectory);
            }

            Console.WriteLine("OK.");

            if (showSyncNotSupportedWarning)
            {
                Console.WriteLine("Warning: Sync was enabled, but the app/item type being download does not support this operation.");
            }

            Console.WriteLine($"Downloading {downloadHandler.TotalFileCount} files with total size of {BytesToDisplayText(downloadHandler.TotalFileSize)}...");

            while (!downloadTask.IsCompleted)
            {
                var delayTask = Task.Delay(500);
                var t = await Task.WhenAny(delayTask, downloadTask);

                Console.WriteLine($"Progress {downloadHandler.TotalProgress * 100:00.00}%, buffer usage {downloadHandler.BufferUsage * 100:00.00}%");
            }

            await downloadTask;
        }

        static string BytesToDisplayText(double len)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }
    }
}
