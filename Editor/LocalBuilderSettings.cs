/*============================================================
Created By: Michal’s MacBook Pro
Date: 17/09/2026 13:07
============================================================*/
#if UNITY_ANDROID
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;

namespace LocalBuilder
{
    // Per-user, per-project settings (UserSettings/ is machine specific, not meant for version control).
    [FilePath("UserSettings/LocalBuilderSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public class LocalBuilderSettings : ScriptableSingleton<LocalBuilderSettings>
    {
        public bool initialized;

        // Final destination (e.g. mounted network share) and token patterns, see LocalBuild.ExpandTokens.
        public string destinationRoot = "";
        public string subfolderPattern = "{version}";
        public string fileNamePattern = "{product}_{version_}_{code}";

        // Relative to the project root (or absolute). Must be on a local disk.
        public string localOutputFolder = "Builds/Android";

        public bool buildAppBundle = true;
        public bool developmentBuild;
        public bool copyMapping = true;
        public bool overwriteExisting;
        public bool revealAfterCopy = true;
        public bool deleteLocalAfterCopy = true;
        public bool rememberPasswords;

        // Last build info (used by "Copy Last Build").
        public string lastResult = "";
        public List<string> lastArtifacts = new List<string>();

        public void SaveSettings() => Save(true);

        // First run: derive destination and file name pattern from the last Android build location.
        public void EnsureInitialized()
        {
            if (initialized)
                return;
            initialized = true;

            string last = EditorUserBuildSettings.GetBuildLocation(BuildTarget.Android);
            if (!string.IsNullOrEmpty(last))
            {
                string version = PlayerSettings.bundleVersion;
                string dir = Path.GetDirectoryName(last);
                if (!string.IsNullOrEmpty(dir))
                {
                    if (Path.GetFileName(dir) == version)
                    {
                        destinationRoot = Path.GetDirectoryName(dir);
                        subfolderPattern = "{version}";
                    }
                    else
                    {
                        destinationRoot = dir;
                        subfolderPattern = "";
                    }
                }

                string name = Path.GetFileNameWithoutExtension(last);
                if (!string.IsNullOrEmpty(name))
                {
                    name = name.Replace(version.Replace('.', '_'), "{version_}").Replace(version, "{version}");
                    // Trailing number is the version code of that build (may differ from the current one).
                    fileNamePattern = Regex.Replace(name, @"\d+$", "{code}");
                }
            }
            SaveSettings();
        }
    }
}
#endif
