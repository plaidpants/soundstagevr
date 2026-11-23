    #if UNITY_EDITOR
     
    using UnityEngine;
    using UnityEditor;
    using UnityEditor.Build;
    using UnityEditor.Build.Reporting;
    using System.IO;
    using ICSharpCode.SharpZipLib.Core;
    using ICSharpCode.SharpZipLib.Zip;
     
     
    class CustomBuildPreProcess : IPreprocessBuildWithReport
    {
        public int callbackOrder { get { return 0; } }
        public void OnPreprocessBuild(BuildReport report) {
     
            // Sources live in the project root (outside Assets) so Unity doesn't import the
            // ~100 MB of sample audio as assets; they only ship inside the .tgz files.
            // Build this with forward slashes from Application.dataPath (as the original code did).
            // Path.GetDirectoryName/Path.Combine return backslashes on Windows, which stops
            // SharpZipLib's RootPath from matching, so every entry got an extra "Examples/" or
            // "Samples/" prefix and the app unpacked to Examples/Examples/... on the headset.
            string projectRoot = Application.dataPath.Substring(0, Application.dataPath.LastIndexOf('/'));
            string dataDirectory = projectRoot + "/Examples/";
            string fileToCreate = Application.streamingAssetsPath + "/Examples.tgz";
     
            if (NeedsPacking(dataDirectory, fileToCreate)) Utility_SharpZipCommands.CreateTarGZ_FromDirectory (fileToCreate, dataDirectory);

            dataDirectory = projectRoot + "/Samples/";
            fileToCreate = Application.streamingAssetsPath + "/Samples.tgz";
     
            if (NeedsPacking(dataDirectory, fileToCreate)) Utility_SharpZipCommands.CreateTarGZ_FromDirectory (fileToCreate, dataDirectory);
     
        }

        // (2026-11-23) Every build used to pack both archives again - Samples.tgz is ~80 MB,
        // which took a while on a laptop and changed the APK even when nothing had (so a Patch
        // build re-sent it). Now only when a file or folder in the source is newer than the
        // archive (adding or deleting a file changes its folder's time too).
        static bool NeedsPacking(string sourceDir, string archive) {
            if (!File.Exists(archive) || !Directory.Exists(sourceDir)) return true;
            System.DateTime packed = File.GetLastWriteTimeUtc(archive);
            bool newer = Directory.GetLastWriteTimeUtc(sourceDir) > packed;
            if (!newer) foreach (string d in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
                if (Directory.GetLastWriteTimeUtc(d) > packed) { newer = true; break; }
            if (!newer) foreach (string f in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
                if (File.GetLastWriteTimeUtc(f) > packed) { newer = true; break; }
            Debug.Log("[BuildPreprocess] " + Path.GetFileName(archive) + (newer ? ": sources changed, packing it again" : ": up to date, not packed again"));
            return newer;
        }
    }
     
    #endif
