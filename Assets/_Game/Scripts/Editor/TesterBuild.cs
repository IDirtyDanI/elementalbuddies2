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
    public static class TesterBuild
    {
        public const string OutDir = "/Users/daniel/Desktop/BuddyTD/Build/Android";

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
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = Path.Combine(OutDir, file),
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.Development,
                extraScriptingDefines = new[] { "EB_TESTVIDEO", "EB_TESTER" }
            };
            var r = BuildPipeline.BuildPlayer(o);
            Debug.Log($"[TesterBuild] {file}: {r.summary.result}, {r.summary.totalErrors} Fehler");
            return file;
        }
    }
}
