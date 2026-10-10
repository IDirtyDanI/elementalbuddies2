using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace ElementalBuddies
{
    // Buddy-Persönlichkeit (Plan „Fesselung“ G2): kleine Sprechblasen und Stimmchen über den Buddies – bei Aufwertung,
    // Fusion, Boss-Sieg, Tod eines Kollegen in der Nähe, knapp gerettetem Nexus und zum Wellenstart. Rein Optik,
    // auf jedem Rechner aus Ereignissen, die dort ohnehin feuern. Stimme: eigener Chirp, Tonhöhe je Element.
    // Liegt auf dem Managers-Objekt (FeedbackDirector legt ihn an).
    public class BuddyEmotes : MonoBehaviour
    {
        [Tooltip("Höchstens so viele Blasen gleichzeitig.")]
        public int MaxBubbles = 4;
        [Tooltip("Mindestabstand zwischen zwei Blasen desselben Buddys (s).")]
        public float PerBuddyCooldown = 6f;

        private static readonly string[] LevelUp = { "Stärker!", "Jawoll!", "Mehr Kraft!", "Hui!" };
        private static readonly string[] Sad = { "Nein…", "Freund!", "Oh nein!", "Wir rächen dich!" };
        private static readonly string[] Cheer = { "Hurra!", "Geschafft!", "Gewonnen!", "Ha!" };
        private static readonly string[] Fused = { "Gemeinsam!", "Zusammen stark!", "Wow!" };
        private static readonly string[] WaveStart = { "Auf sie!", "Ich bin bereit!", "Kommt nur!", "Für die Stadt!" };
        private static readonly string[] Relief = { "Puh!", "Das war knapp!", "Gerade so!" };

        // Element-Farben der Blasen-Schrift (Feuer, Eis, Erde, Licht)
        private static readonly Color[] ElementColors =
        {
            new Color(1f, 0.55f, 0.25f), new Color(0.55f, 0.85f, 1f), new Color(0.6f, 0.85f, 0.35f), new Color(1f, 0.9f, 0.45f),
        };
        // Stimme je Element (Tonhöhe)
        private static readonly float[] ElementPitch = { 1.12f, 1.25f, 0.85f, 1.05f };

        private class Bubble
        {
            public TextMeshPro Text;
            public Transform Follow;
            public float Born, Life;
            public Vector3 Offset;
        }

        private readonly List<Bubble> _bubbles = new List<Bubble>();
        private readonly Dictionary<ElementalBuddy, float> _lastSpoke = new Dictionary<ElementalBuddy, float>();
        private TMP_FontAsset _font;
        private Camera _cam;
        private WaveManager _waves;
        private FusionManager _fusion;

        void Start()
        {
            var hud = FindFirstObjectByType<HUDManager>(FindObjectsInactive.Include);
            if (hud != null && hud.ShardText != null) _font = hud.ShardText.font;
            _waves = WaveManager.Instance;
            _fusion = FusionManager.Instance;
            ElementalBuddy.OnAnyLevelUp += HandleLevelUp;
            ElementalBuddy.OnBuddyDestroyed += HandleDestroyed;
            EnemyBrain.OnEnemyKilled += HandleKilled;
            if (_fusion != null) _fusion.OnFused += HandleFused;
            if (_waves != null) { _waves.OnWaveStart += HandleWaveStart; _waves.OnWaveEnd += HandleWaveEnd; }
        }

        void OnDisable()
        {
            ElementalBuddy.OnAnyLevelUp -= HandleLevelUp;
            ElementalBuddy.OnBuddyDestroyed -= HandleDestroyed;
            EnemyBrain.OnEnemyKilled -= HandleKilled;
            if (_fusion != null) _fusion.OnFused -= HandleFused;
            if (_waves != null) { _waves.OnWaveStart -= HandleWaveStart; _waves.OnWaveEnd -= HandleWaveEnd; }
        }

        // ---------------- Ereignisse ----------------

        private void HandleLevelUp(ElementalBuddy b) => Say(b, Pick(LevelUp), SfxId.BuddyHappy, true);

        private void HandleFused(FusionBuddy f) => Say(f, Pick(Fused), SfxId.BuddyCheer, true);

        // Kollegen in 8 m trauern (höchstens zwei)
        private void HandleDestroyed(ElementalBuddy dead)
        {
            if (dead == null) return;
            int n = 0;
            foreach (var b in Nearest(dead.transform.position, 8f, 2, dead))
            {
                Say(b, Pick(Sad), SfxId.BuddySad, false);
                if (++n >= 2) break;
            }
        }

        // Boss besiegt: bis zu drei Buddies jubeln
        private void HandleKilled(EnemyBrain e)
        {
            if (e == null || !e.IsBoss) return;
            int n = 0;
            foreach (var b in Nearest(e.transform.position, 25f, 3, null))
            {
                Say(b, Pick(Cheer), SfxId.BuddyCheer, n == 0);
                if (++n >= 3) break;
            }
        }

        // Wellenstart: manchmal ruft ein zufälliger Buddy etwas
        private void HandleWaveStart()
        {
            if (Random.value > 0.35f || ElementalBuddy.ActiveCount == 0) return;
            var b = ElementalBuddy.Active[Random.Range(0, ElementalBuddy.ActiveCount)];
            Say(b, Pick(WaveStart), SfxId.BuddyHappy, true);
        }

        // Knapp gerettet: Nexus unter 35 % am Wellenende → der Buddy am nächsten zum Nexus atmet auf
        private void HandleWaveEnd()
        {
            var nexus = Nexus.Instance;
            if (nexus == null || nexus.MaxHP <= 0f || nexus.CurrentHP > nexus.MaxHP * 0.35f) return;
            foreach (var b in Nearest(nexus.transform.position, 30f, 1, null)) Say(b, Pick(Relief), SfxId.BuddyHappy, true);
        }

        // ---------------- Blasen ----------------

        private void Say(ElementalBuddy b, string text, SfxId sound, bool withSound)
        {
            if (b == null || b.IsDead || !b.isActiveAndEnabled) return;
            float now = Time.unscaledTime;
            if (_lastSpoke.TryGetValue(b, out float last) && now - last < PerBuddyCooldown) return;
            if (_bubbles.Count >= MaxBubbles) return;
            _lastSpoke[b] = now;

            int el = Mathf.Clamp(b.ElementIndex, 0, ElementColors.Length - 1);
            var go = new GameObject("Emote");
            var t = go.AddComponent<TextMeshPro>();
            if (_font != null) t.font = _font;
            t.text = text;
            t.fontSize = 5.5f;
            t.alignment = TextAlignmentOptions.Center;
            t.color = ElementColors[el];
            t.outlineWidth = 0.3f;
            t.outlineColor = new Color32(35, 20, 10, 255);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            float h = 2.4f;
            var r = b.GetComponentInChildren<Renderer>();
            if (r != null) h = r.bounds.max.y - b.transform.position.y + 0.9f;
            _bubbles.Add(new Bubble { Text = t, Follow = b.transform, Born = now, Life = 1.8f, Offset = Vector3.up * h });
            if (withSound && GameAudio.Has(sound)) GameAudio.PlayPitched(sound, b.transform.position, ElementPitch[el]);
        }

        void LateUpdate()
        {
            if (_bubbles.Count == 0) return;
            if (_cam == null) _cam = Camera.main;
            float now = Time.unscaledTime;
            for (int i = _bubbles.Count - 1; i >= 0; i--)
            {
                var bb = _bubbles[i];
                float t = (now - bb.Born) / bb.Life;
                if (t >= 1f || bb.Text == null || bb.Follow == null)
                {
                    if (bb.Text != null) Destroy(bb.Text.gameObject);
                    _bubbles.RemoveAt(i);
                    continue;
                }
                float rise = 0.6f * (1f - (1f - t) * (1f - t));
                bb.Text.transform.position = bb.Follow.position + bb.Offset + Vector3.up * rise;
                float pop = t < 0.12f ? Mathf.Lerp(0.4f, 1.1f, t / 0.12f) : t < 0.2f ? Mathf.Lerp(1.1f, 1f, (t - 0.12f) / 0.08f) : 1f;
                bb.Text.transform.localScale = Vector3.one * pop;
                bb.Text.alpha = t > 0.75f ? Mathf.InverseLerp(1f, 0.75f, t) : 1f;
                if (_cam != null) bb.Text.transform.rotation = _cam.transform.rotation;
            }
        }

        private static string Pick(string[] a) => a[Random.Range(0, a.Length)];

        private static readonly List<ElementalBuddy> _near = new List<ElementalBuddy>();
        private static List<ElementalBuddy> Nearest(Vector3 p, float radius, int max, ElementalBuddy except)
        {
            _near.Clear();
            foreach (var b in ElementalBuddy.Active)
                if (b != null && b != except && !b.IsDead && (b.transform.position - p).sqrMagnitude <= radius * radius) _near.Add(b);
            _near.Sort((a, c) => (a.transform.position - p).sqrMagnitude.CompareTo((c.transform.position - p).sqrMagnitude));
            if (_near.Count > max) _near.RemoveRange(max, _near.Count - max);
            return _near;
        }
    }
}
