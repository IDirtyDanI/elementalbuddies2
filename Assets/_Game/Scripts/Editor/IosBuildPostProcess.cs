#if UNITY_EDITOR_OSX
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // iOS-Build nachbearbeiten (iOS-Plan 2026-11-15): Info.plist-Texte und Xcode-Einstellungen, die Unity nicht selbst
    // setzt. Idempotent (Werte werden gesetzt, nicht angehängt), läuft nach dem LevelPlay-Post-Process. Guard
    // UNITY_EDITOR_OSX statt UNITY_IOS: UNITY_IOS hängt im Editor vom aktiven Build-Ziel ab.
    public class IosBuildPostProcess : IPostprocessBuildWithReport
    {
        // Personal Team (kostenlos), falls in den Player Settings keins eingetragen ist
        private const string FallbackTeamId = "8UZ2YT2TA7";

        public int callbackOrder => 100;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS) return;
            string path = report.summary.outputPath;
            PatchPlist(path);
            PatchProject(path);
            Debug.Log("[IosBuildPostProcess] Info.plist und Xcode-Projekt angepasst: " + path);
        }

        private static void PatchPlist(string buildPath)
        {
            string plistPath = Path.Combine(buildPath, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            var root = plist.root;
            root.SetString("NSLocalNetworkUsageDescription",
                "Für das Koop-Spiel im lokalen Netzwerk: Elemental Buddies verbindet sich per IP-Adresse mit Mitspielern im selben WLAN.");
            root.SetString("NSUserTrackingUsageDescription",
                "Nur für die freiwilligen Belohnungsvideos: Mit deiner Erlaubnis können die Werbepartner passendere Werbung zeigen. Das Spiel funktioniert auch ohne.");
            root.SetBoolean("ITSAppUsesNonExemptEncryption", false);
            root.SetBoolean("UIStatusBarHidden", true);
            root.SetBoolean("UIViewControllerBasedStatusBarAppearance", false);
            plist.WriteToFile(plistPath);
        }

        private static void PatchProject(string buildPath)
        {
            string projPath = PBXProject.GetPBXProjectPath(buildPath);
            var proj = new PBXProject();
            proj.ReadFromFile(projPath);

            string main = proj.GetUnityMainTargetGuid();
            string framework = proj.GetUnityFrameworkTargetGuid();

            // Bitcode ist seit Xcode 14 abgeschafft
            foreach (string guid in new[] { proj.ProjectGuid(), main, framework })
                proj.SetBuildProperty(guid, "ENABLE_BITCODE", "NO");

            // ATT (EBTracking.mm) – schwach gelinkt
            if (!proj.ContainsFramework(framework, "AppTrackingTransparency.framework"))
                proj.AddFrameworkToProject(framework, "AppTrackingTransparency.framework", true);

            // Automatisch signieren mit dem Team aus den Player Settings (sonst Personal Team)
            string team = PlayerSettings.iOS.appleDeveloperTeamID;
            if (string.IsNullOrEmpty(team)) team = FallbackTeamId;
            proj.SetBuildProperty(main, "DEVELOPMENT_TEAM", team);
            proj.SetBuildProperty(main, "CODE_SIGN_STYLE", "Automatic");

            // Personal Team (kostenlose Apple-ID): LevelPlay hängt StoreKit an die App, Xcode leitet daraus die Fähigkeit
            // „In-App-Käufe“ ab – die ein Personal Team nicht bekommt (Signierfehler, 2026-11-15). Das LevelPlay-Pod
            // bindet StoreKit selbst ein; für die App ist der Eintrag entbehrlich. Mit bezahltem Team bleibt er stehen.
            if (team == IosPlayerSetup.TeamId && proj.ContainsFramework(main, "StoreKit.framework"))
                proj.RemoveFrameworkFromProject(main, "StoreKit.framework");

            proj.WriteToFile(projPath);
        }
    }
}
#endif
