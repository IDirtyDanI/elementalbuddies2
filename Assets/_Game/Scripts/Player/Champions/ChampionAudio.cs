using UnityEngine;

namespace ElementalBuddies
{
    // Ein Sound eines Champions (Clip + Lautstärke + Tonhöhe). Wird über ChampionAudio abgespielt,
    // damit die Kits eigene Clips nutzen können, ohne GameAudio (SfxId) zu erweitern.
    [System.Serializable]
    public class ChampionSfx
    {
        public AudioClip Clip;
        [Range(0f, 1f)] public float Volume = 0.7f;
        public Vector2 Pitch = new Vector2(0.95f, 1.05f);
        [Tooltip("Mindestabstand zwischen zwei Abspielungen (Spam-Schutz, z. B. Block-Treffer jeden Frame).")]
        public float MinInterval = 0.05f;

        [System.NonSerialized] public float LastPlayed = -999f;

        public void Play(Vector3 position)
        {
            ChampionAudio.Play(this, position);
        }
    }

    // Kleiner Stimmen-Pool für Champion-Sounds; Lautstärke folgt den Reglern von GameAudio (Master × SFX).
    public static class ChampionAudio
    {
        private const int Voices = 8;
        private static AudioSource[] _pool;
        private static int _next;

        public static void Play(ChampionSfx sfx, Vector3 position)
        {
            if (sfx == null || sfx.Clip == null) return;
            float now = Time.unscaledTime;
            if (now - sfx.LastPlayed < sfx.MinInterval) return;
            sfx.LastPlayed = now;
            Play(sfx.Clip, position, sfx.Volume, Random.Range(sfx.Pitch.x, sfx.Pitch.y));
        }

        public static void Play(AudioClip clip, Vector3 position, float volume, float pitch)
        {
            if (clip == null || !Application.isPlaying) return;
            EnsurePool();
            AudioSource src = _pool[_next];
            _next = (_next + 1) % _pool.Length;

            var ga = GameAudio.Instance;
            float master = ga != null ? ga.MasterVolume * ga.SfxVolume : 1f;
            src.transform.position = position;
            src.spatialBlend = ga != null ? ga.SpatialBlend : 0.25f;
            src.pitch = pitch;
            src.clip = clip;
            src.volume = volume * master;
            src.Play();
        }

        private static void EnsurePool()
        {
            if (_pool != null && _pool[0] != null) return;
            var root = new GameObject("ChampionAudio");
            _pool = new AudioSource[Voices];
            for (int i = 0; i < Voices; i++)
            {
                var go = new GameObject("Voice" + i);
                go.transform.SetParent(root.transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = 5f;
                src.maxDistance = 80f;
                _pool[i] = src;
            }
            _next = 0;
        }
    }
}
