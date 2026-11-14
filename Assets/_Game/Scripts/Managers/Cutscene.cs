using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ElementalBuddies
{
    // Ein Film für den Cutscene-Dienst: Der Film liefert nur Zeiten, Kamera-Lage und Texte – Kamera-Übergabe,
    // Rückblende, Kinobalken, Titel, „Überspringen“, HUD, Musik und Eingabesperre macht der Dienst.
    // Zeitachse (s, Echtzeit): 0 … HoldUntil steht die Spielkamera (Balken fahren ein), danach gilt Pose(t, dt) bis
    // Duration, dann gleitet die Kamera in ReturnDuration zur Spielkamera zurück (nach „Überspringen“ in
    // SkipReturnDuration).
    public sealed class CutsceneFilm
    {
        public string Name = "Film";
        public float Duration = 4f;              // Ende der letzten Einstellung = Beginn der Rückblende
        public float HoldUntil;                  // so lange bleibt die Kamera auf der Start-Lage (0 = Pose gilt sofort)
        public float BarsIn = 0.5f;              // Kinobalken fahren in dieser Zeit ein
        public float ReturnDuration = 1f;
        public float SkipReturnDuration = 0.4f;
        public float ReturnArc = 2f;             // Anheben in der Mitte der Rückblende (m)
        public float SkipReturnArc = 0.5f;
        public float MusicGain = 0.45f;          // Musiklautstärke während des Films

        // Kamera-Lage zur Filmzeit t; dt = verstrichene Filmzeit dieses Bilds (0 bei Standbild/Pause). Pose.Fov = 0
        // lässt den Blickwinkel der Spielkamera unverändert.
        public Func<float, float, CutscenePose> Pose;

        // Gefilmter Punkt (OcclusionFader blendet aus, was ihn verdeckt), gültig ab FocusFrom. null = keiner.
        public Func<float, Vector3> Focus;
        public float FocusFrom;

        // Schwarzblende 0..1 über der Filmzeit (z. B. harter Schnitt von der Spielkamera). null = keine.
        public Func<float, float> BlackFade;

        // Titel + Untertitel, sichtbar von TitleIn bis TitleOut (leer = kein Titel)
        public string Title, Subtitle;
        public float TitleIn, TitleOut;
        public float TitleScale = 1f;                               // Schriftgröße relativ zum Intro-Titel
        public Vector2 TitleBand = new Vector2(0.6f, 0.84f);        // Höhe des Titelblocks im Bild (Anteil, unten/oben)

        // Einmalige Ereignisse zu einer Filmzeit (Töne, Effekte); nach „Überspringen“ fallen spätere aus
        public readonly List<CutsceneCue> Cues = new List<CutsceneCue>();

        // Film zu Ende (true = übersprungen oder abgebrochen)
        public Action<bool> Finished;

        public CutsceneFilm AddCue(float time, Action action)
        {
            if (action != null) Cues.Add(new CutsceneCue { Time = time, Action = action });
            return this;
        }
    }

    public struct CutsceneCue
    {
        public float Time;
        public Action Action;
    }

    // Einstellungen für Cutscene.PlayFocusShot: von der Spielkamera zu einem Weltpunkt fliegen, ihn ansehen, zurück.
    public sealed class FocusShotOptions
    {
        public string Name = "FocusShot";
        // Kamera relativ zum Punkt bei der Ankunft. OffsetRelativeToCamera: um den Gierwinkel der Spielkamera gedreht
        // (−Z = „aus Sicht des Spielers vor dem Punkt“), sonst Weltachsen.
        public Vector3 Offset = new Vector3(0f, 8f, -11f);
        public bool OffsetRelativeToCamera = true;
        public float LookHeight = 1.5f;          // Blickpunkt so weit über dem Punkt
        public float SweepDegrees;               // langsamer Bogen um den Punkt während des Haltens (0 = Standbild)
        public float BarsIn = 0.5f;
        public float FlyEnd = 1.6f;              // Ankunft (s)
        public float HoldEnd = 3.2f;             // Ende des Haltens = Beginn der Rückblende (s)
        public float ReturnDuration = 0.8f;
        public float Apex = -1f;                 // zusätzliche Flughöhe in der Flugmitte (m); < 0 = aus der Entfernung
        public float Fov;                        // Blickwinkel beim Halten (0 = unverändert)
        public bool AvoidObstacles = true;       // Winkel/Abstand per Physik-Prüfung so wählen, dass nichts die Sicht verdeckt
        public Transform IgnoreRoot;             // Collider darunter zählen nicht als Hindernis (das gefilmte Objekt)

        public string Title, Subtitle;
        public float TitleIn = -1f;              // < 0 = kurz vor der Ankunft
        public float TitleOut = -1f;             // < 0 = bis zum Ende des Haltens
        public float TitleScale = 0.72f;
        public Vector2 TitleBand = new Vector2(0.6f, 0.84f);

        public SfxId? ArriveSfx;                 // Ton (2D, GameAudio.PlayOr) …
        public float ArriveSfxTime = -1f;        // … zu dieser Filmzeit (< 0 = 0,4 s vor der Ankunft)
        public Action OnArrive;                  // bei FlyEnd
        public Action<bool> Finished;            // Film zu Ende (true = übersprungen)
    }

    // Gemeinsamer Film-Dienst (wiki: Design/Intro-Cutscene, Design/Dimensionsriss-Plan): spielt genau einen Film
    // (CutsceneFilm) zur Zeit, lokal auf diesem Rechner und live – das Spiel läuft weiter, gesperrt ist nur die eigene
    // Eingabe (Cutscene.IsPlaying → InteractionManager.LocalInputBlocked, PlayerController, PlayerAbilities).
    // Der Dienst übernimmt: Kamera (CameraFollow.Suspended, schreibt die Kamera im LateUpdate, Rückblende auf
    // CameraFollow.TryGetGamePose), Kinobalken, Schwarzblende, Titel/Untertitel, Button „Überspringen“ + Esc/Leertaste/
    // Klick/Tippen, HUD ausblenden und wiederherstellen, Echtzeit-Uhr (0,1 s je Bild gedeckelt, steht im Pause-Menü),
    // Musik-Dämpfung (MusicGain), Fokuspunkt für den OcclusionFader, Test-Haken (SeekForTest, TestSpeed, Skip).
    // Filme: IntroCutscene (Welle 1), ShrineCutscene (Schrein erwacht), PlayFocusShot (Blick-Fahrt auf einen Punkt).
    // Das Objekt legt sich in jeder Spielszene (Szene mit WaveManager) selbst an; Prefab/Szene bleiben unverändert.
    [DefaultExecutionOrder(-45)] // nach CameraFollow (−50), vor Billboards/OcclusionFader
    public class Cutscene : MonoBehaviour
    {
        public const string PrefEnabled = "IntroCutsceneEnabled";
        private const int SortingOrder = 900; // über allem (Tipps 75, Popups …)
        private const float SkipGrace = 0.4f; // so lange nach dem Start zählt keine Überspringen-Eingabe (Klick auf „Welle starten“)
        private const float BarShare = 0.115f; // Höhe eines Kinobalkens (Anteil der Bildhöhe)

        // ---------------- Öffentlicher Zustand ----------------

        // Schalter „Intro-Film zeigen“ (Pause-Menü und Hauptmenü), Standard an – gilt für alle automatischen Filme
        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(PrefEnabled, 1) != 0;
            set { PlayerPrefs.SetInt(PrefEnabled, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        // Ein Film läuft (inkl. Einblendung und Rückblende): lokale Eingabe gesperrt
        public static bool IsPlaying { get; private set; }

        // Laufender Film (null, wenn keiner läuft), seine Filmzeit und ob schon die Rückblende läuft
        public static CutsceneFilm Current => IsPlaying && _instance != null ? _instance._film : null;
        public static float FilmTime => _instance != null && IsPlaying ? _instance._t : 0f;
        public static bool IsReturning => _instance != null && IsPlaying && _instance._returning;

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
            if (c == null || !IsPlaying || c._film == null || c._film.Focus == null || c._returning) return false;
            if (c._t < Mathf.Max(c._film.HoldUntil, c._film.FocusFrom)) return false;
            focus = c._film.Focus(c._t);
            return true;
        }

        // Darf jetzt ein automatischer Film starten? Nicht bei Spielende (oder offenem Weiterspielen-Angebot) und nicht, wenn lokal ein Fenster offen ist
        // (Pause-Menü, Tastenbelegung, Kartenwahl, Händler) – das Fenster hat Vorrang. Der Schalter (Enabled) und
        // „läuft schon ein Film“ werden getrennt geprüft.
        public static bool CanStartNow
        {
            get
            {
                if (GameOverPending) return false;
                if (PauseManager.IsPaused || KeyBindingsMenu.IsOpen) return false;
                if (UpgradeManager.Instance != null && UpgradeManager.Instance.IsChoosing) return false;
                if (MerchantManager.Instance != null && MerchantManager.Instance.IsChoosing) return false;
                return true;
            }
        }

        // Spielende oder das Angebot „Video: Weiterspielen“ steht im Bild (Nexus gefallen)
        private static bool GameOverPending =>
            GameManager.Instance != null && (GameManager.Instance.IsGameOver || GameManager.Instance.IsContinueOffered);

        // Lage der Spielkamera in diesem Moment (Start-Lage eines Films); false ohne Kamera
        public static bool TryGetCameraPose(out CutscenePose pose)
        {
            pose = default;
            var cam = CameraFollow.CameraTransform;
            if (cam == null && Camera.main != null) cam = Camera.main.transform;
            if (cam == null) return false;
            pose = new CutscenePose(cam.position, cam.rotation);
            var camera = cam.GetComponent<Camera>();
            pose.Fov = camera != null ? camera.fieldOfView : 0f;
            return true;
        }

        // Film starten. Läuft schon einer, wird die Anfrage verworfen (false) – außer force (Tests): dann bricht der
        // laufende Film ab. false auch ohne Spielszene/Kamera oder ohne Pose-Funktion.
        public static bool Play(CutsceneFilm film, bool force = false)
        {
            if (film == null || film.Pose == null) return false;
            if (IsPlaying && !force) return false;
            EnsureInstance();
            if (_instance == null) return false;
            return _instance.Begin(film);
        }

        // Blick-Fahrt: von der Spielkamera zu target fliegen, den Punkt aus opts.Offset ansehen (optional mit Bogen),
        // halten und zurückgleiten; mit Titel/Untertitel und Ton. Gleiche Regeln wie Play (false, wenn ein Film läuft).
        // Der Aufrufer prüft selbst Enabled/CanStartNow, wenn die Fahrt automatisch ausgelöst wird.
        public static bool PlayFocusShot(Vector3 target, FocusShotOptions opts = null, bool force = false)
        {
            if (IsPlaying && !force) return false;
            if (!TryGetCameraPose(out CutscenePose start)) return false;
            opts = opts ?? new FocusShotOptions();
            Vector3 offset = opts.OffsetRelativeToCamera
                ? Quaternion.Euler(0f, start.Rotation.eulerAngles.y, 0f) * opts.Offset
                : opts.Offset;
            var path = FocusShotPath.FromOffset(start, target, offset);
            path.FlyEnd = opts.FlyEnd;
            path.HoldEnd = Mathf.Max(opts.FlyEnd + 0.1f, opts.HoldEnd);
            path.ReturnDuration = opts.ReturnDuration;
            path.LookHeight = opts.LookHeight;
            path.Sweep = opts.SweepDegrees;
            path.StartAngle -= opts.SweepDegrees * 0.5f; // Bogen liegt mittig um den gewünschten Versatz
            path.Apex = opts.Apex >= 0f ? opts.Apex : ShrineCutscenePath.ApexFor(start.Position, target);
            path.Fov = opts.Fov;
            if (opts.AvoidObstacles) ChooseFraming(path, opts.IgnoreRoot);
            return Play(BuildFocusFilm(path, opts), force);
        }

        // Film aus einer fertigen Blick-Fahrt (Bahn) bauen – für Filme, die die Bahn selbst wählen (ShrineCutscene)
        public static CutsceneFilm BuildFocusFilm(FocusShotPath path, FocusShotOptions opts)
        {
            opts = opts ?? new FocusShotOptions();
            var film = new CutsceneFilm
            {
                Name = opts.Name,
                Duration = path.HoldEnd,
                HoldUntil = 0f,
                BarsIn = opts.BarsIn,
                ReturnDuration = path.ReturnDuration,
                ReturnArc = FocusShotPath.ReturnArc,
                Pose = (t, dt) => path.Evaluate(t),
                Focus = t => path.LookPoint,
                FocusFrom = path.FlyEnd * 0.45f,
                Title = opts.Title,
                Subtitle = opts.Subtitle,
                TitleIn = opts.TitleIn >= 0f ? opts.TitleIn : Mathf.Max(0f, path.FlyEnd - 0.1f),
                TitleOut = opts.TitleOut >= 0f ? opts.TitleOut : path.HoldEnd,
                TitleScale = opts.TitleScale,
                TitleBand = opts.TitleBand,
                Finished = opts.Finished,
            };
            if (opts.ArriveSfx.HasValue)
            {
                SfxId id = opts.ArriveSfx.Value;
                // 2D (ohne Position): der AudioListener sitzt nicht unbedingt an der Filmkamera
                film.AddCue(opts.ArriveSfxTime >= 0f ? opts.ArriveSfxTime : Mathf.Max(0f, path.FlyEnd - 0.4f), () => GameAudio.PlayOr(id));
            }
            film.AddCue(path.FlyEnd, opts.OnArrive);
            return film;
        }

        // Editor/Tests: Filmzeit setzen und Abspieltempo (0 = Standbild für Screenshots)
        public static void SeekForTest(float t)
        {
            var c = _instance;
            if (c != null && IsPlaying && !c._returning && c._film != null) c._t = Mathf.Clamp(t, 0f, c._film.Duration);
        }

        public static float TestSpeed { get; set; } = 1f;

        // Laufenden Film überspringen (Button, Esc/Leertaste/Klick/Tippen)
        public static void Skip()
        {
            if (_instance != null) _instance.SkipInternal();
        }

        // ---------------- Bild-Wahl für Blick-Fahrten ----------------

        // Ebenen, die als festes Hindernis zählen (ohne Gegner, Buddies, Spieler, UI)
        public static int GroundMask
        {
            get
            {
                if (_groundMask != 0) return _groundMask;
                int mask = Physics.DefaultRaycastLayers;
                foreach (var layer in new[] { "Enemy", "Buddy", "Player", "Ignore Raycast", "UI" })
                {
                    int l = LayerMask.NameToLayer(layer);
                    if (l >= 0) mask &= ~(1 << l);
                }
                return _groundMask = mask;
            }
        }

        private static int _groundMask;
        private static readonly RaycastHit[] FramingHits = new RaycastHit[16];
        private static readonly float[] FramingAngles = { 0f, 40f, -40f, 80f, -80f, 130f, -130f, 180f };
        private static readonly float[] FramingScales = { 1f, 1.25f, 0.8f };

        // Winkel um den Punkt, Abstand/Höhe und Drehsinn des Bogens so wählen, dass die Kamera nicht in Gebäuden,
        // Baumkronen oder im Hang steckt und die Sicht auf den Punkt frei ist. Bevorzugt die übergebene Bahn; weicht
        // erst seitlich aus, dann mit größerem/kleinerem Abstand. ignoreRoot: das gefilmte Objekt.
        public static void ChooseFraming(FocusShotPath path, Transform ignoreRoot = null)
        {
            if (path == null) return;
            float mid = path.StartAngle + path.Sweep * 0.5f;
            float sweep = Mathf.Abs(path.Sweep), sign0 = path.Sweep < 0f ? -1f : 1f;
            float radius = path.Radius, height = path.Height;
            float bestScore = float.MaxValue, bestMid = mid, bestSign = sign0, bestScale = 1f;
            for (int s = 0; s < FramingScales.Length; s++)
            {
                for (int a = 0; a < FramingAngles.Length; a++)
                {
                    for (int d = 0; d < (sweep > 0.01f ? 2 : 1); d++)
                    {
                        float sign = d == 0 ? sign0 : -sign0;
                        float score = s * 1.2f + a * 0.4f + d * 0.1f;
                        if (score >= bestScore) continue;
                        Apply(path, mid + FramingAngles[a], sign, sweep, radius * FramingScales[s], height * FramingScales[s]);
                        score += FramingPenalty(path, ignoreRoot);
                        if (score < bestScore)
                        {
                            bestScore = score;
                            bestMid = mid + FramingAngles[a];
                            bestSign = sign;
                            bestScale = FramingScales[s];
                        }
                    }
                }
            }
            Apply(path, bestMid, bestSign, sweep, radius * bestScale, height * bestScale);
        }

        private static void Apply(FocusShotPath path, float mid, float sign, float sweep, float radius, float height)
        {
            path.Radius = radius;
            path.Height = height;
            path.Sweep = sign * sweep;
            path.StartAngle = mid - sign * sweep * 0.5f;
        }

        private static bool Ignored(Collider col, Transform ignoreRoot)
        {
            return col == null || (ignoreRoot != null && col.transform.IsChildOf(ignoreRoot));
        }

        private static float FramingPenalty(FocusShotPath path, Transform ignoreRoot)
        {
            float penalty = 0f;
            int mask = GroundMask;
            Vector3 look = path.LookPoint;
            const int samples = 5;
            for (int i = 0; i < samples; i++)
            {
                float t = Mathf.Lerp(path.FlyEnd, path.HoldEnd, i / (float)(samples - 1));
                Vector3 p = path.OrbitPosition(t);
                // Kamera steckt in Geometrie
                if (Physics.CheckSphere(p, 1f, mask, QueryTriggerInteraction.Ignore)) penalty += 10f;
                // Kamera unter Gelände/Dach (Strahl von oben trifft über ihr etwas Festes)
                else if (Physics.Raycast(p + Vector3.up * 40f, Vector3.down, out RaycastHit top, 39f, mask, QueryTriggerInteraction.Ignore)
                    && !Ignored(top.collider, ignoreRoot) && top.collider.GetComponentInParent<FadeOccluder>() == null) penalty += 8f;
                // Sichtlinie zum Punkt: nur Treffer an Nicht-FadeOccluder-Objekten zählen (die blendet der Fader aus)
                if (Physics.Linecast(p, look, out RaycastHit hit, mask, QueryTriggerInteraction.Ignore)
                    && !Ignored(hit.collider, ignoreRoot) && hit.collider.GetComponentInParent<FadeOccluder>() == null) penalty += 4f;
                // Vor der Linse: dicker Strahl in Blickrichtung (Kronen-Trigger der FadeOccluder zählen mit)
                Vector3 dir = look - p;
                float dist = dir.magnitude;
                if (dist < 0.5f) continue;
                int n = Physics.SphereCastNonAlloc(p, 2f, dir / dist, FramingHits, Mathf.Min(8f, dist * 0.6f), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
                for (int h = 0; h < n; h++)
                {
                    var col = FramingHits[h].collider;
                    if (Ignored(col, ignoreRoot)) continue;
                    var occ = col.GetComponentInParent<FadeOccluder>();
                    if (col.isTrigger && (occ == null || !occ.TriggerVolumes)) continue;
                    if (!col.isTrigger && (mask & (1 << col.gameObject.layer)) == 0) continue;
                    penalty += 2f;
                    break;
                }
            }
            return penalty;
        }

        // ---------------- Selbst-Anlage ----------------

        private static Cutscene _instance;
        private static int _lastPlayingFrame = -10;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
            IsPlaying = false;
            MusicGain = 1f;
            TestSpeed = 1f;
            _lastPlayingFrame = -10;
            _groundMask = 0;
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

        // Nur in Spielszenen (WaveManager vorhanden); das Objekt gehört zur Szene des WaveManagers und stirbt mit ihr.
        // Die automatischen Filme (Intro, Schrein) hängen als Komponenten am selben Objekt.
        internal static void EnsureInstance()
        {
            if (_instance != null) return;
            var waves = WaveManager.Instance;
            if (waves == null) return;
            var go = new GameObject("Cutscene");
            if (waves.gameObject.scene.IsValid()) SceneManager.MoveGameObjectToScene(go, waves.gameObject.scene);
            _instance = go.AddComponent<Cutscene>();
            go.AddComponent<IntroCutscene>();
            go.AddComponent<ShrineCutscene>();
            go.AddComponent<DimensionEvent>(); // Dimensionsriss: braucht denselben Lebenszyklus (je Spielszene, auf jedem Rechner)
        }

        // ---------------- Laufzeit ----------------

        private CutsceneFilm _film;
        private float _t;
        private bool _returning, _skipped;
        private float _returnStart = float.PositiveInfinity, _returnDuration = 1f, _returnArc;
        private CutscenePose _startPose, _returnFrom;
        private Transform _cam;
        private Camera _camera;
        private float _baseFov, _lastFov;
        private bool _fovTouched;
        private int _nextCue;
        private float _blackAtSkip;

        // UI
        private Canvas _canvas;
        private RectTransform _topBar, _bottomBar, _titleRt;
        private Image _black;
        private CanvasGroup _titleGroup, _skipGroup;
        private RectTransform _skipRt;
        private TextMeshProUGUI _titleText, _titleShadow, _subText, _subShadow;
        private float _titleSize, _subSize;
        private TMP_FontAsset _font;
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
                Destroy(this);
                return;
            }
            _instance = this;
        }

        void OnDestroy()
        {
            if (_instance != this) return;
            if (IsPlaying) Finish(true);
            _instance = null;
        }

        void Update()
        {
            if (!IsPlaying || _returning) return;
            // Nexus fällt während des Films: sofort zurück, das Spielende-Fenster liegt sonst unter dem ausgeblendeten HUD
            if (GameOverPending) SkipInternal();
            else if (_t >= SkipGrace && !PauseManager.IsPaused && SkipPressed()) SkipInternal();
        }

        private bool Begin(CutsceneFilm film)
        {
            if (IsPlaying) Finish(true);
            _cam = CameraFollow.CameraTransform;
            if (_cam == null && Camera.main != null) _cam = Camera.main.transform;
            if (_cam == null) return false;
            _camera = _cam.GetComponent<Camera>();
            _baseFov = _lastFov = _camera != null ? _camera.fieldOfView : 0f;
            _fovTouched = false;

            _film = film;
            film.Cues.Sort((a, b) => a.Time.CompareTo(b.Time));
            _nextCue = 0;
            _startPose = new CutscenePose(_cam.position, _cam.rotation);
            _t = 0f;
            _returning = false;
            _skipped = false;
            _returnStart = float.PositiveInfinity;
            _returnDuration = film.ReturnDuration;
            _returnArc = film.ReturnArc;
            _blackAtSkip = 0f;
            IsPlaying = true;
            _lastPlayingFrame = Time.frameCount;
            CameraFollow.Suspended = true;

            BuildUi();
            ApplyTitle(film);
            _canvas.gameObject.SetActive(true);
            _hidden.Clear();
            _nextCanvasScan = 0f;
            ApplyUi();
            return true;
        }

        void LateUpdate()
        {
            if (!IsPlaying) return;
            if (_cam == null || _film == null)
            {
                Finish(true);
                return;
            }
            _lastPlayingFrame = Time.frameCount;
            // Echtzeit (Treffer-Stopp/Zeitlupe bremsen den Film nicht), Ruckler gedeckelt: ein langes Bild überspringt
            // keine halbe Einstellung
            float dt = PauseManager.IsPaused ? 0f : Mathf.Min(Time.unscaledDeltaTime, 0.1f) * TestSpeed;
            _t += dt;

            // Töne/Ereignisse (nicht mehr nach „Überspringen“)
            while (!_returning && _nextCue < _film.Cues.Count && _t >= _film.Cues[_nextCue].Time)
            {
                var cue = _film.Cues[_nextCue++];
                try { cue.Action?.Invoke(); }
                catch (Exception e) { Debug.LogException(e); }
            }

            // Natürliches Ende der letzten Einstellung → Rückblende
            if (!_returning && _t >= _film.Duration)
            {
                _returning = true;
                _returnStart = _film.Duration;
                _returnDuration = _film.ReturnDuration;
                _returnArc = _film.ReturnArc;
                _returnFrom = _film.Pose(_film.Duration, dt);
                _returnFrom.Fov = _lastFov;
            }

            CutscenePose pose;
            if (_returning)
            {
                float k = (_t - _returnStart) / Mathf.Max(0.01f, _returnDuration);
                CutscenePose game = CameraFollow.TryGetGamePose(out Vector3 gp, out Quaternion gr) ? new CutscenePose(gp, gr) : _startPose;
                pose = IntroCutscenePath.Blend(_returnFrom, game, k, _returnArc);
                _cam.SetPositionAndRotation(pose.Position, pose.Rotation);
                if (_fovTouched) SetFov(Mathf.Lerp(_returnFrom.Fov, _baseFov, IntroCutscenePath.Smooth01(k)));
                if (k >= 1f)
                {
                    Finish(_skipped);
                    return;
                }
            }
            else
            {
                pose = _t < _film.HoldUntil ? _startPose : _film.Pose(_t, dt);
                _cam.SetPositionAndRotation(pose.Position, pose.Rotation);
                if (pose.Fov > 0f && _t >= _film.HoldUntil)
                {
                    _fovTouched = true;
                    SetFov(pose.Fov);
                }
                else if (_fovTouched) SetFov(_baseFov);
            }
            ApplyUi();
        }

        private void SetFov(float fov)
        {
            _lastFov = fov;
            if (_camera != null && fov > 1f) _camera.fieldOfView = fov;
        }

        private void SkipInternal()
        {
            if (!IsPlaying || _returning || _film == null) return;
            _returning = true;
            _skipped = true;
            _returnStart = _t;
            _returnDuration = _film.SkipReturnDuration;
            _returnArc = _film.SkipReturnArc;
            _returnFrom = _cam != null ? new CutscenePose(_cam.position, _cam.rotation) : _startPose;
            _returnFrom.Fov = _lastFov;
            _blackAtSkip = _black != null ? _black.color.a : 0f;
        }

        private void Finish(bool aborted)
        {
            var film = _film;
            IsPlaying = false;
            _returning = false;
            MusicGain = 1f;
            CameraFollow.Suspended = false;
            if (_fovTouched && _camera != null && _baseFov > 1f) _camera.fieldOfView = _baseFov;
            _fovTouched = false;
            RestoreCanvases();
            if (_canvas != null) _canvas.gameObject.SetActive(false);
            _film = null;
            if (film != null && film.Finished != null)
            {
                try { film.Finished(aborted); }
                catch (Exception e) { Debug.LogException(e); }
            }
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

        // ---------------- UI ----------------

        private void ApplyUi()
        {
            float bars = CutsceneTiming.Bars(_t, _film.BarsIn, _returnStart, _returnDuration);
            float hide = bars;
            MusicGain = Mathf.Lerp(1f, _film.MusicGain, bars);

            if (_topBar != null)
            {
                _topBar.anchorMin = new Vector2(0f, 1f - BarShare * bars);
                _bottomBar.anchorMax = new Vector2(1f, BarShare * bars);
            }
            if (_black != null)
            {
                float a;
                if (_returning) a = _blackAtSkip * (1f - Mathf.Clamp01((_t - _returnStart) / 0.2f));
                else a = _film.BlackFade != null ? Mathf.Clamp01(_film.BlackFade(_t)) : 0f;
                _black.color = new Color(0f, 0f, 0f, a);
            }
            if (_titleGroup != null)
            {
                float title = string.IsNullOrEmpty(_film.Title) && string.IsNullOrEmpty(_film.Subtitle)
                    ? 0f
                    : CutsceneTiming.TitleAlpha(_t, _film.TitleIn, _film.TitleOut);
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

        // Titel/Untertitel des Films in die (gemeinsamen) Textfelder schreiben
        private void ApplyTitle(CutsceneFilm film)
        {
            if (_titleRt == null) return;
            _titleRt.anchorMin = new Vector2(0.1f, film.TitleBand.x);
            _titleRt.anchorMax = new Vector2(0.9f, film.TitleBand.y);
            _titleRt.offsetMin = _titleRt.offsetMax = Vector2.zero;
            string title = Printable(film.Title), sub = Printable(film.Subtitle);
            float scale = Mathf.Max(0.2f, film.TitleScale);
            SetText(_titleText, title, _titleSize * scale);
            SetText(_titleShadow, title, _titleSize * scale);
            SetText(_subText, sub, _subSize * Mathf.Lerp(1f, scale, 0.5f));
            SetText(_subShadow, sub, _subSize * Mathf.Lerp(1f, scale, 0.5f));
            _titleGroup.alpha = 0f;
        }

        // Lange Titel schrumpfen, statt über den Bildrand zu laufen (kurze behalten ihre Größe)
        private static void SetText(TextMeshProUGUI text, string value, float size)
        {
            if (text == null) return;
            text.enableAutoSizing = true;
            text.fontSizeMax = size;
            text.fontSizeMin = size * 0.5f;
            text.fontSize = size;
            text.text = value;
        }

        // Zeichen, die die HUD-Schrift nicht hat, ersetzen (Halbgeviertstrich → Bindestrich)
        private string Printable(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOf('–') >= 0 && (_font == null || !_font.HasCharacter('–', true, false))) s = s.Replace('–', '-');
            return s;
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

            var go = new GameObject("CutsceneCanvas", typeof(RectTransform));
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
            _font = font;

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

            // Titel + Untertitel (Standard: oberes Drittel; Texte und Lage setzt ApplyTitle je Film)
            var titleGo = new GameObject("Title", typeof(RectTransform));
            _titleRt = (RectTransform)titleGo.transform;
            _titleRt.SetParent(root, false);
            _titleRt.anchorMin = new Vector2(0.1f, 0.6f);
            _titleRt.anchorMax = new Vector2(0.9f, 0.84f);
            _titleRt.offsetMin = _titleRt.offsetMax = Vector2.zero;
            _titleGroup = titleGo.AddComponent<CanvasGroup>();
            _titleGroup.alpha = 0f;
            _titleGroup.blocksRaycasts = false;
            _titleGroup.interactable = false;
            _titleSize = touch ? 112f : 104f;
            _subSize = touch ? 50f : 44f;
            MakeShadowedText(_titleRt, "Wave", "", font, _titleSize, new Color(1f, 0.88f, 0.6f), new Vector2(0f, 0.42f), Vector2.one, FontStyles.Bold,
                out _titleText, out _titleShadow);
            MakeShadowedText(_titleRt, "Subtitle", "", font, _subSize, new Color(0.96f, 0.93f, 0.86f), Vector2.zero, new Vector2(1f, 0.42f), FontStyles.Italic,
                out _subText, out _subShadow);

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
            Vector2 anchorMin, Vector2 anchorMax, FontStyles style, out TextMeshProUGUI main, out TextMeshProUGUI shadow)
        {
            shadow = MakeText(parent, name + "Shadow", text, font, size, new Color(0f, 0f, 0f, 0.65f));
            Stretch(shadow.rectTransform, anchorMin, anchorMax);
            shadow.rectTransform.anchoredPosition = new Vector2(4f, -4f);
            shadow.alignment = TextAlignmentOptions.Center;
            shadow.fontStyle = style;
            main = MakeText(parent, name, text, font, size, color);
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
