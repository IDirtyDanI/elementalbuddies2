using UnityEngine;

namespace ElementalBuddies
{
    // Dauer-Klang einer Bedrohungs-Requisite (Ritualkreis: ritual_loop in der Stadt, drum_loop im Dorf). Sitzt im Requisiten-
    // Prefab und lebt nur so lange wie das Ritual (SummonerBrain baut die Requisite beim Ritualbeginn und zerstört sie danach).
    // Eigene räumliche AudioSource (Schleife, 3D). Wie StreamAmbience regelt der waagerechte Abstand Hörfokus ↔ Requisite die
    // Lautstärke (die Kamera/der AudioListener schwebt hoch über dem Spieler, Unitys Abstandsdämpfung wäre dort fast konstant),
    // gekoppelt an die Spieler-Regler (GameAudio.SfxVolume × MasterVolume; ein Mixer existiert im Projekt nicht). Blendet ein,
    // startet an zufälliger Stelle (mehrere Kreise nicht im Gleichschritt). Im Pause-Menü (AudioListener.pause) ruht der Ton.
    public class ThreatLoopAudio : MonoBehaviour
    {
        public AudioClip Clip;
        [Range(0f, 1f)] public float Volume = 0.5f;
        [Tooltip("Bis zu diesem waagerechten Abstand (m) volle Lautstärke.")]
        public float FullDistance = 6f;
        [Tooltip("Ab diesem waagerechten Abstand (m) still.")]
        public float FadeDistance = 32f;
        [Range(0f, 1f)] public float SpatialBlend = 0.6f;
        [Tooltip("Einblenden (s).")]
        public float FadeIn = 0.8f;

        private AudioSource _src;
        private float _vol;

        public AudioSource Source => _src;

        void Awake()
        {
            if (Clip == null) { enabled = false; return; }
            _src = GetComponent<AudioSource>();
            if (_src == null) _src = gameObject.AddComponent<AudioSource>();
            _src.clip = Clip;
            _src.loop = true;
            _src.playOnAwake = false;
            _src.spatialBlend = SpatialBlend;
            _src.dopplerLevel = 0f;
            _src.priority = 180;
            _src.rolloffMode = AudioRolloffMode.Custom;
            _src.minDistance = 1f;
            _src.maxDistance = 500f;
            _src.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Constant(0f, 1f, 1f));
            _src.volume = 0f;
        }

        void Start()
        {
            if (_src == null) return;
            _src.time = Random.Range(0f, Mathf.Max(0f, Clip.length - 0.1f));
            _src.Play();
        }

        void Update()
        {
            if (_src == null) return;
            float gain = 0f;
            if (TryGetFocus(out var focus))
            {
                Vector3 d = focus - transform.position;
                d.y = 0f;
                float t = Mathf.Clamp01((d.magnitude - FullDistance) / Mathf.Max(0.01f, FadeDistance - FullDistance));
                gain = 1f - t * t * (3f - 2f * t);
            }
            var ga = GameAudio.Instance;
            float player = ga != null ? ga.SfxVolume * ga.MasterVolume : 1f;
            float target = Volume * gain * player;
            float step = Time.unscaledDeltaTime / Mathf.Max(0.05f, FadeIn) * Mathf.Max(Volume, 0.05f);
            _vol = Mathf.MoveTowards(_vol, target, step);
            _src.volume = _vol;
        }

        private static bool TryGetFocus(out Vector3 focus)
        {
            var av = PlayerAvatar.Local;
            if (av != null) { focus = av.transform.position; return true; }
            var cam = Camera.main;
            if (cam != null)
            {
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
