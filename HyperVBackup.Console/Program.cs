/*
 *  Copyright 2012 Cloudbase Solutions Srl + 2016 Coliseo Software srl
 *
 *  This program is free software; you can redistribute it and/or modify
 *  it under the terms of the GNU Lesser General Public License as published by
 *  the Free Software Foundation; either version 3 of the License, or
 *  (at your option) any later version.
 *
 *  This program is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU Lesser General Public License for more details.
 *
 *  You should have received a copy of the GNU Lesser General Public License
 *  along with this program; if not, write to the Free Software
 *  Foundation, Inc., 51 Franklin St, Fifth Floor, Boston, MA  02110-1301  USA
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using CommandLine;
using CommandLine.Text;
using HyperVBackUp.Engine;
using NLog;

namespace HyperVBackup.Console
{
    static class Program
    {
        static volatile bool _cancel = false;
        static int _currentWidth = 0;
        static int _consoleWidth = 0;
        private static ILogger _logger;

        class Options
        {
            [Option('f', "file", HelpText = "Text file containing a list of VMs to backup, one per line. If not set all VM are included.", SetName = "flax")]
            public string File { get; set; }

            [Option('l', "list", Separator = ',', HelpText = "List of VMs to backup, comma separated. If not set all VM are included.", SetName = "flax")]
            public IList<string> List { get; set; }

            [Option('x', "exclude", Separator = ',', HelpText = "List of VMs to exclude from backup, comma separated. Use when all VM are included.", SetName = "flax")]
            public IList<string> Exclude { get; set; }

            [Option('v', "vhdinclude", Separator = ',', HelpText = "List of VHDs file names to backup, comma separated.")]
            public IList<string> VhdInclude { get; set; }

            [Option('i', "vhdignore", Separator = ',', HelpText = "List of VHDs file names to ignore, comma separated.")]
            public IList<string> VhdIgnore { get; set; }

            [Option('n', "name", HelpText = "If set, VMs to backup are specified by name.", SetName = "ng", Default = true)]
            public bool Name { get; set; }

            [Option('o', "output", Required = true, HelpText = "Backup ouput folder.")]
            public string Output { get; set; }

            [Option('p', "password", HelpText = "Secure the backup with a password.")]
            public string Password { get; set; }

            [Option('z', "zip", HelpText = "Use the zip format to store the backup insted of the 7zip format.", SetName = "zd")]
            public bool ZipFormat { get; set; }

            [Option('d', "directcopy", HelpText = "Do not compress the output, just copy the files recreating the folder structure.", SetName = "zd")]
            public bool DirectCopy { get; set; }

            [Option("outputformat", HelpText = "Backup archive name format. {0} is the VM's name, {1} the VM's GUID, {2} is the current date and time and {3} is the extension for the compression format (7z or zip). Default: \"{0}_{2:yyyyMMddHHmmss}{3}\"")]
            public string OutputFormat { get; set; } = "{0}_{2:yyyyMMddHHmmss}{3}";

            [Option('s', "singlevss", HelpText = "Perform one single snapshot for all the VMs.")]
            public bool SingleSnapshot { get; set; }

            [Option("compressionlevel", Default = -1, HelpText = "Compression level, between 0 (no compression, very fast) and 9 (max. compression, very slow).")]
            public int CompressionLevel { get; set; }

            [Option("cleanoutputbydays", Default = 0, HelpText = "Delete all files in the output folder older than x days. TOTALLY OPTIONAL. USE WITH CAUTION.")]
            public int CleanOutputDays { get; set; }

            [Option("cleanoutputbymb", Default = 0, HelpText = "Delete older files in the output folder if total size is bigger then x Megabytes. TOTALLY OPTIONAL. USE WITH CAUTION.")]
            public int CleanOutputMb { get; set; }

            [Option("onsuccess", HelpText = "Execute this program if backup completes correctly.")]
            public string OnSuccess { get; set; }

            [Option("onfailure", HelpText = "Execute this program if backup fails.")]
            public string OnFailure { get; set; }

            [Option("mt", HelpText = "Enable multi-threaded compression (only for 7zip format).")]
            public bool MultiThreaded { get; set; }
        }

        private static int Main(string[] args)
        {
            _logger = LogManager.GetCurrentClassLogger();
            Options parsedOptions = null;

            try
            {
                var stopwatch = new Stopwatch();
                stopwatch.Start();

                System.Console.WriteLine("HyperVBackup 3");
                System.Console.WriteLine("Copyright (C) 2012 Cloudbase Solutions SRL");
                System.Console.WriteLine("Copyright (C) 2016/2017 Coliseo Software SRL");

                _logger.Info("HyperVBackup started at {0}", DateTime.Now);

                if (!IsAdministrator())
                {
                    throw new UnauthorizedAccessException("HyperVBackup requires administrator permissions");
                }

                var parser = new Parser(ConfigureSettings);

                var result = parser.ParseArguments<Options>(args);

                var exitCode = result.MapResult(
                    opts =>
                    {
                        parsedOptions = opts;
                        return RunBackup(opts, stopwatch);
                    },
                    errs =>
                    {
                        System.Console.Error.WriteLine(GetUsage(result));
                        return 1;
                    });

                return exitCode;
            }
            catch (BackupCancelledException ex)
            {
                System.Console.Error.WriteLine(ex.Message);
                _logger.Error(ex.ToString());

                if (parsedOptions != null && !string.IsNullOrEmpty(parsedOptions.OnFailure))
                {
                    _logger.Info("Executing OnFailure program");
                    ExecuteProcess(parsedOptions.OnFailure, _logger);
                }

                return 3;
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine($"Error: {ex.Message}");
                System.Console.Error.WriteLine(ex.StackTrace);
                _logger.Error(ex.ToString());

                if (parsedOptions != null && !string.IsNullOrEmpty(parsedOptions.OnFailure))
                {
                    _logger.Info("Executing OnFailure program");
                    ExecuteProcess(parsedOptions.OnFailure, _logger);
                }

                return 2;
            }
        }

        private static int RunBackup(Options options, Stopwatch stopwatch)
        {
            GetConsoleWidth();
            System.Console.WriteLine();

            var vmNames = GetVmNames(options);

            if (vmNames == null)
            {
                _logger.Info("Backing up all VMs on this server");
            }

            if (!Directory.Exists(options.Output))
            {
                throw new DirectoryNotFoundException(
                    string.Format("The folder \"{0}\" is not valid", options.Output));
            }

            if (options.CleanOutputDays != 0)
            {
                CleanOutputByDays(options.Output, options.CleanOutputDays);
            }

            if (options.CleanOutputMb != 0)
            {
                CleanOutputByMegabytes(options.Output, options.CleanOutputMb);
            }

            var nameType = options.Name ? VmNameType.ElementName : VmNameType.SystemName;

            var mgr = new BackupManager();
            mgr.BackupProgress += MgrBackupProgress;

            System.Console.CancelKeyPress += Console_CancelKeyPress;

            var backupOptions = new HyperVBackUp.Engine.Options
            {
                CompressionLevel = options.CompressionLevel,
                Output = options.Output,
                OutputFormat = options.OutputFormat,
                SingleSnapshot = options.SingleSnapshot,
                VhdInclude = options.VhdInclude,
                VhdIgnore = options.VhdIgnore,
                Password = options.Password,
                ZipFormat = options.ZipFormat,
                DirectCopy = options.DirectCopy,
                MultiThreaded = options.MultiThreaded,
                Exclude = options.Exclude
            };

            var vmNamesMap = mgr.VssBackup(vmNames, nameType, backupOptions, _logger);

            var success = CheckRequiredVMs(vmNames, nameType, vmNamesMap);

            ShowElapsedTime(stopwatch);

            if (success)
            {
                if (!string.IsNullOrEmpty(options.OnSuccess))
                {
                    _logger.Info("Executing OnSucess program");
                    ExecuteProcess(options.OnSuccess, _logger);
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(options.OnFailure))
                {
                    _logger.Info("Executing OnFailure program");
                    ExecuteProcess(options.OnFailure, _logger);
                }
            }

            _logger.Info("HyperVBackup ended at {0}", DateTime.Now);

            return _cancel ? 3 : 0;
        }

        private static string GetUsage(ParserResult<Options> result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            var header = new StringBuilder();
            header.AppendLine();
            header.AppendLine("Note: short switchs use one dash (-) / long switches use two dashes (--). Example: HyperVBackup -l \"Mail Server\" --compressionlevel 0");
            header.AppendLine();

            var help = HelpText.AutoBuild(
                result,
                h =>
                {
                    h.AdditionalNewLineAfterOption = false;

                    if (header.Length > 0)
                    {
                        foreach (var line in header.ToString().Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                        {
                            h.AddPreOptionsLine(line);
                        }
                    }

                    return HelpText.DefaultParsingErrorsHandler(result, h);
                },
                e => e);

            return help;
        }

        private static void ExecuteProcess(string fileName, ILogger logger)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return;
            }

            using (var process = new Process())
            {
                logger.Debug("Executing program {0}", fileName);

                process.StartInfo.FileName = fileName;
                process.StartInfo.WorkingDirectory = Path.GetDirectoryName(fileName);
                process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.CreateNoWindow = true;

                process.Start();
                process.WaitForExit();

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                var exitCode = process.ExitCode;

                process.Close();

                logger.Info("Exit code of executing program {0} is {1}", fileName, exitCode);
                logger.Debug("Output of executing program {0} is {1}", fileName, output);
                logger.Debug("Errors of executing program {0} are {1}", fileName, error);
            }
        }

        private static void CleanOutputByDays(string output, int days)
        {
            foreach (var file in Directory.GetFiles(output))
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.LastWriteTime < DateTime.Now.AddDays(days * -1))
                {
                    _logger.Info("Deleting file {0}", fileInfo.Name);
                    fileInfo.Delete();
                }
            }
        }

        private static void CleanOutputByMegabytes(string output, int totalMb)
        {
            var dirInfo = new DirectoryInfo(output);
            var totalSize = dirInfo.EnumerateFiles().Sum(file => file.Length);
            var desiredMaxSize = (long)totalMb * 1024 * 1024;

            if (totalSize > desiredMaxSize)
            {
                var totalMbSize = totalSize / 1024 / 1024;
                _logger.Info("Size of output folder is {0} Megabytes, deleting some files ...", totalMbSize);

                var files = dirInfo.GetFiles();
                while (totalSize > desiredMaxSize && files.Length != 0)
                {
                    var sortedFiles = files.OrderBy(f => f.LastWriteTime).ToList();
                    var filetoDelete = sortedFiles[0].FullName;
                    _logger.Info("Deleting file {0}", filetoDelete);

                    try
                    {
                        File.Delete(filetoDelete);
                    }
                    catch (Exception e)
                    {
                        _logger.Error(e, "Cannot delete file {0}", filetoDelete);
                    }

                    files = dirInfo.GetFiles();
                    totalSize = files.Sum(file => file.Length);
                }
            }
        }

        private static void ConfigureSettings(ParserSettings settings)
        {
            // We print help ourselves in the parse error branch.
            settings.HelpWriter = null;

            // "Strict" behavior: do not ignore unknown arguments.
            settings.IgnoreUnknownArguments = false;
        }

        private static void ShowElapsedTime(Stopwatch stopwatch)
        {
            stopwatch.Stop();
            var ts = stopwatch.Elapsed;
            _logger.Info(
    "Elapsed time: {0:00}:{1:00}:{2:00}.{3:000}",
    ts.Hours, ts.Minutes, ts.Seconds, ts.Milliseconds);
        }

        private static bool CheckRequiredVMs(IEnumerable<string> vmNames, VmNameType nameType, IDictionary<string, string> vmNamesMap)
        {
            var withErrors = false;

            if (vmNames != null)
            {
                foreach (var vmName in vmNames)
                {
                    if (nameType == VmNameType.SystemName && !vmNamesMap.Keys.Contains(vmName, StringComparer.OrdinalIgnoreCase) ||
                        nameType == VmNameType.ElementName && !vmNamesMap.Values.Contains(vmName, StringComparer.OrdinalIgnoreCase))
                    {
                        _logger.Error("\"{0}\" not found", vmName);
                        withErrors = true;
                    }
                }
            }

            return !withErrors;
        }

        private static void GetConsoleWidth()
        {
            try
            {
                _consoleWidth = System.Console.WindowWidth;
            }
            catch (Exception)
            {
                _consoleWidth = 80;
            }
        }

        private static ICollection<string> GetVmNames(Options options)
        {
            ICollection<string> vmNames = null;

            if (options.File != null)
            {
                vmNames = File.ReadAllLines(options.File);
            }
            else if (options.List != null)
            {
                vmNames = options.List;
            }

            if (vmNames != null)
            {
                vmNames = (from o in vmNames where o.Trim().Length > 0 select o.Trim()).ToList();
            }

            return vmNames;
        }

        static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            System.Console.Error.WriteLine();
            System.Console.Error.WriteLine("Cancelling backup...");

            // Avoid CTRL+C during VSS snapshots
            _cancel = true;
            e.Cancel = true;
        }

        static void MgrBackupProgress(object sender, BackupProgressEventArgs e)
        {
            switch (e.Action)
            {
                case EventAction.InitializingVss:
                    _logger.Info("Initializing VSS");
                    break;

                case EventAction.StartingSnaphotSet:
                    _logger.Info("Starting snapshot set for:");

                    foreach (var componentName in e.Components.Values)
                    {
                        _logger.Info(componentName);
                    }

                    _logger.Info("");
                    _logger.Info("Volumes:");
                    foreach (var volumePath in e.VolumeMap.Keys)
                    {
                        System.Console.WriteLine(volumePath);
                    }
                    break;

                case EventAction.DeletingSnapshotSet:
                    _logger.Info("Deleting snapshot set");
                    break;

                case EventAction.StartingArchive:
                    _logger.Info("");
                    _logger.Info("Creating archive: \"{0}\"", e.AcrhiveFileName);
                    break;

                case EventAction.StartingEntry:
                    _logger.Info("Compressing entry: \"{0}\"", e.CurrentEntry);
                    _currentWidth = 0;
                    break;

                case EventAction.PercentProgress:
                    var progressWidth = e.PercentDone * _consoleWidth / 100;

                    for (var i = 0; i < progressWidth - _currentWidth; i++)
                    {
                        System.Console.Write(".");
                    }
                    _currentWidth = progressWidth;

                    if (e.PercentDone == 100)
                    {
                        System.Console.WriteLine();
                    }

                    _logger.Debug("Percent done: {0}%", e.PercentDone);
                    break;
            }

            e.Cancel = _cancel;
        }

        public static bool IsAdministrator()
        {
            return (new WindowsPrincipal(WindowsIdentity.GetCurrent()))
                .IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
