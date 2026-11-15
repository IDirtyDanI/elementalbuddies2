using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // Tester-APK (2026-11-03): zählt bei jedem Build die Version hoch (Android versionCode N, Anzeige „0.1.N“), damit
    // Android die neue APK als Update installiert (Spielstand bleibt) und Tester ihre Version im Hauptmenü sehen.
    // Development-Build mit EB_TESTVIDEO (Testvideo statt echter Werbung) und EB_TESTER (Knopf „alles freischalten“).
    // Dateiname mit Datum und Version, damit WhatsApp & Co. keine gleichnamigen Dateien verwechseln.
    // iOS (2026-11-15): Xcode-Projekt mit derselben Version wie der letzte Android-Build (kein Hochzählen);
    // danach Tools/ios/build_install.sh zum Signieren, Bauen und Installieren.
    public static class TesterBuild
    {
        public const string OutDir = "/Users/daniel/Desktop/BuddyTD/Build/Android";
        public const string IosOutDir = "/Users/daniel/Desktop/BuddyTD/Build/iOS";

        // gemeinsam für Android und iOS: gleicher Inhalt in beiden Tester-Builds
        private static readonly string[] TesterDefines = { "EB_TESTVIDEO", "EB_TESTER" };

        private static string[] EnabledScenes() =>
            EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();

        [MenuItem("BuddyTD/Mobile/Tester-APK bauen")]
        public static void BuildMenu() => Build();

        public static string Build()
        {
            int code = PlayerSettings.Android.bundleVersionCode + 1;
            PlayerSettings.Android.bundleVersionCode = code;
            PlayerSettings.bundleVersion = "0.1." + code;
            AssetDatabase.SaveAssets();

            string file = $"ElementalBuddies2-Tester-{System.DateTime.Now:ddMM}-v{code}.apk";
            var o = new BuildPlayerOptions
            {
                scenes = EnabledScenes(),
                locationPathName = Path.Combine(OutDir, file),
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.Development,
                extraScriptingDefines = TesterDefines
            };
            var r = BuildPipeline.BuildPlayer(o);
            Debug.Log($"[TesterBuild] {file}: {r.summary.result}, {r.summary.totalErrors} Fehler");
            return file;
        }

        [MenuItem("BuddyTD/Mobile/Tester-iOS bauen (Xcode-Projekt)")]
        public static void BuildIosMenu() => BuildIos();

        // Xcode-Projekt nach Build/iOS/Xcode-v{N}; N = Android-versionCode, Anzeige-Version bleibt unverändert.
        // Development-Build, damit MobileTestRemote aktiv ist. Der Abhängigkeits-Resolver führt danach pod install aus.
        public static string BuildIos()
        {
            IosPlayerSetup.Apply();
            int code = PlayerSettings.Android.bundleVersionCode;
            PlayerSettings.iOS.buildNumber = code.ToString();
            AssetDatabase.SaveAssets();

            string dir = Path.Combine(IosOutDir, $"Xcode-v{code}");
            if (Directory.Exists(dir)) Directory.Delete(dir, true); // sauberer Neuaufbau (alte Pods/DerivedData weg)
            Directory.CreateDirectory(IosOutDir);

            var o = new BuildPlayerOptions
            {
                scenes = EnabledScenes(),
                locationPathName = dir,
                target = BuildTarget.iOS,
                targetGroup = BuildTargetGroup.iOS,
                options = BuildOptions.Development,
                extraScriptingDefines = TesterDefines
            };
            var r = BuildPipeline.BuildPlayer(o);
            bool ws = Directory.Exists(Path.Combine(dir, "Unity-iPhone.xcworkspace"));
            string summary = $"Xcode-v{code} (Version {PlayerSettings.bundleVersion}, Build {code}): {r.summary.result}, " +
                             $"{r.summary.totalErrors} Fehler, Workspace {(ws ? "da" : "fehlt (pod install nötig)")} → {dir}";
            Debug.Log("[TesterBuild] iOS " + summary);
            return summary;
        }
    }
}
