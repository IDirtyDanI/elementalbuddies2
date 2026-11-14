using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Schrein-Film (wiki: Design/Dimensionsriss-Plan, Abschnitt A): ≈ 5 s, wenn ein Schrein zum ersten Mal im Run
    // erwacht – die Kamera fliegt zum Schrein, fährt einen langsamen Bogen um ihn (Lichtsäule im Bild) und gleitet
    // zurück. Titel „Der Feuer-Schrein erwacht“, Untertitel mit dem Zauber, den der Schrein freischaltet.
    // Auslöser: Shrine.OnAnyShrineAwakened (feuert auf Server und Clients). Erwacht ein Schrein nach einem Fehlschlag
    // erneut, kommt nur die Meldung (ToastUI) – gemerkt wird lokal je Schrein; die Komponente stirbt mit der Szene,
    // ein neuer Run beginnt also leer. Kein Film bei „Intro-Film zeigen“ = aus, bei laufendem Film, Spielende oder
    // offenem Fenster (Cutscene.CanStartNow). Live und lokal wie der Intro-Film: nur die eigene Eingabe ist gesperrt
    // (Cutscene.IsPlaying), überspringbar. Alles Gemeinsame macht der Cutscene-Dienst; die Bahn ist reine Mathematik
    // (ShrineCutscenePath / FocusShotPath). Die Komponente hängt am Cutscene-Objekt (legt sich selbst an).
    // Test im Editor (Play Mode): ShrineCutscene.PlayForTest(), dann Cutscene.SeekForTest/TestSpeed/Skip.
    public class ShrineCutscene : MonoBehaviour
    {
        public const string FilmName = "Shrine";

        // Der Schrein-Film läuft (inkl. Rückblende)
        public static bool IsPlaying => _instance != null && _instance._film != null && Cutscene.Current == _instance._film;

        // Editor/Tests: Film sofort starten (unabhängig von Schalter, Zustand und „schon gezeigt“; bricht einen laufenden
        // Film ab). null → erster Schrein der Szene. false ohne Spielszene/Schrein/Kamera.
        public static bool PlayForTest(Shrine s = null)
        {
            Cutscene.EnsureInstance();
            if (_instance == null)
            {
                Debug.LogWarning("[ShrineCutscene] Keine Spielszene (kein WaveManager) – kein Film.");
                return false;
            }
            if (s == null)
                foreach (var shrine in Shrine.All)
                    if (shrine != null) { s = shrine; break; }
            if (s == null)
            {
                Debug.LogWarning("[ShrineCutscene] Kein Schrein in der Szene – kein Film.");
                return false;
            }
            return _instance.Begin(s, true);
        }

        // Editor/Tests: Wurde der Film für diesen Schrein in diesem Run schon ausgelöst (oder bewusst ausgelassen)?
        public static bool WasShown(Shrine s) => _instance != null && s != null && _instance._seen.Contains(s);

        private static ShrineCutscene _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
        }

        private readonly HashSet<Shrine> _seen = new HashSet<Shrine>(); // Schreine, die in diesem Run schon einmal erwacht sind
        private CutsceneFilm _film;

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }
            _instance = this;
            Shrine.OnAnyShrineAwakened += HandleShrineAwakened;
        }

        void OnDestroy()
        {
            Shrine.OnAnyShrineAwakened -= HandleShrineAwakened;
            if (_instance == this) _instance = null;
        }

        private void HandleShrineAwakened(Shrine s)
        {
            if (s == null || !_seen.Add(s)) return; // erneutes Erwachen nach einem Fehlschlag: nur die Meldung
            if (!Cutscene.Enabled || Cutscene.IsPlaying || !Cutscene.CanStartNow) return;
            Begin(s, false);
        }

        private bool Begin(Shrine s, bool test)
        {
            if (!Cutscene.TryGetCameraPose(out CutscenePose start)) return false;
            Vector3 center = s.CenterPosition;
            var path = ShrineCutscenePath.Create(start, center, ShrineCutscenePath.ApproachAngle(start, center));
            Cutscene.ChooseFraming(path, s.transform);

            var opts = new FocusShotOptions
            {
                Name = FilmName,
                BarsIn = ShrineCutscenePath.BarsIn,
                Title = $"Der {s.DisplayName} erwacht",
                Subtitle = Subtitle(s),
                TitleIn = ShrineCutscenePath.TitleIn,
                TitleOut = ShrineCutscenePath.TitleOut,
                TitleScale = 0.72f,
                ArriveSfx = SfxId.ShrineAwakenCine,
                ArriveSfxTime = ShrineCutscenePath.SoundTime,
            };
            var film = Cutscene.BuildFocusFilm(path, opts);
            film.FocusFrom = ShrineCutscenePath.FocusFrom;
            var old = _film;
            _film = film;
            if (!Cutscene.Play(film, test))
            {
                _film = old;
                return false;
            }
            if (test) Debug.Log($"[ShrineCutscene] Film gestartet: {s.DisplayName}, Winkel {path.StartAngle:0}° {(path.Sweep < 0f ? "−" : "+")}{Mathf.Abs(path.Sweep):0}°, Abstand {path.Radius:0.#} m, Höhe {path.Height:0.#} m.");
            return true;
        }

        // „Haltet seinen Kreis – Belohnung: Flammenwelle“ mit dem Element-Zauber des eigenen Champions (wie der Toast
        // beim Einnehmen); ohne Figur der allgemeine Name des Element-Zaubers
        private static string Subtitle(Shrine s)
        {
            int i = s.ElementIndex;
            var av = PlayerAvatar.Local;
            var pa = av != null && av.Abilities != null ? av.Abilities : PlayerAbilities.Instance;
            string ability = pa != null ? pa.GetElementAbilityName(i) : ElementInfo.AbilityName(i);
            if (string.IsNullOrEmpty(ability)) return "Haltet seinen Kreis und schaltet seinen Zauber frei";
            return $"Haltet seinen Kreis – Belohnung: {ability}";
        }
    }
}
