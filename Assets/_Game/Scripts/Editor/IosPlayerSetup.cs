using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // iOS-Player-Einstellungen (2026-11-15), idempotent; läuft auch zu Beginn von TesterBuild.BuildIos.
    // IL2CPP, Stripping Low (wie Android), Gerät-SDK ARM64, iOS ≥ 15, automatisches Signieren mit dem Personal Team.
    // Ausrichtung/Statusleiste sind plattformübergreifend und werden nur auf die Android-Werte festgeschrieben.
    // Bundle-ID, Produktname und Qualitätsstufen bleiben unberührt.
    public static class IosPlayerSetup
    {
        public const string TeamId = "8UZ2YT2TA7";
        public const string MinIos = "15.0";
        private const int ArchArm64 = 1; // iOS: 0 = None, 1 = ARM64, 2 = Universal

        [MenuItem("BuddyTD/Mobile/iOS-Einstellungen setzen")]
        public static void ApplyMenu() => Apply();

        public static void Apply()
        {
            var t = NamedBuildTarget.iOS;
            PlayerSettings.SetScriptingBackend(t, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(t, ManagedStrippingLevel.Low);
            PlayerSettings.SetArchitecture(t, ArchArm64);
            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
            PlayerSettings.iOS.targetOSVersionString = MinIos;
            PlayerSettings.iOS.appleEnableAutomaticSigning = true;
            PlayerSettings.iOS.appleDeveloperTeamID = TeamId;
            PlayerSettings.iOS.requiresFullScreen = true;

            // Querformat links/rechts wie Android (automatisch drehen, nur Landscape)
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.statusBarHidden = true;

            AssetDatabase.SaveAssets();
            Debug.Log($"[IosPlayerSetup] iOS: IL2CPP, Stripping Low, ARM64, iOS ≥ {MinIos}, Team {TeamId}, " +
                      $"Bundle {PlayerSettings.GetApplicationIdentifier(t)}");
        }
    }
}
