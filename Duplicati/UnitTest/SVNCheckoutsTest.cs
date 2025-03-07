// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.


using System;
using System.Collections.Generic;
using System.Text;
using Duplicati.Library.Logging;
using Duplicati.Library.Utility;
using System.Linq;
using Duplicati.Library.Common.IO;
using System.Threading.Tasks;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// This class encapsulates a simple method for testing the correctness of duplicati.
    /// </summary>
    public class SVNCheckoutTest
    {
        /// <summary>
        /// The log tag
        /// </summary>
        private static readonly string LOGTAG = Library.Logging.Log.LogTagFromType<SVNCheckoutTest>();

        /// <summary>
        /// A helper class to write debug messages to the log file
        /// </summary>
        private class LogHelper : StreamLogDestination
        {
            public static long WarningCount = 0;
            public static long ErrorCount = 0;

            public LogHelper(string file)
                : base(file)
            { }

            public override void WriteMessage(LogEntry entry)
            {
                if (entry.Level == LogMessageType.Error)
                    System.Threading.Interlocked.Increment(ref ErrorCount);
                else if (entry.Level == LogMessageType.Warning)
                    System.Threading.Interlocked.Increment(ref WarningCount);
                base.WriteMessage(entry);
            }
        }

        /// <summary>
        /// Running the unit test confirms the correctness of duplicati
        /// </summary>
        /// <param name="folders">The folders to backup. Folder at index 0 is the base, all others are incrementals</param>
        /// <param name="target">The target destination for the backups</param>
        public static void RunTest(string[] folders, Dictionary<string, string> options, string target)
        {
            string tempdir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "tempdir");
            string logfilename = System.IO.Path.Combine(tempdir, string.Format("unittest-{0}.log", Library.Utility.Utility.SerializeDateTime(DateTime.Now)));

            try
            {
                if (System.IO.Directory.Exists(tempdir))
                    System.IO.Directory.Delete(tempdir, true);

                System.IO.Directory.CreateDirectory(tempdir);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to clean tempdir: {0}", ex);
            }

            using (var log = new LogHelper(logfilename))
            using (Log.StartScope(log, LogMessageType.Profiling))
            {

                //Filter empty entries, commonly occuring with copy/paste and newlines
                folders = (from x in folders
                           where !string.IsNullOrWhiteSpace(x)
                           select Environment.ExpandEnvironmentVariables(x)).ToArray();

                foreach (var f in folders)
                    foreach (var n in f.Split(new char[] { System.IO.Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
                        if (!System.IO.Directory.Exists(n))
                            throw new Exception(string.Format("Missing source folder: {0}", n));


                Duplicati.Library.Utility.TempFolder.SystemTempPath = tempdir;

                //Set some defaults
                if (!options.ContainsKey("passphrase"))
                    options["passphrase"] = "secret password!";

                if (!options.ContainsKey("prefix"))
                    options["prefix"] = "duplicati_unittest";

                //We want all messages in the log
                options["log-file-log-level"] = LogMessageType.Profiling.ToString();
                //We cannot rely on USN numbering, but we can use USN enumeration
                //options["disable-usn-diff-check"] = "true";

                //We use precise times
                options["disable-time-tolerance"] = "true";

                //We need all sets, even if they are unchanged
                options["upload-unchanged-backups"] = "true";

                bool skipfullrestore = false;
                bool skippartialrestore = false;
                bool skipverify = false;

                if (Utility.ParseBoolOption(options, "unittest-backuponly"))
                {
                    skipfullrestore = true;
                    skippartialrestore = true;
                    options.Remove("unittest-backuponly");
                }

                if (Utility.ParseBoolOption(options, "unittest-skip-partial-restore"))
                {
                    skippartialrestore = true;
                    options.Remove("unittest-skip-partial-restore");
                }

                if (Utility.ParseBoolOption(options, "unittest-skip-full-restore"))
                {
                    skipfullrestore = true;
                    options.Remove("unittest-skip-full-restore");
                }

                if (Utility.ParseBoolOption(options, "unittest-skip-verify"))
                {
                    skipverify = true;
                    options.Remove("unittest-skip-verify");
                }

                var verifymetadata = !Utility.ParseBoolOption(options, "skip-metadata");

                using (new Timer(LOGTAG, "UnitTest", "Total unittest"))
                using (TempFolder tf = new TempFolder())
                {
                    options["dbpath"] = System.IO.Path.Combine(tempdir, "unittest.sqlite");
                    if (System.IO.File.Exists(options["dbpath"]))
                        System.IO.File.Delete(options["dbpath"]);

                    if (string.IsNullOrEmpty(target))
                    {
                        target = "file://" + tf;
                    }
                    else
                    {
                        BasicSetupHelper.ProgressWriteLine("Removing old backups");
                        Dictionary<string, string> tmp = new Dictionary<string, string>(options);
                        tmp["keep-versions"] = "0";
                        tmp["force"] = "";
                        tmp["allow-full-removal"] = "";

                        using (new Timer(LOGTAG, "CleanupExisting", "Cleaning up any existing backups"))
                            try
                            {
                                using (var bk = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend(target, options))
                                    foreach (var f in bk.ListAsync(System.Threading.CancellationToken.None).ToBlockingEnumerable())
                                        if (!f.IsFolder)
                                            Utility.Await(bk.DeleteAsync(f.Name, System.Threading.CancellationToken.None));
                            }
                            catch (Duplicati.Library.Interface.FolderMissingException)
                            {
                            }
                    }

                    string fhtempsource = null;

                    bool usingFHWithRestore = (!skipfullrestore || !skippartialrestore);

                    using (var fhsourcefolder = usingFHWithRestore ? new Library.Utility.TempFolder() : null)
                    {
                        if (usingFHWithRestore)
                        {
                            fhtempsource = fhsourcefolder;
                            TestUtils.CopyDirectoryRecursive(folders[0], fhsourcefolder);
                        }

                        RunBackup(usingFHWithRestore ? (string)fhsourcefolder : folders[0], target, options, folders[0]);

                        for (int i = 1; i < folders.Length; i++)
                        {
                            //options["passphrase"] = "bad password";
                            //If the backups are too close, we can't pick the right one :(
                            System.Threading.Thread.Sleep(1000 * 5);

                            if (usingFHWithRestore)
                            {
                                System.IO.Directory.Delete(fhsourcefolder, true);
                                TestUtils.CopyDirectoryRecursive(folders[i], fhsourcefolder);
                            }

                            //Call function to simplify profiling
                            RunBackup(usingFHWithRestore ? (string)fhsourcefolder : folders[i], target, options, folders[i]);
                        }
                    }

                    Duplicati.Library.Main.Options opts = new Duplicati.Library.Main.Options(options);
                    using (Duplicati.Library.Interface.IBackend bk = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend(target, options))
                        foreach (Duplicati.Library.Interface.IFileEntry fe in bk.ListAsync(System.Threading.CancellationToken.None).ToBlockingEnumerable())
                            if (fe.Size > opts.VolumeSize)
                            {
                                string msg = string.Format("The file {0} is {1} bytes larger than allowed", fe.Name, fe.Size - opts.VolumeSize);
                                BasicSetupHelper.ProgressWriteLine(msg);
                                Log.WriteErrorMessage(LOGTAG, "RemoteTargetSize", null, msg);
                            }

                    IList<DateTime> entries;
                    using (var console = new CommandLine.ConsoleOutput(Console.Out, options))
                    using (var i = new Duplicati.Library.Main.Controller(target, options, console))
                        entries = (from n in i.List().Filesets select n.Time.ToLocalTime()).ToList();

                    if (entries.Count != folders.Length)
                    {
                        StringBuilder sb = new StringBuilder();
                        sb.AppendLine("Entry count: " + entries.Count.ToString());
                        sb.Append(string.Format("Found {0} filelists but there were {1} source folders", entries.Count, folders.Length));
                        throw new Exception("Filename parsing problem, or corrupt storage: " + sb);
                    }

                    if (!skipfullrestore || !skippartialrestore)
                    {
                        for (int i = 0; i < entries.Count; i++)
                        {
                            using (TempFolder ttf = new TempFolder())
                            {
                                BasicSetupHelper.ProgressWriteLine("Restoring the copy: " + folders[i]);

                                options["time"] = entries[entries.Count - i - 1].ToString();

                                string[] actualfolders = folders[i].Split(System.IO.Path.PathSeparator);
                                if (!skippartialrestore)
                                {
                                    BasicSetupHelper.ProgressWriteLine("Partial restore of: " + folders[i]);
                                    using (TempFolder ptf = new TempFolder())
                                    {
                                        List<string> testfiles = new List<string>();
                                        using (new Timer(LOGTAG, "ExtractFileList", "Extract list of files from" + folders[i]))
                                        {
                                            List<string> sourcefiles;
                                            using (var console = new CommandLine.ConsoleOutput(Console.Out, options))
                                            using (var inst = new Library.Main.Controller(target, options, console))
                                                sourcefiles = (from n in inst.List("*").Files select n.Path).ToList();

                                            //Remove all folders from list
                                            for (int j = 0; j < sourcefiles.Count; j++)
                                                if (sourcefiles[j].EndsWith(Util.DirectorySeparatorString, StringComparison.Ordinal))
                                                {
                                                    sourcefiles.RemoveAt(j);
                                                    j--;
                                                }


                                            int testfilecount = 15;
                                            Random r = new Random();
                                            while (testfilecount-- > 0 && sourcefiles.Count > 0)
                                            {
                                                int rn = r.Next(0, sourcefiles.Count);
                                                testfiles.Add(sourcefiles[rn]);
                                                sourcefiles.RemoveAt(rn);
                                            }

                                        }


                                        //Add all folders to avoid warnings in restore log
                                        int c = testfiles.Count;
                                        Dictionary<string, string> partialFolders = new Dictionary<string, string>(Utility.ClientFilenameStringComparer);

                                        for (int j = 0; j < c; j++)
                                        {
                                            string f = testfiles[j];

                                            if (!f.StartsWith(usingFHWithRestore ? fhtempsource : folders[i], Utility.ClientFilenameStringComparison))
                                                throw new Exception(string.Format("Unexpected file found: {0}, path is not a subfolder for {1}", f, folders[i]));

                                            f = f.Substring(Util.AppendDirSeparator(usingFHWithRestore ? fhtempsource : folders[i]).Length);

                                            do
                                            {
                                                f = System.IO.Path.GetDirectoryName(f);
                                                partialFolders[Util.AppendDirSeparator(f)] = null;
                                            } while (f.IndexOf(System.IO.Path.DirectorySeparatorChar) > 0);
                                        }

                                        if (partialFolders.ContainsKey(""))
                                            partialFolders.Remove("");
                                        if (partialFolders.ContainsKey(Util.DirectorySeparatorString))
                                            partialFolders.Remove(Util.DirectorySeparatorString);

                                        List<string> filterlist;

                                        var tfe = Util.AppendDirSeparator(usingFHWithRestore ? fhtempsource : folders[i]);

                                        filterlist = (from n in partialFolders.Keys
                                                      where !string.IsNullOrWhiteSpace(n) && n != Util.DirectorySeparatorString
                                                      select Util.AppendDirSeparator(System.IO.Path.Combine(tfe, n)))
                                                      .Union(testfiles) //Add files with full path
                                                      .Union(new string[] { tfe }) //Ensure root folder is included
                                                      .Distinct()
                                                      .ToList();

                                        testfiles = (from n in testfiles select n.Substring(tfe.Length)).ToList();

                                        //Call function to simplify profiling
                                        RunPartialRestore(folders[i], target, ptf, options, filterlist.ToArray());

                                        if (!skipverify)
                                        {
                                            //Call function to simplify profiling
                                            BasicSetupHelper.ProgressWriteLine("Verifying partial restore of: " + folders[i]);
                                            VerifyPartialRestore(folders[i], testfiles, actualfolders, ptf, folders[0], verifymetadata);
                                        }
                                    }
                                }

                                if (!skipfullrestore)
                                {
                                    //Call function to simplify profiling
                                    RunRestore(folders[i], target, ttf, options);

                                    if (!skipverify)
                                    {
                                        //Call function to simplify profiling
                                        BasicSetupHelper.ProgressWriteLine("Verifying the copy: " + folders[i]);
                                        VerifyFullRestore(folders[i], actualfolders, new string[] { ttf }, verifymetadata);
                                    }
                                }
                            }
                        }
                    }

                    foreach (string s in Utility.EnumerateFiles(tempdir))
                    {
                        if (s == options["dbpath"])
                            continue;
                        if (s == logfilename)
                            continue;
                        if (s.StartsWith(Util.AppendDirSeparator(tf), StringComparison.Ordinal))
                            continue;

                        Log.WriteWarningMessage(LOGTAG, "LeftOverTempFile", null, "Found left-over temp file: {0}", s.Substring(tempdir.Length));
                        BasicSetupHelper.ProgressWriteLine("Found left-over temp file: {0} -> {1}", s.Substring(tempdir.Length),
#if DEBUG
                        TempFile.GetStackTraceForTempFile(System.IO.Path.GetFileName(s))
#else
                        System.IO.Path.GetFileName(s)
#endif
                    );
                    }

                    foreach (string s in Utility.EnumerateFolders(tempdir))
                        if (!s.StartsWith(Util.AppendDirSeparator(tf), StringComparison.Ordinal) && Util.AppendDirSeparator(s) != Util.AppendDirSeparator(tf) && Util.AppendDirSeparator(s) != Util.AppendDirSeparator(tempdir))
                        {
                            Log.WriteWarningMessage(LOGTAG, "LeftOverTempFolder", null, "Found left-over temp folder: {0}", s.Substring(tempdir.Length));
                            BasicSetupHelper.ProgressWriteLine("Found left-over temp folder: {0}", s.Substring(tempdir.Length));
                        }
                }
            }

            if (LogHelper.ErrorCount > 0)
                BasicSetupHelper.ProgressWriteLine("Unittest completed, but with {0} errors, see logfile for details", LogHelper.ErrorCount);
            else if (LogHelper.WarningCount > 0)
                BasicSetupHelper.ProgressWriteLine("Unittest completed, but with {0} warnings, see logfile for details", LogHelper.WarningCount);
            else
                BasicSetupHelper.ProgressWriteLine("Unittest completed successfully - Have some cake!");

            System.Diagnostics.Debug.Assert(LogHelper.ErrorCount == 0);
        }

        private static void VerifyPartialRestore(string source, IEnumerable<string> testfiles, string[] actualfolders, string tempfolder, string rootfolder, bool verifymetadata)
        {
            using (new Timer(LOGTAG, "PartialRestoreVerify", "Verification of partial restore from " + source))
                foreach (string s in testfiles)
                {
                    string restoredname;
                    string sourcename;

                    if (actualfolders.Length == 1)
                    {
                        sourcename = System.IO.Path.Combine(actualfolders[0], s);
                        restoredname = System.IO.Path.Combine(tempfolder, s);
                    }
                    else
                    {
                        int six = s.IndexOf(System.IO.Path.DirectorySeparatorChar);
                        sourcename = System.IO.Path.Combine(actualfolders[int.Parse(s.Substring(0, six))], s.Substring(six + 1));
                        restoredname = System.IO.Path.Combine(System.IO.Path.Combine(tempfolder, System.IO.Path.GetFileName(rootfolder.Split(System.IO.Path.PathSeparator)[int.Parse(s.Substring(0, six))])), s.Substring(six + 1));
                    }

                    if (!System.IO.File.Exists(sourcename))
                    {
                        Log.WriteErrorMessage(LOGTAG, "PartialRestoreMissingFile", null, "Partial restore, missing SOURCE file: {0}", sourcename);
                        BasicSetupHelper.ProgressWriteLine("Partial restore missing file: " + sourcename);
                        throw new Exception("Unittest is broken");
                    }

                    if (!System.IO.File.Exists(restoredname))
                    {
                        Log.WriteErrorMessage(LOGTAG, "PartialRestoreMissingFile", null, "Partial restore missing file: {0}", restoredname);
                        BasicSetupHelper.ProgressWriteLine("Partial restore missing file: " + restoredname);
                    }
                    else
                    {
                        TestUtils.AssertFilesAreEqual(sourcename, restoredname, verifymetadata, $"Partial restore file differs: {s}");
                    }
                }
        }

        private static void VerifyFullRestore(string source, string[] actualfolders, string[] restorefoldernames, bool verifymetadata)
        {
            using (new Timer(LOGTAG, "SourceVerification", "Verification of " + source))
            {
                for (int j = 0; j < actualfolders.Length; j++)
                    TestUtils.AssertDirectoryTreesAreEquivalent(actualfolders[j], restorefoldernames[j], verifymetadata, "VerifyFullRestore");
            }
        }

        private static void RunBackup(string source, string target, Dictionary<string, string> options, string sourcename)
        {
            BasicSetupHelper.ProgressWriteLine("Backing up the copy: " + sourcename);
            using (new Timer(LOGTAG, "BackupRun", "Backup of " + sourcename))
            using (var console = new CommandLine.ConsoleOutput(Console.Out, options))
            using (var i = new Duplicati.Library.Main.Controller(target, options, console))
                Log.WriteInformationMessage(LOGTAG, "BackupOutput", i.Backup(source.Split(System.IO.Path.PathSeparator)).ToString());
        }

        private static void RunRestore(string source, string target, string tempfolder, Dictionary<string, string> options)
        {
            var tops = new Dictionary<string, string>(options);
            tops["restore-path"] = tempfolder;
            using (new Timer(LOGTAG, "RestoreRun", "Restore of " + source))
            using (var console = new CommandLine.ConsoleOutput(Console.Out, options))
            using (var i = new Duplicati.Library.Main.Controller(target, tops, console))
                Log.WriteInformationMessage(LOGTAG, "RestoreOutput", i.Restore(null).ToString());
        }

        private static void RunPartialRestore(string source, string target, string tempfolder, Dictionary<string, string> options, string[] files)
        {
            var tops = new Dictionary<string, string>(options);
            tops["restore-path"] = tempfolder;
            using (new Timer(LOGTAG, "PartialRestore", "Partial restore of " + source))
            using (var console = new CommandLine.ConsoleOutput(Console.Out, options))
            using (var i = new Duplicati.Library.Main.Controller(target, tops, console))
                Log.WriteInformationMessage(LOGTAG, "PartialRestoreOutput", i.Restore(files).ToString());
        }
    }
}
