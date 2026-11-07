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
    //  - Low: Mobil-Gras (MobileGrass: weniger, größere Büschel, sparsamer Shader) und geschärfte Terrain-Basemap;
    //    weniger Feuerlichter, Treffer-/Todes-Effekte mit kleinerem Budget
    //  - Application.targetFrameRate: High 60, Low fest 30 (Gerätetest 2026-11-02)
    //  - Gerätetest Tab M11 (2026-10-20: GPU-gebunden, ~19 fps): Mobile_Renderer Forward statt Forward+, Low mit höchstens
    //    2 Zusatzlichtern pro Objekt, ohne Gegner-Schatten und ohne Post-Processing; PerfProbe danach: Terrain-Basemap
    //    statt Schichten (Low), Zusatzlichter im Mobile_RPAsset pro Vertex statt pro Pixel
    // Im Editor (Touch-Modus erzwungen) werden geänderte Asset-/Statikwerte beim Beenden wiederhergestellt.
    public class MobileQuality : MonoBehaviour
    {
        public static MobileQuality Instance { get; private set; }

        public MobileQualityRules.Tier Tier { get; private set; }
        // Von der Erkennung bestimmte Stufe (Tier = diese oder die Wahl im Pausenmenü)
        public MobileQualityRules.Tier DetectedTier { get; private set; }
        public MobileQualityRules.Choice Choice { get; private set; }
        public const string PrefChoice = "MobileGraphics";
        public float CurrentRenderScale { get; private set; }
        public int TargetFps { get; private set; } = 60;

        private const string WaterShaderName = "BuddyTD/StylizedWater";
        private const float CameraCheckInterval = 1f;

        private UniversalRenderPipelineAsset _asset;
        private float _origRenderScale = -1f;
        private int _origTargetFps;
        // Gerätewerte vor jedem Rückfall (Ziel beim Zurücknehmen am Wellenende)
        private float _baseRenderScale;
        private int _baseTargetFps;
        private bool _waveWasActive;
        private int _origFireLights;
        private int _origMaxLights = -1;
        private float _origShadowDistance = -1f;
        private float _nextShadowScan;
        private readonly System.Collections.Generic.HashSet<int> _shadowOff = new System.Collections.Generic.HashSet<int>();
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
            DetectedTier = MobileQualityRules.Classify(ios, SystemInfo.graphicsDeviceName, SystemInfo.systemMemorySize);
#if UNITY_EDITOR
            // Editor (Touch-Modus erzwungen, kein Device Simulator): Geräteklasse nach simulierter DPI –
            // ≥ 400 dpi = iPhone-Spitzengerät (High), sonst Tablet der Einstiegsklasse (Low)
            if (!Application.isMobilePlatform) DetectedTier = MobilePlatform.Dpi >= 400f ? MobileQualityRules.Tier.High : MobileQualityRules.Tier.Low;
#endif
            int saved = PlayerPrefs.GetInt(PrefChoice, 0);
            Choice = saved >= 0 && saved <= 2 ? (MobileQualityRules.Choice)saved : MobileQualityRules.Choice.Auto;
            Tier = MobileQualityRules.Resolve(Choice, DetectedTier);
            _asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

            _origTargetFps = Application.targetFrameRate;
            _origFireLights = BuildingDamage.MaxFireLights;
            if (_asset != null)
            {
                _origRenderScale = _asset.renderScale;
                _origMaxLights = _asset.maxAdditionalLightsCount;
                _origShadowDistance = _asset.shadowDistance;
            }
            ApplyTierGlobals();
#if UNITY_EDITOR
            // Beim Beenden des Play Mode übernimmt der Editor einen in OnDestroy gesetzten Wert nicht zuverlässig
            _editorOrigFps = _origTargetFps;
            UnityEditor.EditorApplication.playModeStateChanged -= RestoreEditorFps;
            UnityEditor.EditorApplication.playModeStateChanged += RestoreEditorFps;
#endif
            SceneManager.sceneLoaded += HandleSceneLoaded;
            ApplyScene();
            Debug.Log($"[MobileQuality] Stufe {Tier} (erkannt {DetectedTier}, Wahl {Choice}), Render Scale {CurrentRenderScale:0.00}, {TargetFps} fps, GPU '{SystemInfo.graphicsDeviceName}', {MobilePlatform.Dpi:0} dpi");
        }

        // Grafik-Wahl aus dem Pausenmenü: gespeichert; Auflösung, Bildrate, Lichter, Schatten, Bloom und Effekte sofort,
        // Boden/Gras/Kamera (Szenenobjekte) ab der nächsten geladenen Karte
        public void SetChoice(MobileQualityRules.Choice choice)
        {
            Choice = choice;
            PlayerPrefs.SetInt(PrefChoice, (int)choice);
            PlayerPrefs.Save();
            var tier = MobileQualityRules.Resolve(choice, DetectedTier);
            if (tier == Tier) return;
            Tier = tier;
            ApplyTierGlobals();
            Debug.Log($"[MobileQuality] Wahl {choice} → Stufe {Tier}, Render Scale {CurrentRenderScale:0.00}, {TargetFps} fps");
        }

        // Werte, die nicht an der Szene hängen (Asset, Statiken, Bloom-Override)
        private void ApplyTierGlobals()
        {
            float shortSide = Mathf.Min(Screen.width, Screen.height);
            CurrentRenderScale = MobileQualityRules.RenderScale(Tier, MobilePlatform.IsPhone, MobilePlatform.Dpi, shortSide);
            if (_asset != null)
            {
                _asset.renderScale = CurrentRenderScale;
                _asset.maxAdditionalLightsCount = MobileQualityRules.MaxAdditionalLightsPerObject(Tier);
                _asset.shadowDistance = Mathf.Min(_origShadowDistance, MobileQualityRules.ShadowDistance(Tier));
            }
            TargetFps = MobileQualityRules.TargetFps(Tier);
            _baseRenderScale = CurrentRenderScale;
            _baseTargetFps = TargetFps;
            _fpsTime = 0f;
            _fpsFrames = 0;
            Application.targetFrameRate = TargetFps;
            BuildingDamage.MaxFireLights = MobileQualityRules.FireLights(Tier);
            HitVfx.SetBudgetScale(MobileQualityRules.VfxBudgetScale(Tier));
            bool bloomOff = Tier == MobileQualityRules.Tier.Low;
            if (bloomOff && _bloomOff == null) CreateBloomOverride();
            else if (!bloomOff && _bloomOff != null) { Destroy(_bloomOff.gameObject); _bloomOff = null; }
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            // Asset- und Statikwerte zurück (wichtig im Editor: Assets/Statiken überleben den Play Mode)
            if (_asset != null && _origRenderScale > 0f) _asset.renderScale = _origRenderScale;
            if (_asset != null && _origMaxLights >= 0) _asset.maxAdditionalLightsCount = _origMaxLights;
            if (_asset != null && _origShadowDistance >= 0f) _asset.shadowDistance = _origShadowDistance;
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
            MobileFontWarmup.Run();
        }

        void Update()
        {
            if (Time.unscaledTime >= _nextCamCheck)
            {
                _nextCamCheck = Time.unscaledTime + CameraCheckInterval;
                if (Camera.main != _lastCam) ApplyCamera();
            }
            if (!MobileQualityRules.EnemyShadows(Tier) && Time.unscaledTime >= _nextShadowScan)
            {
                _nextShadowScan = Time.unscaledTime + 0.5f;
                DisableEnemyShadows();
            }
            TrackFrameRate();
        }

        // Neue Gegner (auch Beschworene, Bosse) ohne Schattenwurf; jeder Gegner nur einmal
        private void DisableEnemyShadows()
        {
            var all = EnemyBrain.All;
            for (int i = 0; i < all.Count; i++)
            {
                var e = all[i];
                if (e == null || !_shadowOff.Add(e.GetInstanceID())) continue;
                foreach (var r in e.GetComponentsInChildren<Renderer>(true))
                    r.shadowCastingMode = ShadowCastingMode.Off;
            }
            if (_shadowOff.Count > 4096) _shadowOff.Clear();
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
            if (!MobileQualityRules.PostProcessing(Tier)) data.renderPostProcessing = false;
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
            MobileGrass.Cleanup();
            foreach (var t in Terrain.activeTerrains)
            {
                if (t == null) continue;
                t.detailObjectDensity = Mathf.Min(t.detailObjectDensity, density);
                t.detailObjectDistance = Mathf.Min(t.detailObjectDistance, dist);
                t.treeDistance = Mathf.Min(t.treeDistance, 120f);
                if (Tier == MobileQualityRules.Tier.Low)
                {
                    // Gerätetest Tab M11 (PerfProbe): überall die (in MobileGrass geschärfte) Basemap, gröberes Mesh
                    t.basemapDistance = MobileQualityRules.LowTerrainBasemapDistance;
                    t.heightmapPixelError = Mathf.Max(t.heightmapPixelError, MobileQualityRules.LowTerrainPixelError);
                    // PerfProbe in der Welle: Terrain kostet trotz Basemap noch ~6 ms und Render Scale hilft kaum →
                    // Geometrie-gebunden; das Terrain wirft auf Low keinen Schatten (spart den zweiten Durchgang)
                    t.shadowCastingMode = ShadowCastingMode.Off;
                    // Terrain-Bäume: kürzere Sichtweite, früher Billboards
                    t.treeDistance = Mathf.Min(t.treeDistance, MobileQualityRules.LowTreeDistance);
                    t.treeBillboardDistance = Mathf.Min(t.treeBillboardDistance, MobileQualityRules.LowTreeBillboardDistance);
                }
                if (MobileQualityRules.MobileGrass(Tier))
                    MobileGrass.Apply(t, MobileQualityRules.GrassInstanceShare(Tier), MobileQualityRules.GrassSizeScale(Tier),
                        MobileQualityRules.LowTerrainBasemapResolution);
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
            RestoreAfterWave();
            if (Time.timeScale <= 0f || MobilePerfProbe.Active) { _fpsTime = 0f; _fpsFrames = 0; return; } // Pause/Menüs/Messung nicht mitzählen
            // Lade-/Szenenwechsel-Bilder (mehrere Sekunden) verwerfen das Fenster, sonst „0 fps“ direkt nach dem Start
            if (Time.unscaledDeltaTime > MobileQualityRules.FallbackMaxFrameTime) { _fpsTime = 0f; _fpsFrames = 0; return; }
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

        // Wellenende: Rückfall zurücknehmen (MobileQualityRules.ShouldRestore)
        private void RestoreAfterWave()
        {
            var wm = WaveManager.Instance;
            bool active = wm != null && wm.IsWaveActive;
            bool restore = MobileQualityRules.ShouldRestore(_waveWasActive, active, CurrentRenderScale, _baseRenderScale, TargetFps, _baseTargetFps);
            _waveWasActive = active;
            if (!restore) return;
            CurrentRenderScale = _baseRenderScale;
            TargetFps = _baseTargetFps;
            if (_asset != null) _asset.renderScale = CurrentRenderScale;
            Application.targetFrameRate = TargetFps;
            _fpsTime = 0f;
            _fpsFrames = 0;
            Debug.Log($"[MobileQuality] Wellenende → zurück auf Render Scale {CurrentRenderScale:0.00}, {TargetFps} fps");
        }
    }
}
