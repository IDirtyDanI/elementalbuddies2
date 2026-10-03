using UnityEngine;
using System.Collections.Generic;

namespace ElementalBuddies
{
    // Plant das Erwachen der Schreine: AwakenAtWave[i] = Welle (1-basiert), in der der Schrein mit ElementIndex i
    // (0 Feuer, 1 Eis, 2 Erde, 3 Licht) erwacht. Immer nur ein Schrein gleichzeitig erwacht; fällige Schreine warten
    // (Warteschlange nach geplanter Welle, dann ElementIndex) auf die nächste Welle. Gescheiterte Schreine werden
    // RetryAfterWaves Wellen nach der gescheiterten Welle erneut eingeplant.
    public class ShrineManager : MonoBehaviour
    {
        public static ShrineManager Instance { get; private set; }

        [Tooltip("Welle (1-basiert), in der der Schrein mit ElementIndex 0..3 erwacht (Feuer, Eis, Erde, Licht).")]
        public int[] AwakenAtWave = { 4, 6, 8, 10 };
        [Tooltip("Nach einem Fehlschlag in Welle N erwacht der Schrein frühestens in Welle N + RetryAfterWaves.")]
        public int RetryAfterWaves = 2;

        public Shrine ActiveShrine { get; private set; }

        // Geplante Welle pro Schrein (nur Dormant-Schreine)
        private readonly Dictionary<Shrine, int> _scheduledWave = new Dictionary<Shrine, int>();

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Statische Boni überleben ein Szenen-Reload (Restart) → hier zurücksetzen
            ShrineBonuses.ResetAll();
        }

        void Start()
        {
            foreach (var s in Shrine.All)
            {
                if (s == null) continue;
                int idx = s.ElementIndex;
                if (AwakenAtWave != null && idx >= 0 && idx < AwakenAtWave.Length)
                    _scheduledWave[s] = AwakenAtWave[idx];
                else
                    Debug.LogWarning($"ShrineManager: No AwakenAtWave entry for {s.DisplayName} (ElementIndex {idx}).");
            }

            if (WaveManager.Instance != null) WaveManager.Instance.OnWaveStart += HandleWaveStart;
            Shrine.OnAnyShrineCompleted += HandleShrineCompleted;
            Shrine.OnAnyShrineFailed += HandleShrineFailed;
        }

        void OnDestroy()
        {
            if (WaveManager.Instance != null) WaveManager.Instance.OnWaveStart -= HandleWaveStart;
            Shrine.OnAnyShrineCompleted -= HandleShrineCompleted;
            Shrine.OnAnyShrineFailed -= HandleShrineFailed;
            if (Instance == this) Instance = null;
        }

        // Geplante Welle eines Schreins (-1 = nicht geplant / abgeschlossen)
        public int GetScheduledWave(Shrine s) => s != null && _scheduledWave.TryGetValue(s, out int w) ? w : -1;

        // Nächster geplanter Schrein (für eine mögliche Vorschau-Anzeige); null, wenn keiner mehr aussteht
        public Shrine GetNextScheduled(out int wave)
        {
            Shrine best = null;
            wave = int.MaxValue;
            foreach (var kv in _scheduledWave)
            {
                if (kv.Key == null || kv.Key.State != ShrineState.Dormant) continue;
                if (kv.Value < wave || (kv.Value == wave && best != null && kv.Key.ElementIndex < best.ElementIndex))
                {
                    best = kv.Key;
                    wave = kv.Value;
                }
            }
            if (best == null) wave = -1;
            return best;
        }

        // Dev-Modus: nächsten geplanten Schrein sofort erwecken (unabhängig von der Welle)
        public bool DevAwakenNext()
        {
            if (ActiveShrine != null && ActiveShrine.IsAwakened) return false;
            Shrine next = GetNextScheduled(out int _);
            if (next == null || !next.Awaken()) return false;
            ActiveShrine = next;
            _scheduledWave.Remove(next);
            return true;
        }

        private void HandleWaveStart()
        {
            if (ActiveShrine != null && ActiveShrine.IsAwakened) return; // nur einer gleichzeitig

            int wave = WaveManager.Instance != null ? WaveManager.Instance.UpcomingWaveNumber : 0;
            Shrine next = GetNextScheduled(out int scheduled);
            if (next == null || scheduled > wave) return;

            if (next.Awaken())
            {
                ActiveShrine = next;
                _scheduledWave.Remove(next);
            }
        }

        private void HandleShrineCompleted(Shrine s)
        {
            if (s == ActiveShrine) ActiveShrine = null;
            _scheduledWave.Remove(s);
        }

        private void HandleShrineFailed(Shrine s)
        {
            if (s == ActiveShrine) ActiveShrine = null;
            // Am Wellenende ist CurrentWaveIndex bereits erhöht (= Nummer der gescheiterten Welle);
            // bei einem manuellen Fail() mitten in der Welle ist es CurrentWaveIndex + 1.
            var wm = WaveManager.Instance;
            int failedWave = wm == null ? 0 : (wm.IsWaveActive ? wm.CurrentWaveIndex + 1 : wm.CurrentWaveIndex);
            _scheduledWave[s] = failedWave + Mathf.Max(1, RetryAfterWaves);
        }
    }
}
