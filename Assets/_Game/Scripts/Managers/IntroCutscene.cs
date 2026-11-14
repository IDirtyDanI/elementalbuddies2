using UnityEngine;
using UnityEngine.AI;

namespace ElementalBuddies
{
    // Intro-Film zum Start von Welle 1 (wiki: Design/Intro-Cutscene). Läuft auf jedem Rechner lokal und live: Die Welle
    // läuft weiter, die echten Gegner werden gefilmt, gesperrt ist nur die eigene Eingabe (Cutscene.IsPlaying →
    // InteractionManager.LocalInputBlocked, PlayerController, PlayerAbilities). Keine Netz-RPCs; im Koop überspringt
    // jeder für sich, wer nach dem Start von Welle 1 beitritt, sieht keinen Film (OnWaveStart feuert nur live).
    // Ablauf: Auslöser WaveManager.OnWaveStart bei CurrentWaveIndex 0 (Server und Clients). Kamerabahn aus dem NavMesh-
    // Pfad vom ersten offenen Portal zum Nexus (IntroCutscenePath, reine Mathematik).
    // Kamera-Übergabe, Rückblende, Kinobalken, Titel, „Überspringen“, HUD und Musik macht der gemeinsame Cutscene-
    // Dienst (Cutscene.cs); hier stehen nur Auslöser, Bahn und Töne dieses Films. Die Komponente hängt am Cutscene-
    // Objekt, das sich in jeder Spielszene selbst anlegt.
    // Test im Editor (Play Mode): IntroCutscene.PlayForTest().
    public class IntroCutscene : MonoBehaviour
    {
        public const string PrefEnabled = Cutscene.PrefEnabled;
        public const string FilmName = "Intro";

        // ---------------- Öffentlicher Zustand ----------------

        // Schalter „Intro-Film zeigen“ (Pause-Menü und Hauptmenü), Standard an; gilt auch für den Schrein-Film
        public static bool Enabled
        {
            get => Cutscene.Enabled;
            set => Cutscene.Enabled = value;
        }

        // Der Intro-Film läuft (inkl. Einblendung und Rückblende). Für „irgendein Film läuft“: Cutscene.IsPlaying.
        public static bool IsPlaying => _instance != null && _instance._film != null && Cutscene.Current == _instance._film;

        // Editor/Tests: Film sofort starten (Play Mode, unabhängig von Welle und Schalter; bricht einen laufenden Film ab)
        public static void PlayForTest()
        {
            Cutscene.EnsureInstance();
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
            if (IsPlaying) Cutscene.SeekForTest(t);
        }

        public static float TestSpeed
        {
            get => Cutscene.TestSpeed;
            set => Cutscene.TestSpeed = value;
        }

        // Laufenden Film überspringen (Button, Esc/Leertaste/Klick/Tippen)
        public static void Skip() => Cutscene.Skip();

        private static IntroCutscene _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
        }

        // ---------------- Laufzeit ----------------

        private WaveManager _waves;
        private bool _played;           // dieser Run hat den Film schon gezeigt
        private CutsceneFilm _film;
        private IntroCutscenePath _path;
        private float _columnS = float.NaN, _columnVel;
        private float _lift, _liftVel;

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
            if (_waves != null) _waves.OnWaveStart -= HandleWaveStart;
            if (_instance == this) _instance = null;
        }

        void Update()
        {
            if (_waves == null && WaveManager.Instance != null)
            {
                _waves = WaveManager.Instance;
                _waves.OnWaveStart += HandleWaveStart;
            }
        }

        private void HandleWaveStart()
        {
            if (_played || Cutscene.IsPlaying || _waves == null) return;
            if (_waves.CurrentWaveIndex != 0) return; // nur Welle 1 (DevTools-Startwelle > 1: kein Film)
            _played = true;
            if (!Enabled) return;
            // Spielende oder lokal ein Fenster offen (Koop: Pause-Overlay, Kartenwahl) → kein Film, das Fenster hat Vorrang
            if (!Cutscene.CanStartNow) return;
            Begin(false);
        }

        private void Begin(bool test)
        {
            var nexus = Nexus.Instance;
            var portal = PickPortal();
            if (nexus == null || portal == null)
            {
                if (test) Debug.LogWarning("[IntroCutscene] Kein Nexus oder Portal gefunden – kein Film.");
                return;
            }

            Vector3 portalPos = portal.SpawnTransform.position;
            Vector3 nexusPos = nexus.transform.position;
            var path = new IntroCutscenePath(FindPath(portalPos, nexusPos), portalPos, nexusPos);
            // Spawnpunkt liegt vor dem Portalbogen: Kamera-Abstand ab dem Bogen rechnen
            path.PortalSetback = Mathf.Clamp(Vector3.Dot(portalPos - portal.transform.position, path.StartDir), 0f, 6f);
            ChooseNexusFraming(path);

            var film = new CutsceneFilm
            {
                Name = FilmName,
                Duration = IntroCutscenePath.Shot3End,
                HoldUntil = IntroCutscenePath.FadeInEnd,
                BarsIn = IntroCutscenePath.FadeInEnd,
                ReturnDuration = IntroCutscenePath.ReturnDuration,
                SkipReturnDuration = IntroCutscenePath.SkipReturnDuration,
                ReturnArc = 2f,
                SkipReturnArc = 0.5f,
                MusicGain = 0.45f,
                Pose = FilmPose,
                Focus = t => path.FocusAt(t),
                FocusFrom = IntroCutscenePath.FadeInEnd,
                BlackFade = IntroCutscenePath.CutFadeAt,
                // Titel im oberen Drittel – unten steht der Nexus im Bild
                Title = "Welle 1",
                Subtitle = "Verteidigt den Nexus!",
                TitleIn = IntroCutscenePath.TitleIn,
                TitleOut = IntroCutscenePath.TitleOut,
            };
            // Ton zu Beginn von Einstellung 1 (Portal flammt auf, Kriegshorn)
            film.AddCue(IntroCutscenePath.FadeInEnd, PlayIntroSounds);

            var oldPath = _path;
            var oldFilm = _film;
            _path = path;
            _film = film;
            _columnS = float.NaN;
            _columnVel = 0f;
            _lift = _liftVel = 0f;
            if (!Cutscene.Play(film, test))
            {
                _path = oldPath;
                _film = oldFilm;
                return;
            }
            if (test) Debug.Log($"[IntroCutscene] Film gestartet: Portal „{portal.DisplayName}“, Weglänge {path.Length:0} m{(path.UsedFallback ? " (Fallback: gerade Linie)" : "")}.");
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
            _groundMask = Cutscene.GroundMask;
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

        private int _groundMask;
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

        // Kriegshorn + Portal-Aufflammen (Klänge aus GameAudio)
        // 2D (ohne Position): der AudioListener sitzt nicht unbedingt an der Filmkamera. PlayOr: ohne zugewiesenen
        // Clip der Ersatzton aus GameAudio.Fallback.
        private static void PlayIntroSounds()
        {
            GameAudio.PlayOr(SfxId.PortalSurge);
            GameAudio.PlayOr(SfxId.IntroHorn);
        }
    }
}
