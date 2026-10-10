using UnityEngine;

namespace ElementalBuddies
{
    // Adaptive Musik (Plan „Fesselung“ B2), eigene Kompositionen aus art-src/audio/synth_music.py.
    // Zustände nach dem Director-Prinzip Aufbau → Höhepunkt → Entspannung:
    //   Menü        – music_menu
    //   Bauphase    – music_build (ruhig, Vorahnung)
    //   Kampf       – music_combat_base + music_combat_intense als zweite Schicht; deren Lautstärke folgt der Gefahr
    //                 (verbleibende Gegner, Nexus-Leben, eigenes Leben). Beide Spuren starten sample-genau zusammen
    //                 (PlayScheduled auf dieselbe dspTime) und sind gleich lang → bleiben synchron.
    //   Boss        – music_boss, solange ein Boss lebt
    //   Morgengrauen– Musik kurz abgesenkt, damit die Fanfare (SfxId.Dawn) trägt; Game Over – ausblenden
    // Lautstärke: GameAudio.MusicGain (Regler, Master, Pause-Dämpfung). Liegt auf dem GameAudio-Objekt.
    [RequireComponent(typeof(GameAudio))]
    public class MusicDirector : MonoBehaviour
    {
        public AudioClip MenuClip, BuildClip, CombatBaseClip, CombatIntenseClip, BossClip;
        [Tooltip("Überblendzeit zwischen den Zuständen (s).")]
        public float CrossfadeTime = 2.5f;
        [Tooltip("Ausblenden bei Game Over (s).")]
        public float GameOverFade = 4f;
        [Tooltip("So schnell folgt die Kampf-Intensität der Gefahr (Anteil pro Sekunde).")]
        public float IntensityRate = 0.35f;
        [Tooltip("Gefahr über verbleibende Gegner: ab Low steigt sie, bei High ist sie voll.")]
        public int EnemiesLow = 12, EnemiesHigh = 45;

        private enum Layer { Menu, Build, Base, Intense, Boss }
        private AudioSource[] _src;
        private float[] _vol;
        private float _intensity, _duckUntil;
        private bool _gameOver, _menuMode;
        private GameAudio _audio;
        private WaveManager _waves;

        public static MusicDirector Instance { get; private set; }

        void Awake()
        {
            Instance = this;
            _audio = GetComponent<GameAudio>();
            var clips = new[] { MenuClip, BuildClip, CombatBaseClip, CombatIntenseClip, BossClip };
            _src = new AudioSource[clips.Length];
            _vol = new float[clips.Length];
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] == null) continue;
                var go = new GameObject("Music_" + (Layer)i);
                go.transform.SetParent(transform, false);
                var s = go.AddComponent<AudioSource>();
                s.clip = clips[i];
                s.loop = true;
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                s.priority = 0;
                s.volume = 0f;
                s.ignoreListenerPause = true; // läuft im Pause-Menü gedämpft weiter
                _src[i] = s;
            }
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            _waves = WaveManager.Instance;
            _menuMode = _waves == null;
            if (GameManager.Instance != null) GameManager.Instance.OnGameOver += HandleGameOver;
            if (_waves != null) _waves.OnDawn += HandleDawn;
        }

        void OnDisable()
        {
            if (GameManager.Instance != null) GameManager.Instance.OnGameOver -= HandleGameOver;
            if (_waves != null) _waves.OnDawn -= HandleDawn;
        }

        private void HandleGameOver(string reason) => _gameOver = true;
        private void HandleDawn() => _duckUntil = Time.unscaledTime + 7f;

        public bool HasMusic => _src != null && System.Array.Exists(_src, s => s != null);

        void Update()
        {
            if (_src == null) return;
            float dt = Time.unscaledDeltaTime;
            var target = new float[_src.Length];

            if (_menuMode) target[(int)Layer.Menu] = 1f;
            else if (!_gameOver)
            {
                bool combat = _waves.IsWaveActive;
                bool boss = combat && EnemyBrain.ActiveBosses.Count > 0 && _src[(int)Layer.Boss] != null;
                if (boss) target[(int)Layer.Boss] = 1f;
                else if (combat)
                {
                    _intensity = Mathf.MoveTowards(_intensity, Danger(), IntensityRate * dt);
                    target[(int)Layer.Base] = 1f;
                    target[(int)Layer.Intense] = _intensity;
                }
                else target[(int)Layer.Build] = 1f;
                if (!combat) _intensity = Mathf.MoveTowards(_intensity, 0f, IntensityRate * dt);
            }

            // Kampf-Paar gemeinsam (neu) starten, wenn es aus der Stille kommt – sample-genau synchron
            if (target[(int)Layer.Base] > 0f && _vol[(int)Layer.Base] <= 0.001f) StartPair();
            for (int i = 0; i < _src.Length; i++)
            {
                if (_src[i] == null || i == (int)Layer.Intense) continue;
                if (i == (int)Layer.Base) continue;
                if (target[i] > 0f && !_src[i].isPlaying) _src[i].Play();
            }

            float fade = _gameOver ? GameOverFade : CrossfadeTime;
            float gain = _audio != null ? _audio.MusicGain : 0.2f;
            if (Time.unscaledTime < _duckUntil) gain *= 0.2f;
            for (int i = 0; i < _src.Length; i++)
            {
                var s = _src[i];
                if (s == null) continue;
                // Intensitäts-Schicht folgt direkt (eigene Glättung über _intensity), sonst Überblendung
                _vol[i] = i == (int)Layer.Intense && target[(int)Layer.Base] > 0f
                    ? Mathf.MoveTowards(_vol[i], target[i], dt / 0.5f)
                    : Mathf.MoveTowards(_vol[i], target[i], dt / Mathf.Max(0.05f, fade));
                s.volume = _vol[i] * gain;
                if (_vol[i] <= 0.001f && target[i] <= 0f && s.isPlaying && i != (int)Layer.Intense) s.Stop();
            }
            // Intense läuft nur mit Base
            var intense = _src[(int)Layer.Intense];
            var bas = _src[(int)Layer.Base];
            if (intense != null && bas != null && !bas.isPlaying && intense.isPlaying) intense.Stop();
        }

        private void StartPair()
        {
            var bas = _src[(int)Layer.Base];
            if (bas == null || bas.isPlaying) return;
            var intense = _src[(int)Layer.Intense];
            double t = AudioSettings.dspTime + 0.08;
            bas.PlayScheduled(t);
            if (intense != null)
            {
                intense.Stop();
                intense.PlayScheduled(t);
            }
        }

        // Gefahr 0..1: verbleibende Gegner, Nexus-Schaden, eigenes Leben
        private float Danger()
        {
            float d = Mathf.InverseLerp(EnemiesLow, EnemiesHigh, _waves.EnemiesRemaining);
            var nexus = Nexus.Instance;
            if (nexus != null && nexus.MaxHP > 0f) d = Mathf.Max(d, Mathf.Clamp01((1f - nexus.CurrentHP / nexus.MaxHP) * 2f));
            var me = PlayerAvatar.Local;
            if (me != null && me.MaxHealth > 0f && me.Health01 < 0.35f) d = Mathf.Max(d, 0.8f);
            if (_waves.ActiveEvent != WaveEvent.None) d = Mathf.Max(d, 0.6f);
            return d;
        }
    }
}
