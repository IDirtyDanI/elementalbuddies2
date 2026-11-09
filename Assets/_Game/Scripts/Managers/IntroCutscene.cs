using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ElementalBuddies
{
    // Intro-Film zum Start von Welle 1 (wiki: Design/Intro-Cutscene). Läuft auf jedem Rechner lokal und live: Die Welle
    // läuft weiter, die echten Gegner werden gefilmt, gesperrt ist nur die eigene Eingabe (InteractionManager.
    // LocalInputBlocked, PlayerController, PlayerAbilities). Keine Netz-RPCs; im Koop überspringt jeder für sich, wer
    // nach dem Start von Welle 1 beitritt, sieht keinen Film (OnWaveStart feuert nur live).
    // Ablauf: Auslöser WaveManager.OnWaveStart bei CurrentWaveIndex 0 (Server und Clients). Kamerabahn aus dem NavMesh-
    // Pfad vom ersten offenen Portal zum Nexus (IntroCutscenePath, reine Mathematik). CameraFollow ist so lange
    // ausgesetzt (Suspended) und liefert die Ziel-Lage für die Rückblende.
    // UI (Kinobalken, Titel, „Überspringen“) wird zur Laufzeit gebaut; HUD-Canvases werden per CanvasGroup ausgeblendet
    // (kein SetActive → nichts initialisiert neu), die Touch-Steuerung blendet sich über LocalInputBlocked selbst aus.
    // Das Objekt legt sich selbst in jeder Spielszene an (Szene mit WaveManager), Prefab/Szene bleiben unverändert.
    // Test im Editor (Play Mode): IntroCutscene.PlayForTest().
    [DefaultExecutionOrder(-45)] // nach CameraFollow (−50), vor Billboards/OcclusionFader
    public class IntroCutscene : MonoBehaviour
    {
        public const string PrefEnabled = "IntroCutsceneEnabled";
        private const int SortingOrder = 900; // über allem (Tipps 75, Popups …)
        private const float SkipGrace = 0.4f; // so lange nach dem Start zählt keine Überspringen-Eingabe (Klick auf „Welle starten“)
        private const float BarShare = 0.115f; // Höhe eines Kinobalkens (Anteil der Bildhöhe)

        // ---------------- Öffentlicher Zustand ----------------

        // Schalter „Intro-Film zeigen“ (Pause-Menü und Hauptmenü), Standard an
        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(PrefEnabled, 1) != 0;
            set { PlayerPrefs.SetInt(PrefEnabled, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        // Film läuft (inkl. Einblendung und Rückblende): lokale Eingabe gesperrt
        public static bool IsPlaying { get; private set; }

        // Faktor auf die Musiklautstärke (MusicDirector): während des Films leiser
        public static float MusicGain { get; private set; } = 1f;

        // Esc gehört dem Film (überspringen), nicht dem Pause-Menü – auch im Bild direkt nach dem Ende
        public static bool ConsumesEscape =>
            (IsPlaying && !PauseManager.IsPaused) || Time.frameCount - _lastPlayingFrame <= 1;

        // Gefilmter Punkt (für den OcclusionFader); false, solange die Spielkamera gilt
        public static bool TryGetFocus(out Vector3 focus)
        {
            focus = default;
            var c = _instance;
            if (c == null || !IsPlaying || c._path == null || c._returning || c._t < IntroCutscenePath.FadeInEnd) return false;
            focus = c._path.FocusAt(c._t);
            return true;
        }

        // Editor/Tests: Film sofort starten (Play Mode, unabhängig von Welle und Schalter)
        public static void PlayForTest()
        {
            EnsureInstance();
            if (_instance == null)
            {
                Debug.LogWarning("[IntroCutscene] Keine Spielszene (kein WaveManager) – kein Film.");
                return;
            }
            _instance.Begin(true);
        }

        // Editor/Tests: Filmzeit setzen (z. B. 9,5 s = Einstellung 3) und Abspieltempo (0 = Standbild für Screenshots)
        public static void SeekForTest(float t)
        {
            if (_instance != null && IsPlaying && !_instance._returning) _instance._t = Mathf.Clamp(t, 0f, IntroCutscenePath.Shot3End);
        }

        public static float TestSpeed { get; set; } = 1f;

        // Laufenden Film überspringen (Button, Esc/Leertaste/Klick/Tippen)
        public static void Skip()
        {
            if (_instance != null) _instance.SkipInternal();
        }

        // ---------------- Selbst-Anlage ----------------

        private static IntroCutscene _instance;
        private static int _lastPlayingFrame = -10;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
            IsPlaying = false;
            MusicGain = 1f;
            TestSpeed = 1f;
            _lastPlayingFrame = -10;
            OwnGroups.Clear();
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            EnsureInstance();
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => EnsureInstance();

        // Nur in Spielszenen (WaveManager vorhanden); das Objekt gehört zur Szene des WaveManagers und stirbt mit ihr
        private static void EnsureInstance()
        {
            if (_instance != null) return;
            var waves = WaveManager.Instance;
            if (waves == null) return;
            var go = new GameObject("IntroCutscene");
            if (waves.gameObject.scene.IsValid()) SceneManager.MoveGameObjectToScene(go, waves.gameObject.scene);
            _instance = go.AddComponent<IntroCutscene>();
        }

        // ---------------- Laufzeit ----------------

        private WaveManager _waves;
        private bool _played;           // dieser Run hat den Film schon gezeigt
        private IntroCutscenePath _path;
        private float _t;
        private bool _returning;
        private float _returnStart = float.PositiveInfinity, _returnDuration = IntroCutscenePath.ReturnDuration;
        private CutscenePose _startPose, _returnFrom;
        private Transform _cam;
        private bool _soundPlayed;
        private float _columnS = float.NaN, _columnVel;
        private float _lift, _liftVel;
        private float _blackAtSkip;
        private int _groundMask;

        // UI
        private Canvas _canvas;
        private RectTransform _topBar, _bottomBar;
        private Image _black;
        private CanvasGroup _titleGroup, _skipGroup;
        private RectTransform _skipRt;
        private readonly List<HiddenCanvas> _hidden = new List<HiddenCanvas>();
        private static readonly HashSet<CanvasGroup> OwnGroups = new HashSet<CanvasGroup>(); // von uns angelegte Gruppen
        private float _nextCanvasScan;

        private class HiddenCanvas
        {
            public Canvas Canvas;
            public CanvasGroup Group;     // eigene (von uns angelegte) Gruppe: Alpha weich
            public bool OwnGroup;
            public bool DisabledByUs;     // fremde Gruppe vorhanden: Canvas zur Hälfte der Blende aus-/einschalten
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            _groundMask = Physics.DefaultRaycastLayers;
            foreach (var layer in new[] { "Enemy", "Buddy", "Player", "Ignore Raycast", "UI" })
            {
                int l = LayerMask.NameToLayer(layer);
                if (l >= 0) _groundMask &= ~(1 << l);
            }
        }

        void OnDestroy()
        {
            if (_waves != null) _waves.OnWaveStart -= HandleWaveStart;
            if (_instance != this) return;
            if (IsPlaying) Finish();
            _instance = null;
        }

        void Update()
        {
            if (_waves == null && WaveManager.Instance != null)
            {
                _waves = WaveManager.Instance;
                _waves.OnWaveStart += HandleWaveStart;
            }
            if (IsPlaying && !_returning && _t >= SkipGrace && !PauseManager.IsPaused && SkipPressed()) SkipInternal();
        }

        private void HandleWaveStart()
        {
            if (_played || IsPlaying || _waves == null) return;
            if (_waves.CurrentWaveIndex != 0) return; // nur Welle 1 (DevTools-Startwelle > 1: kein Film)
            _played = true;
            if (!Enabled) return;
            if (GameManager.Instance != null && GameManager.Instance.IsGameOver) return;
            // Lokal ein Fenster offen (Koop: Pause-Overlay, Kartenwahl) → kein Film, das Fenster hat Vorrang
            if (PauseManager.IsPaused || KeyBindingsMenu.IsOpen) return;
            if (UpgradeManager.Instance != null && UpgradeManager.Instance.IsChoosing) return;
            if (MerchantManager.Instance != null && MerchantManager.Instance.IsChoosing) return;
            Begin(false);
        }

        private void Begin(bool test)
        {
            if (IsPlaying) Finish();
            _cam = CameraFollow.CameraTransform;
            if (_cam == null && Camera.main != null) _cam = Camera.main.transform;
            if (_cam == null) return;

            var nexus = Nexus.Instance;
            var portal = PickPortal();
            if (nexus == null || portal == null)
            {
                if (test) Debug.LogWarning("[IntroCutscene] Kein Nexus oder Portal gefunden – kein Film.");
                return;
            }

            Vector3 portalPos = portal.SpawnTransform.position;
            Vector3 nexusPos = nexus.transform.position;
            _path = new IntroCutscenePath(FindPath(portalPos, nexusPos), portalPos, nexusPos);
            // Spawnpunkt liegt vor dem Portalbogen: Kamera-Abstand ab dem Bogen rechnen
            _path.PortalSetback = Mathf.Clamp(Vector3.Dot(portalPos - portal.transform.position, _path.StartDir), 0f, 6f);
            ChooseNexusFraming(_path);

            _startPose = new CutscenePose(_cam.position, _cam.rotation);
            _t = 0f;
            _returning = false;
            _returnStart = float.PositiveInfinity;
            _returnDuration = IntroCutscenePath.ReturnDuration;
            _soundPlayed = false;
            _columnS = float.NaN;
            _columnVel = 0f;
            _lift = _liftVel = 0f;
            _blackAtSkip = 0f;
            IsPlaying = true;
            _lastPlayingFrame = Time.frameCount;
            CameraFollow.Suspended = true;

            BuildUi();
            _canvas.gameObject.SetActive(true);
            _hidden.Clear();
            _nextCanvasScan = 0f;
            ApplyUi();
            if (test) Debug.Log($"[IntroCutscene] Film gestartet: Portal „{portal.DisplayName}“, Weglänge {_path.Length:0} m{(_path.UsedFallback ? " (Fallback: gerade Linie)" : "")}.");
        }

        // Erstes offenes Portal (bei Welle 1 in beiden Maps „Westen“). Mehrere offen: kleinste OpenFromWave, dann
        // „Westen“, dann Listenreihenfolge – unabhängig davon, in welcher Reihenfolge UpdatePortals sie geöffnet hat.
        // Noch keins offen (Client: Öffnen kommt evtl. nach dem Wellenstart): das mit der kleinsten OpenFromWave.
        private static SpawnPortal PickPortal()
        {
            SpawnPortal best = null;
            bool bestOpen = false;
            foreach (var p in SpawnPortal.All)
            {
                if (p == null) continue;
                if (best == null || Better(p, best, bestOpen))
                {
                    best = p;
                    bestOpen = p.IsOpen;
                }
            }
            return best;
        }

        private static bool Better(SpawnPortal p, SpawnPortal best, bool bestOpen)
        {
            if (p.IsOpen != bestOpen) return p.IsOpen;
            if (p.OpenFromWave != best.OpenFromWave) return p.OpenFromWave < best.OpenFromWave;
            bool west = p.DisplayName == "Westen", bestWest = best.DisplayName == "Westen";
            return west && !bestWest;
        }

        // NavMesh-Pfad Portal → Nexus (der Nexus selbst ist evtl. ausgeschnitten: nächster Punkt, Teilpfad zählt)
        private static Vector3[] FindPath(Vector3 from, Vector3 to)
        {
            if (!NavMesh.SamplePosition(from, out NavMeshHit a, 6f, NavMesh.AllAreas)) return null;
            if (!NavMesh.SamplePosition(to, out NavMeshHit b, 10f, NavMesh.AllAreas)) return null;
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path)) return null;
            if (path.status == NavMeshPathStatus.PathInvalid || path.corners.Length < 2) return null;
            return path.corners;
        }

        // Einstellung 3 frei halten: Drehsinn, Abstand/Höhe und seitlichen Versatz hinter dem Nexus so wählen, dass die
        // Bahn nicht durch Gebäude führt, die Endlage nicht in einer Baumkrone (Trigger-Volumen der FadeOccluder) steckt
        // und keine Krone/Wand direkt vor der Linse das Bild zudeckt (Walddorf: Dorflinde neben dem Nexus)
        private void ChooseNexusFraming(IntroCutscenePath path)
        {
            var sizes = new[] { new Vector2(9f, 7f), new Vector2(10.5f, 9f), new Vector2(12f, 11.5f) };
            var offsets = new[] { 0f, 25f, -25f };
            float bestScore = float.MaxValue;
            Vector2 best = sizes[0];
            float bestSign = 1f, bestOffset = 0f;
            for (int o = 0; o < sizes.Length; o++)
            {
                foreach (float offset in offsets)
                {
                    foreach (float sign in new[] { 1f, -1f })
                    {
                        path.NexusRadius = sizes[o].x;
                        path.NexusHeight = sizes[o].y;
                        path.OrbitSign = sign;
                        path.EndAngleOffset = offset;
                        float score = o * 0.5f + (offset != 0f ? 0.7f : 0f) + (sign < 0f ? 0.1f : 0f);
                        if (score >= bestScore) continue;
                        score += FramingPenalty(path);
                        if (score < bestScore)
                        {
                            bestScore = score;
                            best = sizes[o];
                            bestSign = sign;
                            bestOffset = offset;
                        }
                    }
                }
            }
            path.NexusRadius = best.x;
            path.NexusHeight = best.y;
            path.OrbitSign = bestSign;
            path.EndAngleOffset = bestOffset;
        }

        private readonly RaycastHit[] _framingHits = new RaycastHit[16];

        private float FramingPenalty(IntroCutscenePath path)
        {
            float penalty = 0f;
            for (int i = 0; i <= 10; i++)
            {
                float t = Mathf.Lerp(IntroCutscenePath.Shot2End, IntroCutscenePath.Shot3End, i / 10f);
                if (Physics.CheckSphere(path.Evaluate(t).Position, 1f, _groundMask, QueryTriggerInteraction.Ignore)) penalty += 10f;
            }
            var final = path.FinalPose;
            if (Physics.CheckSphere(final.Position, 1.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide)) penalty += 4f;
            // Sichtlinie Endlage → Nexus: nur Treffer an Nicht-FadeOccluder-Objekten zählen (die blendet der Fader aus)
            Vector3 nexusTop = path.Nexus + Vector3.up * 1.5f;
            if (Physics.Linecast(final.Position, nexusTop, out RaycastHit hit, _groundMask, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<FadeOccluder>() == null
                && hit.collider.GetComponentInParent<Nexus>() == null) penalty += 6f;
            // Vor der Linse: dicker Strahl in Blickrichtung (Kronen-Trigger zählen mit), Nexus ausgenommen
            int n = Physics.SphereCastNonAlloc(final.Position, 2.5f, final.Forward, _framingHits, 10f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var col = _framingHits[i].collider;
                if (col == null || col.GetComponentInParent<Nexus>() != null) continue;
                var occ = col.GetComponentInParent<FadeOccluder>();
                if (col.isTrigger && (occ == null || !occ.TriggerVolumes)) continue;
                penalty += 3f;
                break;
            }
            return penalty;
        }

        void LateUpdate()
        {
            if (!IsPlaying) return;
            if (_cam == null)
            {
                Finish();
                return;
            }
            _lastPlayingFrame = Time.frameCount;
            // Echtzeit (Treffer-Stopp/Zeitlupe bremsen den Film nicht), Ruckler gedeckelt: ein langes Bild überspringt
            // keine halbe Einstellung
            float dt = PauseManager.IsPaused ? 0f : Mathf.Min(Time.unscaledDeltaTime, 0.1f) * TestSpeed;
            _t += dt;

            // Ton zu Beginn von Einstellung 1 (Portal flammt auf, Kriegshorn)
            if (!_soundPlayed && !_returning && _t >= IntroCutscenePath.FadeInEnd)
            {
                _soundPlayed = true;
                PlayIntroSounds();
            }

            // Natürliches Ende von Einstellung 3 → Rückblende
            if (!_returning && _t >= IntroCutscenePath.Shot3End)
            {
                _returning = true;
                _returnStart = IntroCutscenePath.Shot3End;
                _returnDuration = IntroCutscenePath.ReturnDuration;
                _returnFrom = FilmPose(IntroCutscenePath.Shot3End, dt);
            }

            CutscenePose pose;
            if (_returning)
            {
                float k = (_t - _returnStart) / Mathf.Max(0.01f, _returnDuration);
                CutscenePose game = CameraFollow.TryGetGamePose(out Vector3 gp, out Quaternion gr) ? new CutscenePose(gp, gr) : _startPose;
                float arc = _returnDuration >= IntroCutscenePath.ReturnDuration ? 2f : 0.5f;
                pose = IntroCutscenePath.Blend(_returnFrom, game, k, arc);
                _cam.SetPositionAndRotation(pose.Position, pose.Rotation);
                if (k >= 1f)
                {
                    Finish();
                    return;
                }
            }
            else
            {
                pose = _t < IntroCutscenePath.FadeInEnd ? _startPose : FilmPose(_t, dt);
                _cam.SetPositionAndRotation(pose.Position, pose.Rotation);
            }
            ApplyUi();
        }

        // Kamera-Lage des Films: Bahn + Gegnerkolonne + Anheben über höheres Gelände (geglättet, nur tief am Portal)
        private CutscenePose FilmPose(float t, float dt)
        {
            UpdateColumn(t, dt);
            var pose = _path.Evaluate(t, _columnS);
            float need = 0f;
            if (t < IntroCutscenePath.Shot1End + 1f
                && Physics.Raycast(pose.Position + Vector3.up * 30f, Vector3.down, out RaycastHit hit, 60f, _groundMask, QueryTriggerInteraction.Ignore))
                need = Mathf.Max(0f, hit.point.y + 1.5f - pose.Position.y);
            _lift = dt > 0f ? Mathf.SmoothDamp(_lift, need, ref _liftVel, 0.25f, Mathf.Infinity, dt) : _lift;
            pose.Position += Vector3.up * _lift;
            return pose;
        }

        // Schwerpunkt (Bogenlänge) der lebenden Gegner nahe dem Weg, geglättet; ohne Gegner NaN
        private void UpdateColumn(float t, float dt)
        {
            if (t < IntroCutscenePath.Shot1End - 0.5f || t > IntroCutscenePath.Shot2End || dt <= 0f) return;
            float sum = 0f;
            int n = 0;
            var all = EnemyBrain.All;
            for (int i = 0; i < all.Count; i++)
            {
                var e = all[i];
                if (e == null || e.IsDead) continue;
                Vector3 p = e.transform.position;
                float s = _path.ProjectS(p);
                Vector3 onPath = _path.PointAt(s);
                if ((new Vector2(p.x - onPath.x, p.z - onPath.z)).sqrMagnitude > 64f) continue;
                sum += s;
                n++;
            }
            if (n == 0) return; // letzter Wert bleibt (bzw. NaN ohne Gegner)
            float target = sum / n;
            if (float.IsNaN(_columnS)) _columnS = target;
            else _columnS = Mathf.SmoothDamp(_columnS, target, ref _columnVel, 0.4f, Mathf.Infinity, dt);
        }

        private void SkipInternal()
        {
            if (!IsPlaying || _returning) return;
            _returning = true;
            _returnStart = _t;
            _returnDuration = IntroCutscenePath.SkipReturnDuration;
            _returnFrom = _cam != null ? new CutscenePose(_cam.position, _cam.rotation) : _startPose;
            _blackAtSkip = _black != null ? _black.color.a : 0f;
        }

        private void Finish()
        {
            IsPlaying = false;
            _returning = false;
            MusicGain = 1f;
            CameraFollow.Suspended = false;
            RestoreCanvases();
            if (_canvas != null) _canvas.gameObject.SetActive(false);
        }

        // Überspringen: Esc, Leertaste, Mausklick, Tippen irgendwo (Button zusätzlich über onClick)
        private static bool SkipPressed()
        {
            var kb = Keyboard.current;
            if (kb != null && (kb.escapeKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)) return true;
            var mouse = Mouse.current;
            if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)) return true;
            var ts = Touchscreen.current;
            if (ts != null)
                foreach (var touch in ts.touches)
                    if (touch.press.wasPressedThisFrame) return true;
            return false;
        }

        // Kriegshorn + Portal-Aufflammen (Klänge aus GameAudio)
        // 2D (ohne Position): der AudioListener sitzt nicht unbedingt an der Filmkamera. PlayOr: ohne zugewiesenen
        // Clip der Ersatzton aus GameAudio.Fallback.
        private static void PlayIntroSounds()
        {
            GameAudio.PlayOr(SfxId.PortalSurge);
            GameAudio.PlayOr(SfxId.IntroHorn);
        }

        // ---------------- UI ----------------

        private void ApplyUi()
        {
            float bars = IntroCutscenePath.BarsAt(_t, _returnStart, _returnDuration);
            float hide = bars;
            MusicGain = Mathf.Lerp(1f, 0.45f, bars);

            if (_topBar != null)
            {
                _topBar.anchorMin = new Vector2(0f, 1f - BarShare * bars);
                _bottomBar.anchorMax = new Vector2(1f, BarShare * bars);
            }
            if (_black != null)
            {
                float a;
                if (_returning) a = _blackAtSkip * (1f - Mathf.Clamp01((_t - _returnStart) / 0.2f));
                else a = IntroCutscenePath.CutFadeAt(_t);
                _black.color = new Color(0f, 0f, 0f, a);
            }
            if (_titleGroup != null)
            {
                float title = IntroCutscenePath.TitleAlpha(_t);
                if (_returning) title *= 1f - Mathf.Clamp01((_t - _returnStart) / 0.25f);
                _titleGroup.alpha = title;
            }
            if (_skipGroup != null)
            {
                float skip = _returning ? 0f : Mathf.Clamp01((_t - 0.3f) / 0.4f);
                _skipGroup.alpha = Mathf.MoveTowards(_skipGroup.alpha, skip, Time.unscaledDeltaTime * 5f);
                _skipGroup.interactable = _skipGroup.blocksRaycasts = !_returning;
            }
            PlaceSkipButton();
            HideCanvases(hide);
        }

        // Unten rechts im sicheren Bereich (Notch/abgerundete Ecken)
        private void PlaceSkipButton()
        {
            if (_skipRt == null || _canvas == null) return;
            float scale = Mathf.Max(0.01f, _canvas.scaleFactor);
            Rect safe = Screen.safeArea;
            float right = Mathf.Max(0f, Screen.width - safe.xMax) / scale;
            float bottom = Mathf.Max(0f, safe.yMin) / scale;
            _skipRt.anchoredPosition = new Vector2(-28f - right, 22f + bottom);
        }

        private void BuildUi()
        {
            if (_canvas != null) return;
            bool touch = MobilePlatform.TouchMode;

            var go = new GameObject("IntroCutsceneCanvas", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = SortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            var root = (RectTransform)go.transform;

            TMP_FontAsset font = null;
            var hud = FindFirstObjectByType<HUDManager>(FindObjectsInactive.Include);
            if (hud != null && hud.WaveText != null) font = hud.WaveText.font;
            else if (hud != null && hud.ShardText != null) font = hud.ShardText.font;
            if (font == null) font = TMP_Settings.defaultFontAsset;

            // Unsichtbarer Klick-Fänger: Klicks/Fingertipps gehen nicht an HUD oder Welt
            var catcher = MakeImage(root, "ClickCatcher", new Color(0f, 0f, 0f, 0f));
            Stretch(catcher.rectTransform, Vector2.zero, Vector2.one);
            catcher.raycastTarget = true;

            _black = MakeImage(root, "CutFade", new Color(0f, 0f, 0f, 0f));
            Stretch(_black.rectTransform, Vector2.zero, Vector2.one);

            var top = MakeImage(root, "BarTop", Color.black);
            _topBar = top.rectTransform;
            Stretch(_topBar, new Vector2(0f, 1f), Vector2.one);
            var bottom = MakeImage(root, "BarBottom", Color.black);
            _bottomBar = bottom.rectTransform;
            Stretch(_bottomBar, Vector2.zero, new Vector2(1f, 0f));

            // Titel „Welle 1“ + Untertitel (oberes Drittel – unten steht der Nexus im Bild)
            var titleGo = new GameObject("Title", typeof(RectTransform));
            var titleRt = (RectTransform)titleGo.transform;
            titleRt.SetParent(root, false);
            titleRt.anchorMin = new Vector2(0.1f, 0.6f);
            titleRt.anchorMax = new Vector2(0.9f, 0.84f);
            titleRt.offsetMin = titleRt.offsetMax = Vector2.zero;
            _titleGroup = titleGo.AddComponent<CanvasGroup>();
            _titleGroup.alpha = 0f;
            _titleGroup.blocksRaycasts = false;
            _titleGroup.interactable = false;
            float titleSize = touch ? 112f : 104f;
            float subSize = touch ? 50f : 44f;
            MakeShadowedText(titleRt, "Wave", "Welle 1", font, titleSize, new Color(1f, 0.88f, 0.6f), new Vector2(0f, 0.42f), Vector2.one, FontStyles.Bold);
            MakeShadowedText(titleRt, "Subtitle", "Verteidigt den Nexus!", font, subSize, new Color(0.96f, 0.93f, 0.86f), Vector2.zero, new Vector2(1f, 0.42f), FontStyles.Italic);

            // Button „Überspringen ▸▸“ unten rechts (im unteren Balken)
            var btnGo = new GameObject("SkipButton", typeof(RectTransform));
            _skipRt = (RectTransform)btnGo.transform;
            _skipRt.SetParent(root, false);
            _skipRt.anchorMin = _skipRt.anchorMax = _skipRt.pivot = new Vector2(1f, 0f);
            _skipRt.sizeDelta = touch ? new Vector2(330f, 88f) : new Vector2(290f, 72f);
            var bg = btnGo.AddComponent<Image>();
            bg.color = new Color(0.12f, 0.1f, 0.08f, 0.82f);
            var btn = btnGo.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.25f, 1.2f, 1.1f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            btn.colors = colors;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            btn.onClick.AddListener(Skip);
            _skipGroup = btnGo.AddComponent<CanvasGroup>();
            _skipGroup.alpha = 0f;
            var label = MakeText(_skipRt, "Label", SkipLabel(font), font, touch ? 38f : 32f, new Color(1f, 0.92f, 0.75f));
            Stretch(label.rectTransform, Vector2.zero, Vector2.one);
            label.alignment = TextAlignmentOptions.Center;

            _canvas.gameObject.SetActive(false);
        }

        // „▸▸“, falls die HUD-Schrift das Zeichen hat (sonst »)
        private static string SkipLabel(TMP_FontAsset font)
        {
            bool has = font != null && font.HasCharacter('▸', true, false);
            return has ? "Überspringen ▸▸" : "Überspringen »";
        }

        private static Image MakeImage(RectTransform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static TextMeshProUGUI MakeText(RectTransform parent, string name, string text, TMP_FontAsset font, float size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        // Text mit weichem Schatten als zweite Textkopie (kein Outline → keine Material-Instanz, Schrift bleibt unverändert)
        private static void MakeShadowedText(RectTransform parent, string name, string text, TMP_FontAsset font, float size, Color color,
            Vector2 anchorMin, Vector2 anchorMax, FontStyles style)
        {
            var shadow = MakeText(parent, name + "Shadow", text, font, size, new Color(0f, 0f, 0f, 0.65f));
            Stretch(shadow.rectTransform, anchorMin, anchorMax);
            shadow.rectTransform.anchoredPosition = new Vector2(4f, -4f);
            shadow.alignment = TextAlignmentOptions.Center;
            shadow.fontStyle = style;
            var main = MakeText(parent, name, text, font, size, color);
            Stretch(main.rectTransform, anchorMin, anchorMax);
            main.alignment = TextAlignmentOptions.Center;
            main.fontStyle = style;
        }

        private static void Stretch(RectTransform rt, Vector2 min, Vector2 max)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        // ---------------- HUD ausblenden ----------------

        // Alle Bildschirm-Canvases (HUD, Gruppenliste, Tipps, Minikarte …) außer dem eigenen und der Touch-Steuerung
        // (die blendet sich über LocalInputBlocked selbst aus). Weltraum-Canvases (Lebensleisten) bleiben – sie gehören
        // ins Bild. Später gebaute Canvases (z. B. Toast zum Wellenstart) werden alle 0,5 s nachgezogen.
        private void HideCanvases(float hide)
        {
            if (Time.unscaledTime >= _nextCanvasScan)
            {
                _nextCanvasScan = Time.unscaledTime + 0.5f;
                ScanCanvases();
            }
            for (int i = _hidden.Count - 1; i >= 0; i--)
            {
                var h = _hidden[i];
                if (h.Canvas == null)
                {
                    _hidden.RemoveAt(i);
                    continue;
                }
                if (h.OwnGroup)
                {
                    if (h.Group == null) { _hidden.RemoveAt(i); continue; }
                    h.Group.alpha = 1f - hide;
                    h.Group.blocksRaycasts = hide < 0.5f;
                }
                else
                {
                    bool off = hide >= 0.5f;
                    if (off && h.Canvas.enabled)
                    {
                        h.Canvas.enabled = false;
                        h.DisabledByUs = true;
                    }
                    else if (!off && h.DisabledByUs)
                    {
                        h.Canvas.enabled = true;
                        h.DisabledByUs = false;
                    }
                }
            }
        }

        private void ScanCanvases()
        {
            var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var c in canvases)
            {
                if (c == null || !c.isRootCanvas || c == _canvas || c.renderMode == RenderMode.WorldSpace) continue;
                if (c.gameObject.name == "TouchControlsCanvas") continue;
                if (!c.enabled) continue;
                bool known = false;
                for (int i = 0; i < _hidden.Count; i++) if (_hidden[i].Canvas == c) { known = true; break; }
                if (known) continue;
                var group = c.GetComponent<CanvasGroup>();
                var h = new HiddenCanvas { Canvas = c };
                if (group == null || OwnGroups.Contains(group))
                {
                    // Eigene Gruppe (bleibt nach dem Film mit Alpha 1 liegen, wird beim nächsten Mal wiederverwendet)
                    if (group == null)
                    {
                        group = c.gameObject.AddComponent<CanvasGroup>();
                        OwnGroups.Add(group);
                    }
                    h.Group = group;
                    h.OwnGroup = true;
                }
                _hidden.Add(h);
            }
        }

        private void RestoreCanvases()
        {
            foreach (var h in _hidden)
            {
                if (h.Canvas == null) continue;
                if (h.OwnGroup && h.Group != null)
                {
                    h.Group.alpha = 1f;
                    h.Group.blocksRaycasts = true;
                }
                if (h.DisabledByUs) h.Canvas.enabled = true;
            }
            _hidden.Clear();
        }
    }
}
