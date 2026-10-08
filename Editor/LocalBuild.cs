/*============================================================
Created By: Michal’s MacBook Pro
Date: 17/09/2026 13:07
============================================================*/
#if UNITY_ANDROID
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace LocalBuilder
{
    // Android only: compiled when the active build target is Android (UNITY_ANDROID).
    // Builds Android player to a local folder (Gradle runs on the local disk),
    // then copies the signed artifacts (.aab/.apk, .obb, symbols, R8 mapping) to the destination folder.
    public static class LocalBuild
    {
        public const string MenuRoot = "Builder/";
        const string LogPrefix = "[LocalBuilder] ";
        const int CopyBufferSize = 4 << 20;

        [MenuItem(MenuRoot + "Build", false, 1)]
        static void MenuBuild() => Run(false);

        [MenuItem(MenuRoot + "Clean Build", false, 2)]
        static void MenuCleanBuild() => Run(true);

        [MenuItem(MenuRoot + "Copy Last Build to Destination", false, 20)]
        static void MenuCopyLast() => EditorApplication.delayCall += () => CopyLastBuild(true);

        [MenuItem(MenuRoot + "Copy Last Build to Destination", true)]
        static bool MenuCopyLastValidate() => LocalBuilderSettings.instance.lastArtifacts.Any(File.Exists);

        const string MenuDeleteLocal = MenuRoot + "Delete Local Build After Copy";

        [MenuItem(MenuDeleteLocal, false, 21)]
        static void MenuToggleDeleteLocal()
        {
            var settings = LocalBuilderSettings.instance;
            settings.deleteLocalAfterCopy = !settings.deleteLocalAfterCopy;
            settings.SaveSettings();
        }

        [MenuItem(MenuDeleteLocal, true)]
        static bool MenuToggleDeleteLocalValidate()
        {
            Menu.SetChecked(MenuDeleteLocal, LocalBuilderSettings.instance.deleteLocalAfterCopy);
            return true;
        }

        [MenuItem(MenuRoot + "Settings...", false, 40)]
        static void MenuSettings() => LocalBuilderWindow.Open();

        // Deferred so it's safe to call from OnGUI / menu callbacks.
        public static void Run(bool clean)
        {
            EditorApplication.delayCall += () => Build(clean, !Application.isBatchMode);
        }

        // Command line overrides, not saved to settings.
        static string s_DestinationRootOverride;
        static bool s_DevelopmentOverride;

        public static string DestinationRoot => s_DestinationRootOverride ?? LocalBuilderSettings.instance.destinationRoot;

        public static bool DevelopmentBuild => s_DevelopmentOverride || LocalBuilderSettings.instance.developmentBuild;

        public static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        public static string LocalOutputDir
        {
            get
            {
                string folder = LocalBuilderSettings.instance.localOutputFolder;
                if (string.IsNullOrWhiteSpace(folder))
                    folder = "Builds/Android";
                return Path.GetFullPath(Path.Combine(ProjectRoot, folder));
            }
        }

        public static string BaseFileName => ExpandTokens(LocalBuilderSettings.instance.fileNamePattern, false);

        public static string OutputExtension => LocalBuilderSettings.instance.buildAppBundle ? ".aab" : ".apk";

        public static string DestinationDir
        {
            get
            {
                string root = DestinationRoot;
                if (string.IsNullOrWhiteSpace(root))
                    return "";
                string sub = ExpandTokens(LocalBuilderSettings.instance.subfolderPattern, true);
                return string.IsNullOrEmpty(sub) ? root : Path.Combine(root, sub);
            }
        }

        public static bool IsDestinationAvailable
        {
            get
            {
                string root = DestinationRoot;
                return !string.IsNullOrWhiteSpace(root) && Directory.Exists(root);
            }
        }

        // Tokens: {product} {company} {version} {version_} {code} {date} {time} {dev}
        public static string ExpandTokens(string pattern, bool allowSubfolders)
        {
            string version = PlayerSettings.bundleVersion;
            DateTime now = DateTime.Now;
            string result = (pattern ?? "")
                .Replace("{product}", PlayerSettings.productName)
                .Replace("{company}", PlayerSettings.companyName)
                .Replace("{version_}", version.Replace('.', '_'))
                .Replace("{version}", version)
                .Replace("{code}", PlayerSettings.Android.bundleVersionCode.ToString())
                .Replace("{date}", now.ToString("yyyyMMdd"))
                .Replace("{time}", now.ToString("HHmm"))
                .Replace("{dev}", DevelopmentBuild ? "dev" : "");

            if (!allowSubfolders)
                return SanitizeSegment(result);

            var segments = result.Split('/', '\\')
                .Select(SanitizeSegment)
                .Where(x => x.Length > 0 && x != "." && x != "..");
            return string.Join(Path.DirectorySeparatorChar.ToString(), segments);
        }

        static string SanitizeSegment(string s)
        {
            const string invalid = "/\\:*?\"<>|";
            var chars = s.Select(c => invalid.IndexOf(c) >= 0 || char.IsControl(c) ? '_' : c).ToArray();
            return new string(chars).Trim();
        }

        #region Signing

        static string PrefsKey(string name) => $"LocalBuilder.{PlayerSettings.productGUID}.{name}";

        public static void StorePasswords(bool remember)
        {
            if (remember)
            {
                EditorPrefs.SetString(PrefsKey("keystorePass"), PlayerSettings.Android.keystorePass);
                EditorPrefs.SetString(PrefsKey("keyaliasPass"), PlayerSettings.Android.keyaliasPass);
            }
            else
            {
                EditorPrefs.DeleteKey(PrefsKey("keystorePass"));
                EditorPrefs.DeleteKey(PrefsKey("keyaliasPass"));
            }
        }

        // Unity doesn't persist keystore passwords between editor sessions.
        public static void RestoreRememberedPasswords()
        {
            if (!LocalBuilderSettings.instance.rememberPasswords)
                return;
            if (string.IsNullOrEmpty(PlayerSettings.Android.keystorePass))
                PlayerSettings.Android.keystorePass = EditorPrefs.GetString(PrefsKey("keystorePass"), "");
            if (string.IsNullOrEmpty(PlayerSettings.Android.keyaliasPass))
                PlayerSettings.Android.keyaliasPass = EditorPrefs.GetString(PrefsKey("keyaliasPass"), "");
        }

        static string ValidateSigning()
        {
            if (!PlayerSettings.Android.useCustomKeystore)
                return null;
            if (!File.Exists(PlayerSettings.Android.keystoreName))
                return "Keystore file not found: " + PlayerSettings.Android.keystoreName;
            if (string.IsNullOrEmpty(PlayerSettings.Android.keyaliasName))
                return "Key alias is not set in Player Settings.";
            if (string.IsNullOrEmpty(PlayerSettings.Android.keystorePass) || string.IsNullOrEmpty(PlayerSettings.Android.keyaliasPass))
                return "Keystore / key alias password is empty. Enter it in Builder/Settings (or Player Settings > Publishing Settings).";
            return null;
        }

        #endregion

        public static bool Build(bool clean, bool interactive)
        {
            var settings = LocalBuilderSettings.instance;
            settings.EnsureInitialized();
            RestoreRememberedPasswords();

            string[] scenes = EditorBuildSettings.scenes.Where(x => x.enabled).Select(x => x.path).ToArray();
            if (scenes.Length == 0)
                return Fail("No enabled scenes in Build Settings.", interactive);

            string signingError = ValidateSigning();
            if (signingError != null)
            {
                if (interactive)
                    LocalBuilderWindow.Open();
                return Fail(signingError, interactive);
            }

            bool copyToDestination = true;
            if (!IsDestinationAvailable)
            {
                string msg = string.IsNullOrWhiteSpace(DestinationRoot)
                    ? "Destination folder is not set."
                    : "Destination folder is not available (network share not mounted?):\n" + DestinationRoot;
                if (!interactive)
                    return Fail(msg, false);
                if (!EditorUtility.DisplayDialog("Local Builder", msg + "\n\nBuild locally only? You can copy it later with Builder > Copy Last Build.", "Build locally", "Cancel"))
                    return false;
                copyToDestination = false;
            }

            string localDir = LocalOutputDir;
            Directory.CreateDirectory(localDir);
            string baseName = BaseFileName;
            if (string.IsNullOrEmpty(baseName))
                return Fail("File name pattern expands to an empty name.", interactive);
            string outputPath = Path.Combine(localDir, baseName + OutputExtension);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            };
            if (clean)
                options.options |= BuildOptions.CleanBuildCache;
            if (DevelopmentBuild)
                options.options |= BuildOptions.Development;

            Debug.Log($"{LogPrefix}{(clean ? "Clean build" : "Build")} started: {outputPath}");

            bool prevAppBundle = EditorUserBuildSettings.buildAppBundle;
            DateTime startUtc = DateTime.UtcNow;
            BuildReport report;
            try
            {
                EditorUserBuildSettings.buildAppBundle = settings.buildAppBundle;
                report = BuildPipeline.BuildPlayer(options);
            }
            finally
            {
                EditorUserBuildSettings.buildAppBundle = prevAppBundle;
            }

            BuildSummary summary = report.summary;
            string buildTime = FormatTime(summary.totalTime);
            if (summary.result != BuildResult.Succeeded)
            {
                settings.lastResult = $"{DateTime.Now:yyyy-MM-dd HH:mm} {summary.result} ({summary.totalErrors} errors, {buildTime})";
                settings.SaveSettings();
                return Fail($"Build {summary.result} after {buildTime}, errors: {summary.totalErrors}. See Console for details.", interactive && summary.result != BuildResult.Cancelled);
            }

            List<string> artifacts = CollectArtifacts(localDir, outputPath, baseName, startUtc, settings.copyMapping);
            settings.lastArtifacts = artifacts;
            settings.lastBaseName = baseName;
            settings.lastResult = $"{DateTime.Now:yyyy-MM-dd HH:mm} Succeeded ({buildTime}, {FormatSize(summary.totalSize)})";
            settings.SaveSettings();

            Debug.Log($"{LogPrefix}Build succeeded in {buildTime}. Artifacts:\n" + string.Join("\n", artifacts));

            if (!copyToDestination)
            {
                if (interactive)
                    EditorUtility.RevealInFinder(outputPath);
                return true;
            }
            return CopyArtifacts(artifacts, baseName, interactive);
        }

        public static bool CopyLastBuild(bool interactive)
        {
            var artifacts = LocalBuilderSettings.instance.lastArtifacts.Where(File.Exists).ToList();
            if (artifacts.Count == 0)
                return Fail("No local build artifacts found. Build first.", interactive);
            if (!IsDestinationAvailable)
                return Fail("Destination folder is not available:\n" + DestinationRoot, interactive);
            return CopyArtifacts(artifacts, LocalBuilderSettings.instance.lastBaseName, interactive);
        }

        static List<string> CollectArtifacts(string localDir, string outputPath, string baseName, DateTime startUtc, bool copyMapping)
        {
            // Everything Unity produced next to the output during this build (.aab/.apk, .obb, *.symbols.zip).
            DateTime threshold = startUtc.AddSeconds(-5);
            var result = new List<string>();
            if (File.Exists(outputPath))
                result.Add(outputPath);
            foreach (string file in Directory.GetFiles(localDir))
            {
                string name = Path.GetFileName(file);
                if (name.StartsWith(".") || name.EndsWith(".partial") || result.Contains(file))
                    continue;
                if (File.GetLastWriteTimeUtc(file) >= threshold)
                    result.Add(file);
            }

            if (copyMapping)
            {
                string mapping = FindR8Mapping(threshold);
                if (mapping != null)
                {
                    string localMapping = Path.Combine(localDir, baseName + "_mapping.txt");
                    File.Copy(mapping, localMapping, true);
                    if (!result.Contains(localMapping))
                        result.Add(localMapping);
                }
            }
            return result;
        }

        static string FindR8Mapping(DateTime thresholdUtc)
        {
            string prj = Path.Combine(ProjectRoot, "Library/Bee/Android/Prj");
            if (!Directory.Exists(prj))
                return null;
            return Directory.GetDirectories(prj)
                .Select(d => Path.Combine(d, "Gradle/launcher/build/outputs/mapping/release/mapping.txt"))
                .Where(File.Exists)
                .Where(f => File.GetLastWriteTimeUtc(f) >= thresholdUtc)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }

        static bool CopyArtifacts(List<string> artifacts, string baseName, bool interactive)
        {
            var settings = LocalBuilderSettings.instance;
            string destDir = DestinationDir;
            var sw = Stopwatch.StartNew();
            try
            {
                Directory.CreateDirectory(destDir);

                var existing = artifacts.Select(a => Path.Combine(destDir, Path.GetFileName(a))).Where(File.Exists).ToList();
                if (existing.Count > 0 && !settings.overwriteExisting)
                {
                    string msg = "Files already exist in destination:\n" + string.Join("\n", existing.Select(Path.GetFileName));
                    if (!interactive)
                        return Fail(msg, false);
                    if (!EditorUtility.DisplayDialog("Local Builder", msg + "\n\nOverwrite?", "Overwrite", "Cancel"))
                        return false;
                }

                long totalBytes = artifacts.Sum(a => new FileInfo(a).Length);
                long copiedBytes = 0;
                foreach (string src in artifacts)
                {
                    string dst = Path.Combine(destDir, Path.GetFileName(src));
                    CopyFileWithProgress(src, dst, interactive, totalBytes, ref copiedBytes, sw);
                }

                double seconds = Math.Max(sw.Elapsed.TotalSeconds, 0.001);
                Debug.Log($"{LogPrefix}Copied {artifacts.Count} file(s), {FormatSize((ulong)totalBytes)} in {seconds:0.0}s " +
                          $"({totalBytes / seconds / (1 << 20):0.0} MB/s) to {destDir}");
                if (settings.deleteLocalAfterCopy)
                    DeleteLocalArtifacts(artifacts, destDir, baseName);
                if (interactive && settings.revealAfterCopy)
                    EditorUtility.RevealInFinder(Path.Combine(destDir, Path.GetFileName(artifacts[0])));
                return true;
            }
            catch (OperationCanceledException)
            {
                Debug.LogWarning(LogPrefix + "Copy cancelled. Use Builder > Copy Last Build to retry.");
                return false;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return Fail("Copy failed: " + e.Message + "\nLocal build is kept, use Builder > Copy Last Build to retry.", interactive);
            }
            finally
            {
                if (interactive)
                    EditorUtility.ClearProgressBar();
            }
        }

        // Removes only build files (name starts with the build's base name) whose copy in the destination has the same size.
        // Other files written to the local folder during the build are copied but kept.
        static void DeleteLocalArtifacts(List<string> artifacts, string destDir, string baseName)
        {
            long freedBytes = 0;
            int deleted = 0;
            foreach (string src in artifacts)
            {
                string dst = Path.Combine(destDir, Path.GetFileName(src));
                try
                {
                    if (string.IsNullOrEmpty(baseName) || !Path.GetFileName(src).StartsWith(baseName, StringComparison.Ordinal))
                    {
                        Debug.LogWarning($"{LogPrefix}Local file kept, not named after the build: {src}");
                        continue;
                    }
                    // Destination == local folder: the "copy" is the local file itself.
                    if (string.Equals(Path.GetFullPath(src), Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.LogWarning($"{LogPrefix}Local file kept, destination is the same file: {src}");
                        continue;
                    }
                    long size = new FileInfo(src).Length;
                    if (!File.Exists(dst) || new FileInfo(dst).Length != size)
                    {
                        Debug.LogWarning($"{LogPrefix}Local file kept, destination copy missing or size differs: {src}");
                        continue;
                    }
                    File.Delete(src);
                    freedBytes += size;
                    deleted++;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"{LogPrefix}Could not delete local file {src}: {e.Message}");
                }
            }
            Debug.Log($"{LogPrefix}Deleted {deleted} local file(s), freed {FormatSize((ulong)freedBytes)}.");
        }

        // Copies to "<dst>.partial" first so a half-written file never appears under the final name.
        static void CopyFileWithProgress(string src, string dst, bool interactive, long totalBytes, ref long copiedBytes, Stopwatch sw)
        {
            string tmp = dst + ".partial";
            string name = Path.GetFileName(src);
            var buffer = new byte[CopyBufferSize];
            double lastUpdate = -1;
            try
            {
                using (var input = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan))
                using (var output = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1))
                {
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        output.Write(buffer, 0, read);
                        copiedBytes += read;

                        double t = sw.Elapsed.TotalSeconds;
                        if (interactive && t - lastUpdate > 0.1)
                        {
                            lastUpdate = t;
                            double speed = copiedBytes / Math.Max(t, 0.001) / (1 << 20);
                            string info = $"{name}  {FormatSize((ulong)copiedBytes)} / {FormatSize((ulong)totalBytes)}  ({speed:0.0} MB/s)";
                            if (EditorUtility.DisplayCancelableProgressBar("Copying build to destination", info, totalBytes > 0 ? (float)copiedBytes / totalBytes : 1f))
                                throw new OperationCanceledException();
                        }
                    }
                }
                if (File.Exists(dst))
                    File.Delete(dst);
                File.Move(tmp, dst);
            }
            catch
            {
                try
                {
                    if (File.Exists(tmp))
                        File.Delete(tmp);
                }
                catch
                {
                    // ignore, original exception matters
                }
                throw;
            }
        }

        static bool Fail(string message, bool showDialog)
        {
            Debug.LogError(LogPrefix + message);
            if (showDialog)
                EditorUtility.DisplayDialog("Local Builder", message, "OK");
            return false;
        }

        static string FormatTime(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

        public static string FormatSize(ulong bytes) => bytes >= 1 << 30 ? $"{bytes / (double)(1 << 30):0.00} GB" : $"{bytes / (double)(1 << 20):0.0} MB";

        #region Command line

        // Unity -batchmode -quit -projectPath <path> -buildTarget Android -executeMethod LocalBuilder.LocalBuild.BuildFromCommandLine
        //   [-lbClean] [-lbDest <folder>] [-lbDevelopment]
        // Passwords: env LB_KEYSTORE_PASS / LB_KEYALIAS_PASS (or remembered ones from EditorPrefs).
        public static void BuildFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            LocalBuilderSettings.instance.EnsureInitialized();

            int destIndex = Array.IndexOf(args, "-lbDest");
            if (destIndex >= 0 && destIndex + 1 < args.Length)
                s_DestinationRootOverride = args[destIndex + 1];
            s_DevelopmentOverride = args.Contains("-lbDevelopment");

            string keystorePass = Environment.GetEnvironmentVariable("LB_KEYSTORE_PASS");
            string keyaliasPass = Environment.GetEnvironmentVariable("LB_KEYALIAS_PASS");
            if (!string.IsNullOrEmpty(keystorePass))
                PlayerSettings.Android.keystorePass = keystorePass;
            if (!string.IsNullOrEmpty(keyaliasPass))
                PlayerSettings.Android.keyaliasPass = keyaliasPass;

            bool ok = Build(args.Contains("-lbClean"), false);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        #endregion
    }
}
#endif
