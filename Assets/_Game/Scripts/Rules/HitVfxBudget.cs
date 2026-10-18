using System.Collections.Generic;

namespace ElementalBuddies
{
    // Budget für Treffer-Effekte je Fähigkeit (Spieltest 2026-10-18, Paket V, HitVfx): ein Flächenzauber auf 30 Gegnern
    // soll nicht 30 Effekte auf einmal erzeugen. Drei Grenzen, alle rein kosmetisch und pro Rechner:
    //  1. je Auslösung (gleiche Fähigkeit im selben Frame) höchstens MaxPerTrigger Effekte,
    //  2. global ein Eimer mit Burst Plätzen, der mit RefillPerSecond nachläuft,
    //  3. derselbe Gegner bekommt für dieselbe Fähigkeit frühestens nach minInterval Sekunden wieder einen Effekt
    //     (Dauer-Treffer wie Glutsturm-Ticks oder Pfeilregen-Wellen).
    // Abgelehnte Treffer zeigen nur das Aufblitzen (FeedbackDirector.HandleHit). Ohne Unity-Abhängigkeit testbar.
    public sealed class HitVfxBudget
    {
        public int MaxPerTrigger = 8;
        public float Burst = 12f;
        public float RefillPerSecond = 30f;

        private float _tokens;
        private float _lastTime = float.NegativeInfinity;
        private int _triggerFrame = -1;
        private readonly Dictionary<int, int> _perTrigger = new Dictionary<int, int>();
        private readonly Dictionary<long, float> _lastShown = new Dictionary<long, float>();
        private readonly List<long> _prune = new List<long>();
        private float _nextPrune;

        public HitVfxBudget() { }

        public HitVfxBudget(int maxPerTrigger, float burst, float refillPerSecond)
        {
            MaxPerTrigger = maxPerTrigger;
            Burst = burst;
            RefillPerSecond = refillPerSecond;
        }

        public float Tokens => _tokens;

        // Darf für (Gegner, Fähigkeit) jetzt ein Effekt erscheinen? Bei true ist der Platz verbraucht.
        public bool TryShow(int enemyId, int ability, float minInterval, float now, int frame)
        {
            Refill(now);
            if (frame != _triggerFrame)
            {
                _triggerFrame = frame;
                _perTrigger.Clear();
            }
            long key = ((long)enemyId << 6) | (long)(ability & 63);
            if (minInterval > 0f && _lastShown.TryGetValue(key, out float last) && now - last < minInterval) return false;
            _perTrigger.TryGetValue(ability, out int count);
            if (MaxPerTrigger > 0 && count >= MaxPerTrigger) return false;
            if (_tokens < 1f) return false;

            _tokens -= 1f;
            _perTrigger[ability] = count + 1;
            _lastShown[key] = now;
            Prune(now);
            return true;
        }

        private void Refill(float now)
        {
            if (float.IsNegativeInfinity(_lastTime)) _tokens = Burst;
            else if (now > _lastTime) _tokens = System.Math.Min(Burst, _tokens + (now - _lastTime) * RefillPerSecond);
            _lastTime = now;
        }

        // Alte Gegner-Einträge (tote Gegner) gelegentlich entfernen
        private void Prune(float now)
        {
            if (now < _nextPrune) return;
            _nextPrune = now + 5f;
            _prune.Clear();
            foreach (var kv in _lastShown)
                if (now - kv.Value > 5f) _prune.Add(kv.Key);
            foreach (var k in _prune) _lastShown.Remove(k);
        }
    }
}
