/*============================================================
Created By: Michal’s MacBook Pro
Date: 17/09/2026 13:10
============================================================*/
#if UNITY_ANDROID
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LocalBuilder
{
    public class LocalBuilderWindow : EditorWindow
    {
        Vector2 m_Scroll;

        public static void Open()
        {
            var window = GetWindow<LocalBuilderWindow>("Local Builder");
            window.minSize = new Vector2(420, 480);
            window.Show();
        }

        void OnEnable()
        {
            LocalBuilderSettings.instance.EnsureInitialized();
            LocalBuild.RestoreRememberedPasswords();
        }

        void OnFocus() => Repaint();

        void OnGUI()
        {
            var s = LocalBuilderSettings.instance;
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            EditorGUI.BeginChangeCheck();

            DrawDestination(s);
            EditorGUILayout.Space();
            DrawLocal(s);
            EditorGUILayout.Space();
            DrawOptions(s);
            EditorGUILayout.Space();
            DrawSigning(s);

            if (EditorGUI.EndChangeCheck())
                s.SaveSettings();

            EditorGUILayout.Space();
            DrawBuildButtons(s);
            EditorGUILayout.EndScrollView();
        }

        static void DrawDestination(LocalBuilderSettings s)
        {
            EditorGUILayout.LabelField("Destination", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                s.destinationRoot = EditorGUILayout.TextField("Folder", s.destinationRoot);
                if (GUILayout.Button("...", GUILayout.Width(28)))
                {
                    string picked = EditorUtility.OpenFolderPanel("Destination folder", s.destinationRoot, "");
                    if (!string.IsNullOrEmpty(picked))
                    {
                        s.destinationRoot = picked;
                        GUI.changed = true;
                    }
                }
                using (new EditorGUI.DisabledScope(!LocalBuild.IsDestinationAvailable))
                {
                    if (GUILayout.Button("Open", GUILayout.Width(48)))
                        EditorUtility.RevealInFinder(Directory.Exists(LocalBuild.DestinationDir) ? LocalBuild.DestinationDir : s.destinationRoot);
                }
            }
            s.subfolderPattern = EditorGUILayout.TextField(new GUIContent("Subfolder", "Optional, may contain tokens and '/'"), s.subfolderPattern);
            s.fileNamePattern = EditorGUILayout.TextField(new GUIContent("File name", "Without extension"), s.fileNamePattern);
            EditorGUILayout.LabelField(" ", "Tokens: {product} {company} {version} {version_} {code} {date} {time} {dev}", EditorStyles.miniLabel);

            string target = string.IsNullOrWhiteSpace(s.destinationRoot)
                ? "(not set)"
                : Path.Combine(LocalBuild.DestinationDir, LocalBuild.BaseFileName + LocalBuild.OutputExtension);
            EditorGUILayout.SelectableLabel("→ " + target, EditorStyles.wordWrappedMiniLabel, GUILayout.MinHeight(28));

            if (string.IsNullOrWhiteSpace(s.destinationRoot))
                EditorGUILayout.HelpBox("Destination folder is not set.", MessageType.Warning);
            else if (!LocalBuild.IsDestinationAvailable)
                EditorGUILayout.HelpBox("Destination folder is not available (network share not mounted?).", MessageType.Warning);
        }

        static void DrawLocal(LocalBuilderSettings s)
        {
            EditorGUILayout.LabelField("Local build", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                s.localOutputFolder = EditorGUILayout.TextField(new GUIContent("Output folder", "Relative to project root or absolute; should be on a local disk"), s.localOutputFolder);
                if (GUILayout.Button("...", GUILayout.Width(28)))
                {
                    string picked = EditorUtility.OpenFolderPanel("Local output folder", LocalBuild.LocalOutputDir, "");
                    if (!string.IsNullOrEmpty(picked))
                    {
                        string root = LocalBuild.ProjectRoot + Path.DirectorySeparatorChar;
                        s.localOutputFolder = picked.StartsWith(root) ? picked.Substring(root.Length) : picked;
                        GUI.changed = true;
                    }
                }
                if (GUILayout.Button("Open", GUILayout.Width(48)))
                {
                    Directory.CreateDirectory(LocalBuild.LocalOutputDir);
                    EditorUtility.RevealInFinder(LocalBuild.LocalOutputDir + Path.DirectorySeparatorChar);
                }
            }
            if (LocalBuild.LocalOutputDir.StartsWith("/Volumes/"))
                EditorGUILayout.HelpBox("Output folder is on an external/network volume. The point of this tool is to build on a local disk.", MessageType.Warning);
        }

        static void DrawOptions(LocalBuilderSettings s)
        {
            EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);
            s.buildAppBundle = EditorGUILayout.Toggle(new GUIContent("App Bundle (.aab)", "Off = .apk"), s.buildAppBundle);
            s.developmentBuild = EditorGUILayout.Toggle("Development build", s.developmentBuild);
            s.copyMapping = EditorGUILayout.Toggle(new GUIContent("Copy R8 mapping.txt", "Copied as <name>_mapping.txt when minification produced it"), s.copyMapping);
            s.overwriteExisting = EditorGUILayout.Toggle(new GUIContent("Overwrite without asking"), s.overwriteExisting);
            s.revealAfterCopy = EditorGUILayout.Toggle("Reveal after copy", s.revealAfterCopy);
            s.deleteLocalAfterCopy = EditorGUILayout.Toggle(new GUIContent("Delete local after copy", "Removes the local build files once they are copied to the destination"), s.deleteLocalAfterCopy);
        }

        static void DrawSigning(LocalBuilderSettings s)
        {
            EditorGUILayout.LabelField("Signing", EditorStyles.boldLabel);
            if (!PlayerSettings.Android.useCustomKeystore)
            {
                EditorGUILayout.HelpBox("Custom keystore is disabled in Player Settings, the build will be signed with the debug key.", MessageType.Warning);
                return;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Keystore", PlayerSettings.Android.keystoreName);
                EditorGUILayout.TextField("Alias", PlayerSettings.Android.keyaliasName);
            }

            EditorGUI.BeginChangeCheck();
            string keystorePass = EditorGUILayout.PasswordField("Keystore password", PlayerSettings.Android.keystorePass);
            string keyaliasPass = EditorGUILayout.PasswordField("Alias password", PlayerSettings.Android.keyaliasPass);
            bool remember = EditorGUILayout.Toggle(new GUIContent("Remember passwords", "Stored in EditorPrefs on this machine (plain text). Otherwise Unity forgets them on restart."), s.rememberPasswords);
            if (EditorGUI.EndChangeCheck())
            {
                PlayerSettings.Android.keystorePass = keystorePass;
                PlayerSettings.Android.keyaliasPass = keyaliasPass;
                s.rememberPasswords = remember;
                LocalBuild.StorePasswords(remember);
            }

            if (!File.Exists(PlayerSettings.Android.keystoreName))
                EditorGUILayout.HelpBox("Keystore file not found.", MessageType.Error);
            else if (string.IsNullOrEmpty(PlayerSettings.Android.keystorePass) || string.IsNullOrEmpty(PlayerSettings.Android.keyaliasPass))
                EditorGUILayout.HelpBox("Enter passwords to build a signed package.", MessageType.Warning);
        }

        static void DrawBuildButtons(LocalBuilderSettings s)
        {
            EditorGUILayout.LabelField("Build", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Version", $"{PlayerSettings.bundleVersion} ({PlayerSettings.Android.bundleVersionCode})");
            EditorGUILayout.LabelField("Scenes", EditorBuildSettings.scenes.Count(x => x.enabled).ToString());
            if (!string.IsNullOrEmpty(s.lastResult))
                EditorGUILayout.LabelField("Last build", s.lastResult);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Build", GUILayout.Height(32)))
                {
                    LocalBuild.Run(false);
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button("Clean Build", GUILayout.Height(32)))
                {
                    LocalBuild.Run(true);
                    GUIUtility.ExitGUI();
                }
            }

            using (new EditorGUI.DisabledScope(!s.lastArtifacts.Any(File.Exists)))
            {
                if (GUILayout.Button("Copy Last Build to Destination"))
                {
                    EditorApplication.delayCall += () => LocalBuild.CopyLastBuild(true);
                    GUIUtility.ExitGUI();
                }
            }
        }
    }
}
#endif
