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
     
            Utility_SharpZipCommands.CreateTarGZ_FromDirectory (fileToCreate, dataDirectory);

            dataDirectory = projectRoot + "/Samples/";
            fileToCreate = Application.streamingAssetsPath + "/Samples.tgz";
     
            Utility_SharpZipCommands.CreateTarGZ_FromDirectory (fileToCreate, dataDirectory);
     
        }
    }
     
    #endif
