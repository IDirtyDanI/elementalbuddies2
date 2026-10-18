using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Flugsummen des Arkanballs (Spieltest 2026-10-18, Paket S): schimmernde Schleife arcane_loop (art-src/audio/synth_sfx.py)
    // an einer eigenen räumlichen AudioSource, sitzt auf dem Root von ArcaneBall.prefab und lebt so lange wie die Kugel.
    // Wie ThreatLoopAudio regelt der waagerechte Abstand Hörfokus ↔ Kugel die Lautstärke (die Kamera/der AudioListener schwebt
    // ~15 m über dem Spieler, Unitys Abstandsdämpfung wäre dort fast konstant), gekoppelt an die Spieler-Regler
    // (GameAudio.SfxVolume × MasterVolume; ein Mixer existiert im Projekt nicht). Kurzes Einblenden, zufälliger Startpunkt
    // und leichte Tonhöhen-Streuung (mehrere Kugeln nicht im Gleichschritt). Bei Dauerfeuer (alle 0,6 s) sind höchstens
    // MaxAudible Kugeln hörbar – die jüngsten; ältere blenden aus. Wird die Kugel zerstört (Treffer, Wand, Lebensdauer),
    // klingt ein kurzer, abgelöster Rest aus statt hart abzubrechen. Im Pause-Menü (AudioListener.pause) ruht der Ton.
    // Clip: eigenes Feld, sonst GameAudio-Eintrag SfxId.ArcaneBallLoop (GameFeelSetup). Rein lokal, kein Netz.
    public class ArcaneBallHum : MonoBehaviour
    {
        [Tooltip("Flug-Schleife (arcane_loop). Leer = Clip aus dem GameAudio-Eintrag ArcaneBallLoop.")]
        public AudioClip Clip;
        [Range(0f, 1f)] public float Volume = 0.35f;
        [Tooltip("Bis zu diesem waagerechten Abstand (m) volle Lautstärke.")]
        public float FullDistance = 5f;
        [Tooltip("Ab diesem waagerechten Abstand (m) still.")]
        public float FadeDistance = 26f;
        [Tooltip("0 = 2D, 1 = voll räumlich (nur Richtung, Abstand regelt diese Komponente).")]
        [Range(0f, 1f)] public float SpatialBlend = 0.6f;
        [Tooltip("Zufällige Tonhöhe je Kugel (min/max).")]
        public Vector2 Pitch = new Vector2(0.97f, 1.03f);
        [Tooltip("Einblenden (s).")]
        public float FadeIn = 0.08f;
        [Tooltip("Ausklingen nach dem Zerstören der Kugel bzw. beim Verstummen (s).")]
        public float FadeOut = 0.12f;
        [Tooltip("Höchstens so viele Kugeln gleichzeitig hörbar (die jüngsten).")]
        public int MaxAudible = 4;

        // Alle lebenden Kugeln in Spawn-Reihenfolge (älteste zuerst)
        private static readonly List<ArcaneBallHum> Active = new List<ArcaneBallHum>();
        private static bool _quitting;

        private AudioSource _src;
        private float _vol;

        public AudioSource Source => _src;
        public static int ActiveCount => Active.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Active.Clear();
            _quitting = false;
            Application.quitting -= OnQuit;
            Application.quitting += OnQuit;
        }

        private static void OnQuit() => _quitting = true;

#if UNITY_EDITOR
        // Beim Hinzufügen im Editor den eigenen Synthese-Clip vorbelegen
        void Reset()
        {
            Clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(DefaultClipPath);
        }
#endif

        public const string DefaultClipPath = "Assets/_Game/Audio/Generated/arcane_loop.wav";

        void Awake()
        {
            var clip = Clip != null ? Clip : GameAudio.GetClip(SfxId.ArcaneBallLoop);
            if (clip == null) { enabled = false; return; }
            // Eigenes Kind-Objekt: die Kugel hat evtl. weitere AudioSources/Skalierung, die Quelle soll davon unberührt bleiben
            var go = new GameObject("Summen");
            go.transform.SetParent(transform, false);
            _src = go.AddComponent<AudioSource>();
            _src.clip = clip;
            _src.loop = true;
            _src.playOnAwake = false;
            _src.spatialBlend = SpatialBlend;
            _src.dopplerLevel = 0f;
            _src.priority = 160;
            _src.rolloffMode = AudioRolloffMode.Custom;
            _src.minDistance = 1f;
            _src.maxDistance = 500f;
            _src.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Constant(0f, 1f, 1f));
            _src.pitch = Random.Range(Mathf.Min(Pitch.x, Pitch.y), Mathf.Max(Pitch.x, Pitch.y));
            _src.volume = 0f;
        }

        void OnEnable()
        {
            if (_src != null && !Active.Contains(this)) Active.Add(this);
        }

        void OnDisable()
        {
            Active.Remove(this);
        }

        void Start()
        {
            if (_src == null) return;
            _src.time = Random.Range(0f, Mathf.Max(0f, _src.clip.length - 0.05f));
            _src.Play();
        }

        void Update()
        {
            if (_src == null) return;
            float target = 0f;
            int idx = Active.IndexOf(this);
            bool audible = idx >= 0 && idx >= Active.Count - Mathf.Max(1, MaxAudible);
            if (audible && TryGetFocus(out var focus))
            {
                Vector3 d = focus - transform.position;
                d.y = 0f;
                float t = Mathf.Clamp01((d.magnitude - FullDistance) / Mathf.Max(0.01f, FadeDistance - FullDistance));
                var ga = GameAudio.Instance;
                float player = ga != null ? ga.SfxVolume * ga.MasterVolume : 1f;
                target = Volume * (1f - t * t * (3f - 2f * t)) * player;
            }
            // Ein- und Ausblenden über die gesamte Lautstärke in FadeIn bzw. FadeOut Sekunden
            float time = target > _vol ? FadeIn : FadeOut;
            float step = Time.unscaledDeltaTime / Mathf.Max(0.01f, time) * Mathf.Max(Volume, 0.05f);
            _vol = Mathf.MoveTowards(_vol, target, step);
            _src.volume = _vol;
        }

        void OnDestroy()
        {
            Active.Remove(this);
            // Abgelösten Rest ausklingen lassen (kein Knacksen beim harten Abbruch). Nicht beim Beenden/Szenenwechsel.
            if (_quitting || _src == null || !_src.isPlaying || _vol < 0.002f) return;
            var runner = GameAudio.Instance;
            if (runner == null || !runner.isActiveAndEnabled || !gameObject.scene.isLoaded) return;
            var go = new GameObject("ArcaneBallHum_Rest");
            go.transform.position = _src.transform.position;
            var tail = go.AddComponent<AudioSource>();
            tail.clip = _src.clip;
            tail.loop = false;
            tail.playOnAwake = false;
            tail.spatialBlend = _src.spatialBlend;
            tail.dopplerLevel = 0f;
            tail.priority = _src.priority;
            tail.rolloffMode = AudioRolloffMode.Custom;
            tail.minDistance = 1f;
            tail.maxDistance = 500f;
            tail.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Constant(0f, 1f, 1f));
            tail.pitch = _src.pitch;
            tail.volume = _vol;
            tail.time = _src.time;
            tail.Play();
            float fade = Mathf.Max(0.02f, FadeOut);
            Destroy(go, fade + 0.1f); // Sicherung, falls der Ausblend-Lauf abbricht
            runner.StartCoroutine(FadeTail(tail, fade));
        }

        private static IEnumerator FadeTail(AudioSource src, float duration)
        {
            float start = src != null ? src.volume : 0f;
            float t = 0f;
            while (src != null && t < duration)
            {
                t += Time.unscaledDeltaTime;
                src.volume = start * (1f - Mathf.Clamp01(t / duration));
                yield return null;
            }
            if (src != null) Destroy(src.gameObject);
        }

        private static bool TryGetFocus(out Vector3 focus)
        {
            var av = PlayerAvatar.Local;
            if (av != null) { focus = av.transform.position; return true; }
            var cam = Camera.main;
            if (cam != null)
            {
                // Bodenpunkt in Blickrichtung (Spielkamera schaut schräg nach unten)
                var r = new Ray(cam.transform.position, cam.transform.forward);
                if (r.direction.y < -0.05f) { focus = r.GetPoint(-r.origin.y / r.direction.y); return true; }
                focus = cam.transform.position;
                return true;
            }
            focus = Vector3.zero;
            return false;
        }
    }
}
