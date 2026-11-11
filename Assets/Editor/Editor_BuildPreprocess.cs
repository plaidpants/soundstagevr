    #if UNITY_EDITOR
     
    using UnityEngine;
    using UnityEditor;
    using UnityEditor.Build;
    using System.IO;
    using ICSharpCode.SharpZipLib.Core;
    using ICSharpCode.SharpZipLib.Zip;
     
     
    class CustomBuildPreProcess : IPreprocessBuild
    {
        public int callbackOrder { get { return 0; } }
        public void OnPreprocessBuild(BuildTarget target, string path) {
     
            // Sources live in the project root (outside Assets) so Unity doesn't import the
            // ~100 MB of sample audio as assets; they only ship inside the .tgz files.
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string dataDirectory = Path.Combine(projectRoot, "Examples") + "/";
            string fileToCreate = Application.streamingAssetsPath + "/Examples.tgz";
     
            Utility_SharpZipCommands.CreateTarGZ_FromDirectory (fileToCreate, dataDirectory);

            dataDirectory = Path.Combine(projectRoot, "Samples") + "/";
            fileToCreate = Application.streamingAssetsPath + "/Samples.tgz";
     
            Utility_SharpZipCommands.CreateTarGZ_FromDirectory (fileToCreate, dataDirectory);
     
        }
    }
     
    #endif
