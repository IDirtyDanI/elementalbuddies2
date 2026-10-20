using UnityEngine;

namespace ElementalBuddies
{
    // Zentraler Schalter für die Mobil-Version (Branch feature/mobile, 2026-10-20). Alles, was nur auf Touch-Geräten gilt
    // (virtuelle Sticks, Fähigkeits-Knöpfe, Tippen zum Bauen, größere UI, Mobil-Grafik), fragt TouchMode ab. Auf dem PC ist
    // der Wert false – die PC-Version verhält sich exakt wie vorher.
    //
    // Aktiv, wenn: Android-/iOS-Build, im Editor der Device Simulator (Application.isMobilePlatform) oder der Editor-Schalter
    // „BuddyTD → Mobile → Touch-Modus im Editor erzwingen“ (EditorPrefs, nur im Editor).
    public static class MobilePlatform
    {
        public const string ForceEditorPrefKey = "BuddyTD.ForceTouchMode";

        // Mindestgröße für Touch-Ziele: Apple HIG 44 pt, Material 48 dp ≈ 9 mm. Wir rechnen mit 48 dp.
        public const float MinTouchTargetDp = 48f;

        private static bool? _override;

        public static bool TouchMode
        {
            get
            {
                if (_override.HasValue) return _override.Value;
#if UNITY_ANDROID || UNITY_IOS
                return true;
#else
                if (Application.isMobilePlatform) return true; // Device Simulator im Editor
#if UNITY_EDITOR
                if (UnityEditor.EditorPrefs.GetBool(ForceEditorPrefKey, false)) return true;
#endif
                return false;
#endif
            }
        }

        // Nur für Tests: Modus fest setzen (null = automatisch)
        public static void SetOverride(bool? value) => _override = value;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _override = null;

        // Bildschirm-DPI mit Rückfall (Editor/Desktop liefern teils 0)
        public static float Dpi
        {
            get
            {
#if UNITY_EDITOR
                float sim = SimulatedDpi;
                if (sim > 1f) return sim;
#endif
                return Screen.dpi > 1f ? Screen.dpi : 160f;
            }
        }

        // dp → Pixel (Android-Definition: 1 dp = 1 px bei 160 dpi)
        public static float DpToPixels(float dp) => dp * Dpi / 160f;

        // Bildschirmdiagonale in Zoll (Telefon < ~7", Tablet ≥ ~7")
        public static float DiagonalInches
        {
            get
            {
                float w = Screen.width / Dpi, h = Screen.height / Dpi;
                return Mathf.Sqrt(w * w + h * h);
            }
        }

        public static bool IsPhone => TouchMode && DiagonalInches < 7.5f;

        // Sicherer Bereich (Notch, Dynamic Island, Home-Indikator, abgerundete Ecken) in Pixeln
        public static Rect SafeArea
        {
            get
            {
#if UNITY_EDITOR
                if (TryGetSimulatedInsets(out Vector4 i))
                    return new Rect(i.x, i.y, Mathf.Max(1f, Screen.width - i.x - i.z), Mathf.Max(1f, Screen.height - i.y - i.w));
#endif
                return Screen.safeArea;
            }
        }

#if UNITY_EDITOR
        // ---- Nur Editor: Geräte-Nachbildung ohne Device Simulator (Game-View in Geräteauflösung + Touch-Modus erzwingen).
        // Gilt nur im Touch-Modus; die PC-Version liest weiter Screen.dpi/Screen.safeArea. Werte liegen in SessionState
        // (Editor-Sitzung, nichts wird gespeichert). Menü: „BuddyTD → Mobile → Gerät nachbilden …“.
        public const string SimDpiKey = "BuddyTD.SimDpi";            // float, 0 = aus
        public const string SimInsetsKey = "BuddyTD.SimSafeInsets";  // "links,unten,rechts,oben" in Pixeln, leer = aus

        private static float SimulatedDpi => TouchMode ? UnityEditor.SessionState.GetFloat(SimDpiKey, 0f) : 0f;

        private static bool TryGetSimulatedInsets(out Vector4 insets)
        {
            insets = Vector4.zero;
            if (!TouchMode) return false;
            string s = UnityEditor.SessionState.GetString(SimInsetsKey, "");
            if (string.IsNullOrEmpty(s)) return false;
            var p = s.Split(',');
            if (p.Length != 4) return false;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            for (int k = 0; k < 4; k++)
            {
                if (!float.TryParse(p[k], System.Globalization.NumberStyles.Float, inv, out float v)) return false;
                insets[k] = Mathf.Max(0f, v);
            }
            return true;
        }

        public static void SetSimulation(float dpi, Vector4 insets)
        {
            UnityEditor.SessionState.SetFloat(SimDpiKey, dpi);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            UnityEditor.SessionState.SetString(SimInsetsKey, insets == Vector4.zero ? "" :
                string.Format(inv, "{0},{1},{2},{3}", insets.x, insets.y, insets.z, insets.w));
        }

        public static void ClearSimulation()
        {
            UnityEditor.SessionState.EraseFloat(SimDpiKey);
            UnityEditor.SessionState.EraseString(SimInsetsKey);
        }
#endif
    }
}
