using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    public enum SfxId
    {
        ArcaneBallCast,
        ArcaneBallHit,
        Blink,
        FireWave,
        FrostNova,
        StoneWall,
        HolyCircle,
        EnemyDeath,
        PlayerHurt,
        ShrineCaptured
    }

    // Zentrale Sound-Ausgabe (Clips aus dem FunProject). Liegt auf dem Managers-Objekt.
    // Zauber-Sounds hängen an PlayerAbilities.OnAbilityCast, der Rest wird per GameAudio.Play(...) ausgelöst.
    public class GameAudio : MonoBehaviour
    {
        [System.Serializable]
        public class SfxEntry
        {
            public SfxId Id;
            public AudioClip[] Clips;
            [Range(0f, 1f)] public float Volume = 0.7f;
            public Vector2 Pitch = new Vector2(0.95f, 1.05f);
            [Tooltip("Mindestabstand zwischen zwei Abspielungen (verhindert Sound-Spam, z. B. viele Tode in einem Frame).")]
            public float MinInterval = 0.05f;
        }

        public static GameAudio Instance { get; private set; }

        [Range(0f, 1f)] public float MasterVolume = 1f;
        [Tooltip("0 = rein 2D, 1 = voll 3D. Die Kamera ist weit weg, daher nur leicht räumlich.")]
        [Range(0f, 1f)] public float SpatialBlend = 0.25f;
        public int Voices = 16;
        public List<SfxEntry> Entries = new List<SfxEntry>();

        private readonly Dictionary<SfxId, SfxEntry> _map = new Dictionary<SfxId, SfxEntry>();
        private readonly Dictionary<SfxId, float> _lastPlayed = new Dictionary<SfxId, float>();
        private AudioSource[] _pool;
        private int _next;
        private PlayerAbilities _abilities;

        void Awake()
        {
            Instance = this;
            foreach (var e in Entries) if (e != null) _map[e.Id] = e;

            _pool = new AudioSource[Mathf.Max(1, Voices)];
            for (int i = 0; i < _pool.Length; i++)
            {
                var go = new GameObject("Sfx_" + i);
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = SpatialBlend;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = 10f;
                src.maxDistance = 80f;
                src.dopplerLevel = 0f;
                _pool[i] = src;
            }
        }

        void OnEnable()
        {
            Shrine.OnAnyShrineCompleted += OnShrineCompleted;
        }

        void OnDisable()
        {
            Shrine.OnAnyShrineCompleted -= OnShrineCompleted;
            if (_abilities != null) _abilities.OnAbilityCast -= OnAbilityCast;
        }

        void Start()
        {
            _abilities = PlayerAbilities.Instance;
            if (_abilities != null) _abilities.OnAbilityCast += OnAbilityCast;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public static void Play(SfxId id, Vector3? position = null)
        {
            if (Instance != null) Instance.PlayInternal(id, position);
        }

        private void PlayInternal(SfxId id, Vector3? position)
        {
            if (!_map.TryGetValue(id, out var e) || e.Clips == null || e.Clips.Length == 0) return;

            float now = Time.unscaledTime;
            if (_lastPlayed.TryGetValue(id, out float last) && now - last < e.MinInterval) return;
            _lastPlayed[id] = now;

            AudioClip clip = e.Clips[Random.Range(0, e.Clips.Length)];
            if (clip == null) return;

            AudioSource src = _pool[_next];
            _next = (_next + 1) % _pool.Length;

            Vector3 pos = position ?? (Camera.main != null ? Camera.main.transform.position : transform.position);
            src.transform.position = pos;
            src.spatialBlend = position.HasValue ? SpatialBlend : 0f;
            src.pitch = Random.Range(e.Pitch.x, e.Pitch.y);
            src.clip = clip;
            src.volume = e.Volume * MasterVolume;
            src.Play();
        }

        private void OnAbilityCast(AbilityId id)
        {
            Vector3 pos = _abilities != null ? _abilities.transform.position : transform.position;
            switch (id)
            {
                case AbilityId.ArcaneBall: Play(SfxId.ArcaneBallCast, pos); break;
                case AbilityId.Blink: Play(SfxId.Blink, pos); break;
                case AbilityId.FireWave: Play(SfxId.FireWave, pos); break;
                case AbilityId.FrostNova: Play(SfxId.FrostNova, pos); break;
                case AbilityId.StoneWall: Play(SfxId.StoneWall, pos); break;
                case AbilityId.HolyCircle: Play(SfxId.HolyCircle, pos); break;
            }
        }

        private void OnShrineCompleted(Shrine s)
        {
            Play(SfxId.ShrineCaptured);
        }
    }
}
