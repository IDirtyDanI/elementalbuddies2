using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Vorschau der nächsten Welle (Plan „Fesselung“ C1): Zusammensetzung wie beim Start (WaveManager.BuildWave),
    // neue Gegnertypen, Boss, Ereignisse (Portal, Schrein, Händler, Ernte-Welle) und Abstand zum nächsten Boss.
    // Läuft auf jedem Rechner (Wellenlisten, Portale und Zeitplan sind überall gleich).
    public class WavePreview
    {
        public struct Group
        {
            public EnemyConfigSO Config;
            public int Count;
            public bool IsNew;
        }

        public int Wave;
        public readonly List<Group> Groups = new List<Group>();
        public EnemyConfigSO Boss;
        public readonly List<string> Events = new List<string>();
        public bool Harvest;
        public int NextBossWave = -1;          // nächste Boss-Welle nach dieser (−1 = keine in Sicht)
        public EnemyConfigSO NextBoss;
        public int TotalCount;
        public int EliteCount;
        public WaveEvent Event;

        // Erste Welle, in der ein Gegnertyp vorkommt (pro WaveManager gecacht)
        private static readonly Dictionary<EnemyConfigSO, int> _firstWave = new Dictionary<EnemyConfigSO, int>();
        private static int _scannedUpTo;
        private static WaveManager _scannedFor;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _firstWave.Clear();
            _scannedUpTo = 0;
            _scannedFor = null;
        }

        private const int BossLookahead = 8;

        public static WavePreview Build(WaveManager wm, int wave)
        {
            if (wm == null || wave < 1) return null;
            var wc = wm.BuildWave(wave);
            if (wc == null) return null;
            ScanFirstWaves(wm, wave);

            var p = new WavePreview { Wave = wave, Harvest = wm.IsHarvestWave(wave) };
            var index = new Dictionary<EnemyConfigSO, int>();
            foreach (var g in wc.EnemiesToSpawn)
            {
                if (g == null || g.EnemyType == null || g.Count <= 0) continue;
                if (g.EnemyType.IsBoss && p.Boss == null) p.Boss = g.EnemyType;
                p.TotalCount += g.Count;
                if (index.TryGetValue(g.EnemyType, out int i))
                {
                    var e = p.Groups[i];
                    e.Count += g.Count;
                    p.Groups[i] = e;
                    continue;
                }
                index[g.EnemyType] = p.Groups.Count;
                bool isNew = _firstWave.TryGetValue(g.EnemyType, out int first) && first == wave;
                p.Groups.Add(new Group { Config = g.EnemyType, Count = g.Count, IsNew = isNew });
            }
            // Boss zuerst, dann neue Typen, dann nach Anzahl
            p.Groups.Sort((a, b) =>
            {
                int ba = a.Config.IsBoss ? 0 : 1, bb = b.Config.IsBoss ? 0 : 1;
                if (ba != bb) return ba.CompareTo(bb);
                int na = a.IsNew ? 0 : 1, nb = b.IsNew ? 0 : 1;
                if (na != nb) return na.CompareTo(nb);
                return b.Count.CompareTo(a.Count);
            });

            foreach (var portal in SpawnPortal.All)
                if (portal != null && !portal.IsOpen && portal.OpenFromWave == wave)
                    p.Events.Add($"Neues Portal im {portal.DisplayName}");

            var sm = ShrineManager.Instance;
            if (sm != null)
            {
                int sw = -1;
                var next = Net.IsServer ? sm.GetNextScheduled(out sw) : null;
                if (next != null && sw == wave) p.Events.Add($"{next.DisplayName} erwacht");
                else if (!Net.IsServer && sm.AwakenAtWave != null && System.Array.IndexOf(sm.AwakenAtWave, wave) >= 0) p.Events.Add("Ein Schrein erwacht");
            }
            var mm = MerchantManager.Instance;
            if (mm != null && mm.IsMerchantWave(wave)) p.Events.Add("Ein Händler öffnet seinen Stand");
            // Wellen-Ereignis (C5)
            var ev = wm.EventFor(wave);
            if (ev != WaveEvent.None)
            {
                p.Event = ev;
                p.Events.Insert(0, $"<b><color=#{WaveEvents.DarkHex(ev)}>{WaveEvents.Name(ev)}:</color></b> {WaveEvents.Description(ev)}");
            }
            // Morgengrauen
            if (wave == SiegeLevels.DawnWave && !wm.DawnReached)
                p.Events.Add("Letzte Welle der Nacht – danach graut der Morgen!");
            else if (!wm.DawnReached && wave >= SiegeLevels.DawnWave - 5 && wave < SiegeLevels.DawnWave)
            {
                int left = SiegeLevels.DawnWave - wave + 1;
                p.Events.Add($"Morgengrauen in {left} Wellen – halte durch!");
            }

            // Elite-Gegner (C4): deterministischer Plan, gleiche Zusammensetzung wie beim Start
            var elites = wm.PlanElites(wc, wave);
            if (elites.Count > 0)
            {
                var counts = new int[EliteInfo.Count + 1];
                foreach (var e in elites) counts[(int)e]++;
                var parts = new List<string>();
                for (int i = 1; i < counts.Length; i++)
                    if (counts[i] > 0) parts.Add($"<color=#{EliteInfo.DarkHex((EliteAffix)i)}>{counts[i]}× {EliteInfo.Name((EliteAffix)i)}</color>");
                p.Events.Add($"Elite-Gegner: {string.Join(", ", parts)}");
                p.EliteCount = elites.Count;
            }
            if (p.Harvest) p.Events.Add($"Ernte-Welle: weniger Gegner, +{(wm.HarvestBonusFactor - 1f) * 100f:0} % Wellenbonus");

            for (int w = wave + 1; w <= wave + BossLookahead; w++)
            {
                if (!wm.IsBossWave(w)) continue;
                p.NextBossWave = w;
                var bw = wm.BuildWave(w);
                if (bw != null)
                    foreach (var g in bw.EnemiesToSpawn)
                        if (g != null && g.EnemyType != null && g.EnemyType.IsBoss && g.Count > 0) { p.NextBoss = g.EnemyType; break; }
                break;
            }
            return p;
        }

        // Erste Auftrittswelle je Gegnertyp bis einschließlich maxWave (ohne Rampe: Anzahl > 0 genügt)
        private static void ScanFirstWaves(WaveManager wm, int maxWave)
        {
            if (_scannedFor != wm)
            {
                _firstWave.Clear();
                _scannedUpTo = 0;
                _scannedFor = wm;
            }
            for (int w = _scannedUpTo + 1; w <= maxWave; w++)
            {
                var wc = wm.BuildWave(w);
                if (wc == null || wc.EnemiesToSpawn == null) continue;
                foreach (var g in wc.EnemiesToSpawn)
                    if (g != null && g.EnemyType != null && g.Count > 0 && !_firstWave.ContainsKey(g.EnemyType))
                        _firstWave[g.EnemyType] = w;
            }
            _scannedUpTo = Mathf.Max(_scannedUpTo, maxWave);
        }

        // Anzeigename eines Gegnertyps (DisplayName, sonst Asset-Name)
        public static string NameOf(EnemyConfigSO c) =>
            c == null ? "?" : !string.IsNullOrEmpty(c.DisplayName) ? c.DisplayName : c.name;
    }
}
