using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace ElementalBuddies
{
    // Mobil-Grafik zur Laufzeit (Paket P, 2026-10-20). Nur im Touch-Modus; auf dem PC wird nichts angelegt.
    // Statische Werte (Schatten 1 Kaskade/40 m/1024, keine weichen Schatten, kein MSAA, Depth/Opaque aus) stehen in der
    // Qualitätsstufe „Mobile“ (Mobile_RPAsset, Standard für Android/iOS). Hier kommt hinzu, was vom Gerät abhängt:
    //  - Render Scale je Gerät (MobileQualityRules.RenderScale) + Rückfall bei Dauer-Bildrate < 50 fps (erst Render Scale
    //    −0,05 bis 0,6, dann 30 fps)
    //  - Low-Tier (Android-Einstieg, z. B. Tab M11/Mali-G52): HDR aus und Bloom aus (Override-Volume); iOS behält Bloom
    //  - Depth-/Opaque-Texture pro Kamera nur in Szenen mit StylizedWater (Walddorf: Tiefe für Ufer/Schaum, Opaque für Brechung)
    //  - Gras (Terrain-Details) dünner und kürzer, weniger Feuerlichter, Treffer-/Todes-Effekte mit kleinerem Budget
    //  - Application.targetFrameRate 60
    // Im Editor (Touch-Modus erzwungen) werden geänderte Asset-/Statikwerte beim Beenden wiederhergestellt.
    public class MobileQuality : MonoBehaviour
    {
        public static MobileQuality Instance { get; private set; }

        public MobileQualityRules.Tier Tier { get; private set; }
        public float CurrentRenderScale { get; private set; }
        public int TargetFps { get; private set; } = 60;

        private const string WaterShaderName = "BuddyTD/StylizedWater";
        private const float CameraCheckInterval = 1f;

        private UniversalRenderPipelineAsset _asset;
        private float _origRenderScale = -1f;
        private int _origTargetFps;
        private int _origFireLights;
        private Volume _bloomOff;
        private Camera _lastCam;
        private float _nextCamCheck;
        private bool _sceneHasWater;
        private float _fpsTime;
        private int _fpsFrames;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!MobilePlatform.TouchMode || Instance != null) return;
            var go = new GameObject("MobileQuality");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<MobileQuality>();
        }

        void Awake()
        {
            bool ios = Application.platform == RuntimePlatform.IPhonePlayer ||
                       SystemInfo.operatingSystem.StartsWith("iOS") || SystemInfo.operatingSystem.StartsWith("iPadOS");
            Tier = MobileQualityRules.Classify(ios, SystemInfo.graphicsDeviceName, SystemInfo.systemMemorySize);
#if UNITY_EDITOR
            // Editor (Touch-Modus erzwungen, kein Device Simulator): Geräteklasse nach simulierter DPI –
            // ≥ 400 dpi = iPhone-Spitzengerät (High), sonst Tablet der Einstiegsklasse (Low)
            if (!Application.isMobilePlatform) Tier = MobilePlatform.Dpi >= 400f ? MobileQualityRules.Tier.High : MobileQualityRules.Tier.Low;
#endif
            _asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            float shortSide = Mathf.Min(Screen.width, Screen.height);
            CurrentRenderScale = MobileQualityRules.RenderScale(Tier, MobilePlatform.IsPhone, MobilePlatform.Dpi, shortSide);

            _origTargetFps = Application.targetFrameRate;
            _origFireLights = BuildingDamage.MaxFireLights;
            if (_asset != null)
            {
                _origRenderScale = _asset.renderScale;
                _asset.renderScale = CurrentRenderScale;
            }
            TargetFps = 60;
            Application.targetFrameRate = TargetFps;
#if UNITY_EDITOR
            // Beim Beenden des Play Mode übernimmt der Editor einen in OnDestroy gesetzten Wert nicht zuverlässig
            _editorOrigFps = _origTargetFps;
            UnityEditor.EditorApplication.playModeStateChanged -= RestoreEditorFps;
            UnityEditor.EditorApplication.playModeStateChanged += RestoreEditorFps;
#endif
            BuildingDamage.MaxFireLights = MobileQualityRules.FireLights(Tier);
            HitVfx.SetBudgetScale(MobileQualityRules.VfxBudgetScale(Tier));
            if (Tier == MobileQualityRules.Tier.Low) CreateBloomOverride();

            SceneManager.sceneLoaded += HandleSceneLoaded;
            ApplyScene();
            Debug.Log($"[MobileQuality] Stufe {Tier}, Render Scale {CurrentRenderScale:0.00}, {TargetFps} fps, GPU '{SystemInfo.graphicsDeviceName}', {MobilePlatform.Dpi:0} dpi");
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            // Asset- und Statikwerte zurück (wichtig im Editor: Assets/Statiken überleben den Play Mode)
            if (_asset != null && _origRenderScale > 0f) _asset.renderScale = _origRenderScale;
            Application.targetFrameRate = _origTargetFps;
            BuildingDamage.MaxFireLights = _origFireLights;
            HitVfx.SetBudgetScale(1f);
        }

#if UNITY_EDITOR
        private static int _editorOrigFps = -1;

        private static void RestoreEditorFps(UnityEditor.PlayModeStateChange state)
        {
            if (state != UnityEditor.PlayModeStateChange.EnteredEditMode) return;
            Application.targetFrameRate = _editorOrigFps;
            UnityEditor.EditorApplication.delayCall += () => Application.targetFrameRate = _editorOrigFps;
            UnityEditor.EditorApplication.playModeStateChanged -= RestoreEditorFps;
        }
#endif

        private void HandleSceneLoaded(Scene s, LoadSceneMode mode) => ApplyScene();

        private void ApplyScene()
        {
            _sceneHasWater = SceneUsesWater();
            _lastCam = null;
            _nextCamCheck = 0f;
            ApplyCamera();
            ApplyTerrain();
            ApplyFeedback();
        }

        void Update()
        {
            if (Time.unscaledTime >= _nextCamCheck)
            {
                _nextCamCheck = Time.unscaledTime + CameraCheckInterval;
                if (Camera.main != _lastCam) ApplyCamera();
            }
            TrackFrameRate();
        }

        // ---------------- Kamera: HDR, Depth/Opaque ----------------

        private void ApplyCamera()
        {
            var cam = Camera.main;
            _lastCam = cam;
            if (cam == null) return;
            if (Tier == MobileQualityRules.Tier.Low) cam.allowHDR = false;
            cam.allowMSAA = false;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null) return;
            var opt = _sceneHasWater ? CameraOverrideOption.On : CameraOverrideOption.Off;
            data.requiresDepthOption = opt;
            data.requiresColorOption = opt;
        }

        private static bool SceneUsesWater()
        {
            var renderers = FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var r in renderers)
            {
                var m = r.sharedMaterial;
                if (m != null && m.shader != null && m.shader.name == WaterShaderName) return true;
            }
            return false;
        }

        // ---------------- Bloom aus (Low) ----------------

        private void CreateBloomOverride()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "MobileBloomOff";
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0f);
            var go = new GameObject("MobileBloomOff");
            go.transform.SetParent(transform, false);
            _bloomOff = go.AddComponent<Volume>();
            _bloomOff.isGlobal = true;
            _bloomOff.priority = 1000f;
            _bloomOff.sharedProfile = profile;
        }

        // ---------------- Gras, Feedback ----------------

        private void ApplyTerrain()
        {
            float density = MobileQualityRules.DetailDensityScale(Tier);
            float dist = MobileQualityRules.DetailDistance(Tier);
            foreach (var t in Terrain.activeTerrains)
            {
                if (t == null) continue;
                t.detailObjectDensity = Mathf.Min(t.detailObjectDensity, density);
                t.detailObjectDistance = Mathf.Min(t.detailObjectDistance, dist);
                t.treeDistance = Mathf.Min(t.treeDistance, 120f);
            }
        }

        private void ApplyFeedback()
        {
            var fd = FindFirstObjectByType<FeedbackDirector>();
            if (fd == null) return;
            // Todes-Effekte pro Sekunde (PC 24): Szenenobjekt, wird beim Neuladen ohnehin neu gelesen
            fd.DeathVfxPerSecond = Mathf.Min(fd.DeathVfxPerSecond, 24f * MobileQualityRules.VfxBudgetScale(Tier));
        }

        // ---------------- Bildraten-Rückfall ----------------

        private void TrackFrameRate()
        {
            if (Time.timeScale <= 0f) { _fpsTime = 0f; _fpsFrames = 0; return; } // Pause/Menüs nicht mitzählen
            _fpsTime += Time.unscaledDeltaTime;
            _fpsFrames++;
            if (_fpsTime < MobileQualityRules.FallbackWindow) return;
            float fps = _fpsFrames / _fpsTime;
            _fpsTime = 0f;
            _fpsFrames = 0;
#if UNITY_EDITOR
            if (!Application.isMobilePlatform) return; // Editor-Bildrate sagt nichts über das Gerät
#endif
            if (fps >= MobileQualityRules.FallbackFps * TargetFps / 60f) return;
            float scale = CurrentRenderScale;
            int target = TargetFps;
            MobileQualityRules.Fallback(ref scale, ref target);
            if (Mathf.Approximately(scale, CurrentRenderScale) && target == TargetFps) return;
            CurrentRenderScale = scale;
            TargetFps = target;
            if (_asset != null) _asset.renderScale = scale;
            Application.targetFrameRate = target;
            Debug.Log($"[MobileQuality] Bildrate {fps:0} fps → Render Scale {scale:0.00}, Ziel {target} fps");
        }
    }
}
