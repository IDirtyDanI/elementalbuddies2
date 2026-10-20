using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ElementalBuddies
{
    // Startet die Mobil-UI (Paket U) im Touch-Modus: findet alle Wurzel-Canvases (auch später zur Laufzeit gebaute wie
    // TeamHUD, Hinweise, Erfolgs-Popup) und hängt an
    //  - MobileUiScaler (Touch-Skalierung) an jeden Canvas mit „Scale With Screen Size“ sowie an den FeedbackCanvas
    //    (Schadenszahlen, ohne Scaler). Canvases mit konstanter Pixel-/Physikgröße (Touch-Steuerung, Bau-✓/✗) bleiben
    //    unangetastet, ebenso Canvases mit MobileUiScaler.Exclude.
    //  - MobileUiTouchTargets (Trefferflächen ≥ 48 dp) an dieselben Canvases.
    //  - SafeAreaFitter an HUD-Canvas (HUDManager) und Hauptmenü (MainMenuUI, nur links/rechts – die Komposition füllt
    //    die Höhe, unten liegt nichts Bedienbares im Bereich des Home-Indikators).
    // Läuft vor den Start-Methoden der Szene (AfterSceneLoad bzw. sceneLoaded), damit die Umhängung in den sicheren
    // Bereich schon steht. Auf dem PC wird nichts angelegt.
    public class MobileUiRuntime : MonoBehaviour
    {
        private const float ScanInterval = 0.5f;
        private float _nextScan;

        public static MobileUiRuntime Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!MobilePlatform.TouchMode || Instance != null) return;
            var go = new GameObject("MobileUiRuntime");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<MobileUiRuntime>();
            SceneManager.sceneLoaded += HandleSceneLoaded;
            Scan();
        }

        private static void HandleSceneLoaded(Scene s, LoadSceneMode mode)
        {
            if (Instance != null) Scan();
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        void Update()
        {
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + ScanInterval;
            Scan();
        }

        // Alle aktiven Wurzel-Canvases prüfen (wenige Objekte, alle 0,5 s)
        public static void Scan()
        {
            if (!MobilePlatform.TouchMode) return;
            var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var c in canvases)
            {
                if (c == null || !c.isRootCanvas || c.renderMode == RenderMode.WorldSpace) continue;
                if (c.GetComponent<MobileUiScaler>() != null) continue;
                Setup(c);
            }
        }

        private static void Setup(Canvas c)
        {
            var scaler = c.GetComponent<CanvasScaler>();
            bool feedback = scaler == null && c.GetComponent<FeedbackCanvas>() != null;
            bool scaled = feedback || scaler != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize;
            if (!scaled)
            {
                MobileUiScaler.Exclude(c); // merken, nicht erneut prüfen
                return;
            }

            bool menu = c.GetComponentInChildren<MainMenuUI>(true) != null;
            bool hud = c.GetComponentInChildren<HUDManager>(true) != null;
            // Hauptmenü: Komposition füllt die 1350er-Höhe (Champion-Karten, Schwierigkeit, Map, Knöpfe) → nicht verstärken
            MobileUiScaler.Attach(c, feedback, menu ? 1f : 10f);
            if (feedback) return; // Schadenszahlen: weder Knöpfe noch Ränder

            if (c.GetComponent<MobileUiTouchTargets>() == null) c.gameObject.AddComponent<MobileUiTouchTargets>();
            if (hud) SafeAreaFitter.Attach(c, true, true);
            else if (menu) SafeAreaFitter.Attach(c, true, false);
        }
    }
}
