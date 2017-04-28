﻿using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    public class MountTests : TestsWithEnlistmentPerFixture
    {
        private const int ProjectGitIndexOnDiskVersion = 4;
        private const int AlwaysExcludeOnDiskVersion = 5;
        private const int GVFSGenericError = 3;
        private const uint GenericRead = 2147483648;
        private const uint FileFlagBackupSemantics = 3355443;
        private const string IndexLockPath = ".git\\index.lock";
        private const string ExcludePath = ".git\\info\\exclude";
        private const string AlwaysExcludePath = ".git\\info\\always_exclude";

        private FileSystemRunner fileSystem;

        public MountTests()
        {
            this.fileSystem = new SystemIORunner();
        }

        [TestCaseSource(typeof(MountSubfolders), MountSubfolders.MountFolders)]
        public void SecondMountAttemptFails(string mountSubfolder)
        {
            this.MountShouldFail(0, "already mounted", this.Enlistment.GetVirtualPathTo(mountSubfolder));
        }

        [TestCase]
        public void MountFailsOutsideEnlistment()
        {
            this.MountShouldFail("is not a valid GVFS enlistment", Path.GetDirectoryName(this.Enlistment.EnlistmentRoot));
        }

        [TestCase]
        public void MountCopiesMissingReadObjectHook()
        {
            this.Enlistment.UnmountGVFS();

            string readObjectPath = this.Enlistment.GetVirtualPathTo(@".git\hooks\read-object.exe");
            readObjectPath.ShouldBeAFile(this.fileSystem);
            this.fileSystem.DeleteFile(readObjectPath);
            readObjectPath.ShouldNotExistOnDisk(this.fileSystem);
            this.Enlistment.MountGVFS();
            readObjectPath.ShouldBeAFile(this.fileSystem);
        }

        [TestCase]
        public void MountCleansStaleIndexLock()
        {
            this.MountCleansIndexLock(lockFileContents: "GVFS");
        }

        [TestCase]
        public void MountCleansEmptyIndexLock()
        {
            this.MountCleansIndexLock(lockFileContents: string.Empty);
        }

        [TestCase]
        public void MountCleansUnknownIndexLock()
        {
            this.MountCleansIndexLock(lockFileContents: "Bogus lock file contents");
        }

        [TestCase]
        public void MountFailsWhenNoOnDiskVersion()
        {
            this.Enlistment.UnmountGVFS();

            // Get the current disk layout version
            string currentVersion = GVFSHelpers.GetPersistedDiskLayoutVersion(this.Enlistment.DotGVFSRoot).ShouldNotBeNull();
            int currentVersionNum;
            int.TryParse(currentVersion, out currentVersionNum).ShouldEqual(true);

            // Move the RepoMetadata database to a temp folder
            string versionDatabasePath = Path.Combine(this.Enlistment.DotGVFSRoot, GVFSHelpers.RepoMetadataDatabaseName);
            versionDatabasePath.ShouldBeADirectory(this.fileSystem);

            string tempDatabasePath = versionDatabasePath + "_MountFailsWhenNoOnDiskVersion";
            tempDatabasePath.ShouldNotExistOnDisk(this.fileSystem);

            this.fileSystem.MoveDirectory(versionDatabasePath, tempDatabasePath);
            versionDatabasePath.ShouldNotExistOnDisk(this.fileSystem);

            this.MountShouldFail("Enlistment disk layout version not found");

            // Move the RepoMetadata database back
            this.fileSystem.MoveDirectory(tempDatabasePath, versionDatabasePath);
            tempDatabasePath.ShouldNotExistOnDisk(this.fileSystem);
            versionDatabasePath.ShouldBeADirectory(this.fileSystem);

            this.Enlistment.MountGVFS();
        }

        [TestCaseSource(typeof(MountSubfolders), MountSubfolders.MountFolders)]
        public void MountFailsAfterBreakingDowngrade(string mountSubfolder)
        {
            MountSubfolders.EnsureSubfoldersOnDisk(this.Enlistment, this.fileSystem);
            this.Enlistment.UnmountGVFS();

            string currentVersion = GVFSHelpers.GetPersistedDiskLayoutVersion(this.Enlistment.DotGVFSRoot).ShouldNotBeNull();
            int currentVersionNum;
            int.TryParse(currentVersion, out currentVersionNum).ShouldEqual(true);
            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, (currentVersionNum + 1).ToString());

            this.MountShouldFail("do not allow mounting after downgrade", this.Enlistment.GetVirtualPathTo(mountSubfolder));

            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, currentVersionNum.ToString());
            this.Enlistment.MountGVFS();
        }

        [TestCaseSource(typeof(MountSubfolders), MountSubfolders.MountFolders)]
        public void MountFailsUpgradingFromCommitProjectionVersionWhereProjectedCommitIdDoesNotMatchHEAD(string mountSubfolder)
        {
            MountSubfolders.EnsureSubfoldersOnDisk(this.Enlistment, this.fileSystem);
            string headCommitId = GitProcess.Invoke(this.Enlistment.RepoRoot, "rev-parse HEAD");

            this.Enlistment.UnmountGVFS();

            string currentVersion = GVFSHelpers.GetPersistedDiskLayoutVersion(this.Enlistment.DotGVFSRoot).ShouldNotBeNull();
            int currentVersionNum;
            int.TryParse(currentVersion, out currentVersionNum).ShouldEqual(true);

            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, (ProjectGitIndexOnDiskVersion - 1).ToString());

            string gvfsHeadPath = Path.Combine(this.Enlistment.DotGVFSRoot, "GVFS_HEAD");
            gvfsHeadPath.ShouldNotExistOnDisk(this.fileSystem);

            // 575d597cf09b2cd1c0ddb4db21ce96979010bbcb is an earlier commit in the FunctionalTests/20170310 branch
            this.fileSystem.WriteAllText(gvfsHeadPath, "575d597cf09b2cd1c0ddb4db21ce96979010bbcb");

            this.MountShouldFail("Failed to mount", this.Enlistment.GetVirtualPathTo(mountSubfolder));

            this.fileSystem.DeleteFile(gvfsHeadPath);
            this.fileSystem.WriteAllText(gvfsHeadPath, headCommitId);
            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, currentVersionNum.ToString());
            this.Enlistment.MountGVFS();
            gvfsHeadPath.ShouldNotExistOnDisk(this.fileSystem);
        }

        [TestCaseSource(typeof(MountSubfolders), MountSubfolders.MountFolders)]
        public void MountSucceedsWhenBadDataInGvfsHeadFile(string mountSubfolder)
        {
            MountSubfolders.EnsureSubfoldersOnDisk(this.Enlistment, this.fileSystem);
            this.Enlistment.UnmountGVFS();

            string gvfsHeadPath = Path.Combine(this.Enlistment.DotGVFSRoot, "GVFS_HEAD");
            gvfsHeadPath.ShouldNotExistOnDisk(this.fileSystem);
            this.fileSystem.WriteAllText(gvfsHeadPath, "This is a bad commit Id!");

            this.Enlistment.MountGVFS();
            gvfsHeadPath.ShouldNotExistOnDisk(this.fileSystem);
        }

        [TestCaseSource(typeof(MountSubfolders), MountSubfolders.MountFolders)]
        public void MountSucceedsUpgradingFromCommitProjectionVersionWhereProjectedCommitIdMatchesHEAD(string mountSubfolder)
        {
            MountSubfolders.EnsureSubfoldersOnDisk(this.Enlistment, this.fileSystem);
            this.Enlistment.UnmountGVFS();

            string currentVersion = GVFSHelpers.GetPersistedDiskLayoutVersion(this.Enlistment.DotGVFSRoot).ShouldNotBeNull();
            int currentVersionNum;
            int.TryParse(currentVersion, out currentVersionNum).ShouldEqual(true);
            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, (ProjectGitIndexOnDiskVersion - 1).ToString());

            string alwaysExcludeVirtualPath = this.Enlistment.GetVirtualPathTo(AlwaysExcludePath);
            this.fileSystem.DeleteFile(alwaysExcludeVirtualPath);

            this.Enlistment.MountGVFS();
            this.Enlistment.UnmountGVFS();

            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, currentVersionNum.ToString());
            this.Enlistment.MountGVFS();
        }

        // Ported from GVFlt's BugRegressionTest
        [TestCase]
        public void GVFlt_CMDHangNoneActiveInstance()
        {
            this.Enlistment.UnmountGVFS();

            IntPtr handle = CreateFile(
                Path.Combine(this.Enlistment.RepoRoot, "aaa", "aaaa"),
                GenericRead,
                FileShare.Read,
                IntPtr.Zero,
                FileMode.Open,
                FileFlagBackupSemantics,
                IntPtr.Zero);

            int lastError = Marshal.GetLastWin32Error();

            IntPtr invalid_handle = new IntPtr(-1);
            handle.ShouldEqual(invalid_handle);
            lastError.ShouldNotEqual(0); // 0 == ERROR_SUCCESS
        }

        [TestCase]
        public void MountSucceedsUpgradeToAlwaysExclude()
        {
            this.Enlistment.UnmountGVFS();

            string originalVersion = GVFSHelpers.GetPersistedDiskLayoutVersion(this.Enlistment.DotGVFSRoot).ShouldNotBeNull();
            int originalVersionNum;
            int.TryParse(originalVersion, out originalVersionNum).ShouldEqual(true);
            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, (AlwaysExcludeOnDiskVersion - 1).ToString());
            string excludeVirtualPath = this.Enlistment.GetVirtualPathTo(ExcludePath);
            string alwaysExcludeVirtualPath = this.Enlistment.GetVirtualPathTo(AlwaysExcludePath);

            excludeVirtualPath.ShouldBeAFile(this.fileSystem);
            alwaysExcludeVirtualPath.ShouldBeAFile(this.fileSystem);
            string originalAlwaysExcludeContents = this.fileSystem.ReadAllText(alwaysExcludeVirtualPath);

            this.fileSystem.DeleteFile(excludeVirtualPath);
            this.fileSystem.MoveFile(alwaysExcludeVirtualPath, excludeVirtualPath);

            this.Enlistment.MountGVFS();
            this.Enlistment.UnmountGVFS();

            excludeVirtualPath.ShouldBeAFile(this.fileSystem);
            alwaysExcludeVirtualPath.ShouldBeAFile(this.fileSystem);
            originalAlwaysExcludeContents.ShouldEqual(this.fileSystem.ReadAllText(alwaysExcludeVirtualPath));

            string finalVersion = GVFSHelpers.GetPersistedDiskLayoutVersion(this.Enlistment.DotGVFSRoot).ShouldNotBeNull();
            int finalVersionNum;
            int.TryParse(finalVersion, out finalVersionNum).ShouldEqual(true);

            finalVersion.ShouldEqual(originalVersion);

            this.Enlistment.MountGVFS();
        }

        [TestCase]
        public void MountSucceedsUpgradeToAlwaysExcludeWithMissingExclude()
        {
            this.Enlistment.UnmountGVFS();

            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, (AlwaysExcludeOnDiskVersion - 1).ToString());
            string excludeVirtualPath = this.Enlistment.GetVirtualPathTo(ExcludePath);
            string alwaysExcludeVirtualPath = this.Enlistment.GetVirtualPathTo(AlwaysExcludePath);

            excludeVirtualPath.ShouldBeAFile(this.fileSystem);
            alwaysExcludeVirtualPath.ShouldBeAFile(this.fileSystem);

            this.fileSystem.DeleteFile(excludeVirtualPath);
            this.fileSystem.DeleteFile(alwaysExcludeVirtualPath);

            this.Enlistment.MountGVFS();
            this.Enlistment.UnmountGVFS();

            excludeVirtualPath.ShouldNotExistOnDisk(this.fileSystem);
            alwaysExcludeVirtualPath.ShouldBeAFile(this.fileSystem);
            this.fileSystem.ReadAllText(alwaysExcludeVirtualPath).ShouldEqual("*\n");

            this.Enlistment.MountGVFS();
        }

        [TestCase]
        public void MountFailsUpgradeToAlwaysExcludeWithPreviousAlwaysExclude()
        {
            this.Enlistment.UnmountGVFS();

            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, (AlwaysExcludeOnDiskVersion - 1).ToString());
            string alwaysExcludeVirtualPath = this.Enlistment.GetVirtualPathTo(AlwaysExcludePath);

            alwaysExcludeVirtualPath.ShouldBeAFile(this.fileSystem);

            this.MountShouldFail("Failed to mount");

            this.fileSystem.DeleteFile(alwaysExcludeVirtualPath);
            this.Enlistment.MountGVFS();
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFile(
            [In] string fileName,
            uint desiredAccess,
            FileShare shareMode,
            [In] IntPtr securityAttributes,
            [MarshalAs(UnmanagedType.U4)]FileMode creationDisposition,
            uint flagsAndAttributes,
            [In] IntPtr templateFile);

        private void MountCleansIndexLock(string lockFileContents)
        {
            this.Enlistment.UnmountGVFS();

            string indexLockVirtualPath = this.Enlistment.GetVirtualPathTo(IndexLockPath);
            indexLockVirtualPath.ShouldNotExistOnDisk(this.fileSystem);

            if (string.IsNullOrEmpty(lockFileContents))
            {
                this.fileSystem.CreateEmptyFile(indexLockVirtualPath);
            }
            else
            {
                this.fileSystem.AppendAllText(indexLockVirtualPath, lockFileContents);
            }

            this.Enlistment.MountGVFS();
            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations failed to complete.");
            indexLockVirtualPath.ShouldNotExistOnDisk(this.fileSystem);
        }

        private void MountShouldFail(int expectedExitCode, string expectedErrorMessage, string mountWorkingDirectory = null)
        {
            string pathToGVFS = Path.Combine(TestContext.CurrentContext.TestDirectory, Properties.Settings.Default.PathToGVFS);
            string enlistmentRoot = this.Enlistment.EnlistmentRoot;

            ProcessStartInfo processInfo = new ProcessStartInfo(pathToGVFS);
            processInfo.Arguments = "mount";
            processInfo.WindowStyle = ProcessWindowStyle.Hidden;
            processInfo.WorkingDirectory = string.IsNullOrEmpty(mountWorkingDirectory) ? enlistmentRoot : mountWorkingDirectory;
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardOutput = true;

            ProcessResult result = ProcessHelper.Run(processInfo);
            result.ExitCode.ShouldEqual(expectedExitCode, $"mount exit code was not {expectedExitCode}");
            result.Output.ShouldContain(expectedErrorMessage);
        }

        private void MountShouldFail(string expectedErrorMessage, string mountWorkingDirectory = null)
        {
            this.MountShouldFail(GVFSGenericError, expectedErrorMessage, mountWorkingDirectory);
        }

        private class MountSubfolders
        {            
            public const string MountFolders = "Folders";
            private static object[] mountFolders =
            {
                new object[] { string.Empty },
                new object[] { "GVFS" },
            };

            public static object[] Folders
            {
                get
                {
                    return mountFolders;
                }
            }

            public static void EnsureSubfoldersOnDisk(GVFSFunctionalTestEnlistment enlistment, FileSystemRunner fileSystem)
            {
                // Enumerate the directory to ensure that the folder is on disk after GVFS is unmounted
                foreach (object[] folder in Folders)
                {
                    string folderPath = enlistment.GetVirtualPathTo((string)folder[0]);
                    folderPath.ShouldBeADirectory(fileSystem).WithItems();
                }
            }
        }
    }
}
