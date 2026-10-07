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
        ShrineCaptured,
        Fusion,
        BuddyDeath,
        BossSpawn,
        ShardPickup
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

        // Spieler-Einstellungen (0..1, gespeichert in PlayerPrefs)
        public const string PrefMaster = "vol_master", PrefMusic = "vol_music", PrefSfx = "vol_sfx";

        [Range(0f, 1f)] public float MasterVolume = 1f;
        [Range(0f, 1f)] public float SfxVolume = 1f;
        [Tooltip("0 = rein 2D, 1 = voll 3D. Die Kamera ist weit weg, daher nur leicht räumlich.")]
        [Range(0f, 1f)] public float SpatialBlend = 0.25f;
        public int Voices = 16;
        public List<SfxEntry> Entries = new List<SfxEntry>();

        [Header("Musik")]
        [Tooltip("Hintergrundmusik in Schleife (aus dem FunProject: Game Music Intro).")]
        public AudioClip MusicClip;
        [Range(0f, 1f)] public float MusicVolume = 0.2f;
        public float MusicFadeIn = 2f;
        [Tooltip("Überblendzeit bei Regler- oder Pause-Wechseln (nach dem Einblenden zu Spielbeginn).")]
        public float MusicChangeTime = 0.25f;
        [Tooltip("Musik-Regler des Spielers (0..1), multipliziert mit MusicVolume.")]
        [Range(0f, 1f)] public float MusicLevel = 1f;
        [Tooltip("Musik-Faktor während das Pause-Menü offen ist.")]
        [Range(0f, 1f)] public float PauseMusicDuck = 0.45f;

        private readonly Dictionary<SfxId, SfxEntry> _map = new Dictionary<SfxId, SfxEntry>();
        private readonly Dictionary<SfxId, float> _lastPlayed = new Dictionary<SfxId, float>();
        private AudioSource[] _pool;
        private int _next;
        private PlayerAbilities _abilities;
        private AudioSource _music;
        private bool _musicFadedIn;

        void Awake()
        {
            Instance = this;

            // Gespeicherte Lautstärken laden (sonst Inspector-Defaults)
            if (PlayerPrefs.HasKey(PrefMaster)) MasterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(PrefMaster));
            if (PlayerPrefs.HasKey(PrefMusic)) MusicLevel = Mathf.Clamp01(PlayerPrefs.GetFloat(PrefMusic));
            if (PlayerPrefs.HasKey(PrefSfx)) SfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(PrefSfx));

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

        private void StartMusic()
        {
            if (MusicClip == null) return;
            _music = gameObject.AddComponent<AudioSource>();
            _music.clip = MusicClip;
            _music.loop = true;
            _music.playOnAwake = false;
            _music.spatialBlend = 0f;
            _music.priority = 0;
            _music.volume = 0f;
            _music.ignoreListenerPause = true; // Musik läuft im Pause-Menü (AudioListener.pause) gedämpft weiter
            _music.Play();
        }

        void Update()
        {
            if (_abilities == null && PlayerAvatar.Local != null) BindAbilities();
            if (_music == null) return;
            float target = MusicVolume * MusicLevel * MasterVolume;
            if (PauseManager.IsPaused) target *= PauseMusicDuck;
            // Nur der Spielstart blendet langsam ein; Regler- und Pause-Wechsel greifen fast sofort.
            // Tempo am Grundpegel (MusicVolume) bemessen, nicht am Ziel – sonst wird das Leiserdrehen quälend langsam.
            float fadeTime = _musicFadedIn ? MusicChangeTime : MusicFadeIn;
            float step = fadeTime > 0f ? Mathf.Max(MusicVolume, 0.01f) / fadeTime * Time.unscaledDeltaTime : 1f;
            _music.volume = Mathf.MoveTowards(_music.volume, target, step);
            if (!_musicFadedIn && Mathf.Approximately(_music.volume, target)) _musicFadedIn = true;
        }

        public void SetMaster(float v) { MasterVolume = Mathf.Clamp01(v); SavePref(PrefMaster, MasterVolume); }
        public void SetMusic(float v) { MusicLevel = Mathf.Clamp01(v); SavePref(PrefMusic, MusicLevel); }
        public void SetSfx(float v) { SfxVolume = Mathf.Clamp01(v); SavePref(PrefSfx, SfxVolume); }

        private static void SavePref(string key, float v)
        {
            PlayerPrefs.SetFloat(key, v);
            PlayerPrefs.Save();
        }

        void OnEnable()
        {
            Shrine.OnAnyShrineCompleted += OnShrineCompleted;
        }

        void OnDisable()
        {
            Shrine.OnAnyShrineCompleted -= OnShrineCompleted;
            PlayerAvatar.OnLocalAvatarSpawned -= HandleLocalAvatarSpawned;
            if (_abilities != null) _abilities.OnAbilityCast -= OnAbilityCast;
            _abilities = null;
        }

        void Start()
        {
            StartMusic();
            PlayerAvatar.OnLocalAvatarSpawned += HandleLocalAvatarSpawned;
            BindAbilities();
        }

        // Zauber-Sounds der eigenen Figur (spawnt erst über das Netz → verzögert binden)
        private void HandleLocalAvatarSpawned(PlayerAvatar avatar) => BindAbilities();

        private void BindAbilities()
        {
            var av = PlayerAvatar.Local;
            var a = av != null ? av.Abilities : null;
            if (a == _abilities) return;
            if (_abilities != null) _abilities.OnAbilityCast -= OnAbilityCast;
            _abilities = a;
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

        // Hat der Eintrag Clips? (für Fallback-Sounds)
        public static bool Has(SfxId id) =>
            Instance != null && Instance._map.TryGetValue(id, out var e) && e.Clips != null && e.Clips.Length > 0;

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
            src.volume = e.Volume * SfxVolume * MasterVolume;
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
