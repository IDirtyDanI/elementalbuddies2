using UnityEngine;

namespace ElementalBuddies
{
    // Umgebungsgeräusch entlang einer Linie (Walddorf: Mühlbach, Mühlrad). Eine einzige 3D-AudioSource wird jedes Frame
    // auf den Punkt der Linie geschoben, der dem Hörfokus (eigene Spielfigur, sonst Kamera-Bodenpunkt) am nächsten ist –
    // so klingt der Bach überall gleichmäßig. Die Lautstärke hängt vom waagerechten Abstand Fokus ↔ Linie ab
    // (die Kamera/der AudioListener schwebt ~17 m über dem Spieler, die Unity-Abstandsdämpfung wäre dort fast konstant).
    // Gekoppelt an die Spieler-Regler: Lautstärke × GameAudio.SfxVolume × GameAudio.MasterVolume; im Pause-Menü
    // (AudioListener.pause) ruht der Ton wie alle anderen Effekte. Rein lokal, kein Netz. Eingerichtet von VillageSetup.
    public class StreamAmbience : MonoBehaviour
    {
        [Tooltip("Linie in Welt-Koordinaten (ein Punkt = punktförmige Quelle, z. B. Mühlrad).")]
        public Vector3[] Points = new Vector3[0];
        public AudioClip Clip;
        [Range(0f, 1f)] public float Volume = 0.4f;
        [Tooltip("Bis zu diesem Abstand (m, waagerecht) volle Lautstärke.")]
        public float FullDistance = 3f;
        [Tooltip("Ab diesem Abstand (m, waagerecht) still.")]
        public float FadeDistance = 18f;
        [Tooltip("0 = 2D, 1 = voll räumlich (nur Richtung, Abstand regelt diese Komponente).")]
        [Range(0f, 1f)] public float SpatialBlend = 0.6f;
        [Tooltip("Höhe der Quelle über dem Linienpunkt.")]
        public float Height = 0.3f;
        [Tooltip("Sekunden für Lautstärke-Änderungen (gegen Sprünge beim Teleport/Spawn).")]
        public float Smoothing = 0.35f;

        AudioSource _src;
        float _vol;

        public AudioSource Source => _src;
        public float CurrentVolume => _vol;
        public float LastDistance { get; private set; } = float.MaxValue;

        void Awake()
        {
            if (Clip == null || Points == null || Points.Length == 0) { enabled = false; return; }
            var go = new GameObject("Quelle");
            go.transform.SetParent(transform, false);
            _src = go.AddComponent<AudioSource>();
            _src.clip = Clip;
            _src.loop = true;
            _src.playOnAwake = false;
            _src.spatialBlend = SpatialBlend;
            _src.dopplerLevel = 0f;
            _src.priority = 200;
            _src.rolloffMode = AudioRolloffMode.Custom;
            _src.maxDistance = 500f;
            _src.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Constant(0f, 1f, 1f));
            _src.volume = 0f;
        }

        void Start()
        {
            if (_src == null) return;
            // Mehrere Quellen mit demselben Clip nicht im Gleichschritt starten
            _src.time = Random.Range(0f, Mathf.Max(0f, Clip.length - 0.1f));
            _src.Play();
        }

        void Update()
        {
            if (_src == null) return;
            if (!TryGetFocus(out var focus)) { _vol = 0f; _src.volume = 0f; return; }

            Vector3 p = Nearest(new Vector2(focus.x, focus.z), out float d);
            LastDistance = d;
            _src.transform.position = p + Vector3.up * Height;

            float gain = 1f - Smooth01((d - FullDistance) / Mathf.Max(0.01f, FadeDistance - FullDistance));
            var ga = GameAudio.Instance;
            float player = ga != null ? ga.SfxVolume * ga.MasterVolume : 1f;
            float target = Volume * gain * player;
            float step = Smoothing > 0f ? Time.unscaledDeltaTime / Smoothing : 1f;
            _vol = Mathf.MoveTowards(_vol, target, step * Mathf.Max(Volume, 0.05f));
            _src.volume = _vol;
        }

        static float Smooth01(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        bool TryGetFocus(out Vector3 focus)
        {
            var av = PlayerAvatar.Local;
            if (av != null) { focus = av.transform.position; return true; }
            var cam = Camera.main;
            if (cam != null)
            {
                // Bodenpunkt in Blickrichtung (Spielkamera schaut schräg nach unten)
                var r = new Ray(cam.transform.position, cam.transform.forward);
                if (r.direction.y < -0.05f) { focus = r.GetPoint(-r.origin.y / r.direction.y); return true; }
                focus = cam.transform.position; return true;
            }
            focus = Vector3.zero;
            return false;
        }

        public Vector3 Nearest(Vector2 q, out float dist)
        {
            if (Points.Length == 1) { var a0 = Points[0]; dist = Vector2.Distance(q, new Vector2(a0.x, a0.z)); return a0; }
            float best = float.MaxValue;
            Vector3 bp = Points[0];
            for (int i = 0; i + 1 < Points.Length; i++)
            {
                Vector3 a = Points[i], b = Points[i + 1];
                var a2 = new Vector2(a.x, a.z);
                var ab = new Vector2(b.x, b.z) - a2;
                float l2 = ab.sqrMagnitude;
                float t = l2 < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(q - a2, ab) / l2);
                float dd = Vector2.Distance(q, a2 + ab * t);
                if (dd < best) { best = dd; bp = Vector3.Lerp(a, b, t); }
            }
            dist = best;
            return bp;
        }

        void OnDrawGizmosSelected()
        {
            if (Points == null) return;
            Gizmos.color = new Color(0.3f, 0.7f, 1f);
            for (int i = 0; i + 1 < Points.Length; i++) Gizmos.DrawLine(Points[i], Points[i + 1]);
            foreach (var p in Points) Gizmos.DrawWireSphere(p, FullDistance);
        }
    }
}
