#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

namespace ElementalBuddies
{
    // Messwerkzeug für den Dimensionsriss (nur Editor/Dev-Build, Play Mode, Einzelspiel/Host): lässt einen Champion mit
    // einem üblichen Welle-20-Kartensatz OHNE Unverwundbarkeit gegen den Dimensions-Boss der Map kämpfen und misst
    // Kampfdauer, erlittenen Schaden und Tode (#556/#579).
    // Aufruf: DimensionFightBench.Run("mage dodge") – Schlüsselwörter (Leerzeichen/Komma, Groß/klein egal):
    //   mage/knight/archer/berserker/shaman – Champion (Standard: mage)
    //   dodge (Standard) – weicht Warnflächen aus (nach ReactionTime), hält als Fernkämpfer Abstand
    //   careless – weicht nie aus (bleibt stehen und greift an)
    //   nocards – ohne Kartensatz; x1/x2/x3 – Zeitfaktor (Standard 2)
    // Ergebnis: eine Log-Zeile „[DimFight] …“, LastResult und Anhang an Temp/DimFight/results.txt.
    // Kartensatz (Annahme, siehe Bericht): bis Welle 20 gibt es 19 Wellenkarten; ein schadensorientierter Spieler hat
    // davon etwa 3× Kampfeswut (+75 % Schaden), 2× Kampfrhythmus (−20 % Abklingzeit), 1× Zähigkeit (+20 % Leben),
    // 1× Siebenmeilenstiefel, 1× Tiefer Manabrunnen – der Rest sind Buddy-/Wirtschaftskarten (in der Arena wirkungslos).
    // Dazu zwei erlernte Element-Zauber (Feuer, Eis). Keine Händlerkarten.
    public class DimensionFightBench : MonoBehaviour
    {
        public const string LogPrefix = "[DimFight]";
        public static string LastResult { get; private set; } = "";
        public static bool IsRunning => _instance != null;
        private static DimensionFightBench _instance;

        public static readonly string[] Loadout =
        {
            "PlayerStat", "PlayerStat", "PlayerStat", "PlayerCooldown", "PlayerCooldown", "PlayerHealth", "Player speed", "PlayerManaboost",
        };

        public float ReactionTime = 0.35f;   // so lange nach Erscheinen einer Warnfläche reagiert der „Spieler“ nicht
        public float TimeLimit = 240f;       // Spielzeit-Obergrenze (s)
        public float KiteNear = 6.5f, KiteFar = 9f; // Fernkämpfer halten diesen Abstand zum Boss (m)

        private string _args;
        private bool _dodge = true, _cards = true;
        private float _timeScale = 2f;
        private ChampionClass _champion = ChampionClass.Mage;

        private PlayerAvatar _avatar;
        private PlayerAbilities _pa;
        private PlayerStats _stats;
        private PlayerController _controller;
        private CharacterController _cc;
        private EnemyBrain _boss;
        private float _dealt, _taken, _lastHP, _lastMax, _minHP = float.MaxValue;
        private int _deaths, _hits;
        private bool _measuring;
        private float _strafeSign = 1f;

        private static readonly FieldInfo AimPointField = typeof(PlayerController).GetField("_aimPoint", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo HasAimPointField = typeof(PlayerController).GetField("_hasAimPoint", BindingFlags.NonPublic | BindingFlags.Instance);

        public static void Run(string args = "mage dodge")
        {
            if (!Application.isPlaying || _instance != null) { Debug.LogWarning($"{LogPrefix} Nur im Play Mode, nur ein Lauf."); return; }
            var go = new GameObject("DimensionFightBench");
            _instance = go.AddComponent<DimensionFightBench>();
            _instance._args = args ?? "";
            LastResult = "";
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { _instance = null; LastResult = ""; }

        void Start() => StartCoroutine(Routine());

        void OnDestroy()
        {
            EnemyBrain.OnDamageDealt -= HandleDamageDealt;
            PlayerAvatar.OnDowned -= HandleDowned;
            if (_stats != null) _stats.OnHealthChanged -= HandleHealth;
            if (_instance == this) _instance = null;
        }

        private void Parse()
        {
            foreach (var raw in _args.Split(new[] { ' ', ',' }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                string a = raw.ToLowerInvariant();
                switch (a)
                {
                    case "dodge": _dodge = true; break;
                    case "careless": _dodge = false; break;
                    case "nocards": _cards = false; break;
                    case "x1": _timeScale = 1f; break;
                    case "x2": _timeScale = 2f; break;
                    case "x3": _timeScale = 3f; break;
                    default:
                        foreach (ChampionClass c in System.Enum.GetValues(typeof(ChampionClass)))
                            if (c.ToString().ToLowerInvariant() == a) _champion = c;
                        break;
                }
            }
        }

        private IEnumerator Routine()
        {
            Parse();
            float wait = Time.realtimeSinceStartup + 40f;
            while ((PlayerAvatar.Local == null || WaveManager.Instance == null || DimensionEvent.Instance == null) && Time.realtimeSinceStartup < wait) yield return null;
            _avatar = PlayerAvatar.Local;
            if (_avatar == null || !Net.IsServer) { Finish("FEHLER: keine eigene Figur / kein Host"); yield break; }
            _pa = _avatar.Abilities != null ? _avatar.Abilities : PlayerAbilities.Instance;
            _stats = _avatar.Stats;
            _controller = _avatar.GetComponent<PlayerController>();
            _cc = _avatar.GetComponent<CharacterController>();

            // Champion
            var np = NetPlayer.Local;
            if (np != null) np.RequestChampion(_champion);
            if (_pa.ActiveKit == null || _pa.ActiveClass != _champion) _pa.SetChampion(_champion);
            for (int i = 0; i < 10 && _pa.ActiveClass != _champion; i++) yield return null;
            if (_pa.ActiveClass != _champion) { Finish($"FEHLER: Champion {_champion} nicht aktiv ({_pa.ActiveClass})"); yield break; }

            // Unverwundbarkeit aus (DevTools setzt sie sonst jedes Frame), Nexus geschützt (auf der Karte stehen keine Buddies)
            var dev = FindFirstObjectByType<DevTools>();
            if (dev != null) { dev.GodMode = false; dev.InvulnerableNexus = true; }
            _stats.GodMode = false;
            _stats.IsInvulnerable = false;
            if (Nexus.Instance != null) Nexus.Instance.Invulnerable = true;

            // Kartensatz + zwei Element-Zauber
            var um = UpgradeManager.Instance;
            int applied = 0;
            if (_cards && um != null)
            {
                foreach (string name in Loadout)
                {
                    int idx = um.AllUpgrades.FindIndex(u => u != null && u.name == name);
                    if (idx < 0) { Debug.LogWarning($"{LogPrefix} Karte {name} fehlt."); continue; }
                    um.ApplyPicked(Net.LocalClientId, idx);
                    applied++;
                }
                _pa.UnlockElementAbility(0);
                _pa.UnlockElementAbility(1);
            }
            yield return null;
            if (_stats.CurrentHP < _stats.MaxHP) _stats.Heal(_stats.MaxHP);

            // Event starten (Welle 20) und hinein
            if (!DimensionEvent.IsActive && !DimensionEvent.DevForce()) { Finish("FEHLER: DevForce fehlgeschlagen"); yield break; }
            var ev = DimensionEvent.Instance;
            wait = Time.realtimeSinceStartup + 20f;
            while (ev.Phase != DimensionPhase.Open && Time.realtimeSinceStartup < wait) yield return null;
            if (Cutscene.IsPlaying) Cutscene.Skip();
            Time.timeScale = _timeScale;
            DimensionEvent.DevEnter();
            wait = Time.realtimeSinceStartup + 20f;
            while (!DimensionEvent.LocalInArena && Time.realtimeSinceStartup < wait) yield return null;
            if (!DimensionEvent.LocalInArena) { Finish("FEHLER: nicht in der Arena angekommen"); yield break; }
            yield return new WaitForSeconds(0.5f);
            _boss = ev.FindBoss();
            if (_boss == null) { Finish("FEHLER: kein Boss"); yield break; }

            EnemyBrain.OnDamageDealt += HandleDamageDealt;
            PlayerAvatar.OnDowned += HandleDowned;
            _stats.OnHealthChanged += HandleHealth;
            _lastHP = _stats.CurrentHP;
            _lastMax = _stats.MaxHP;
            _measuring = true;
            float t0 = Time.time, bossMax = _boss.MaxHP, playerMax = _stats.MaxHP;
            float firstDeath = -1f, downtime = 0f;
            int wave = WaveManager.Instance.CurrentWaveIndex;
            float dmgMult = _boss.DamageMultiplier;
            Debug.Log($"{LogPrefix} Start: {_champion} {(_dodge ? "dodge" : "careless")} gegen {_boss.DisplayName} {bossMax:0} LP (Schaden ×{dmgMult:0.00}), Welle {wave}, Spieler {playerMax:0} LP, Karten {applied}, Schaden ×{_pa.DamageMultiplier:0.00}, Abklingzeit ×{_pa.CooldownMultiplier:0.00}");

            while (_boss != null && !_boss.IsDead && Time.time - t0 < TimeLimit)
            {
                if (GameManager.Instance != null && GameManager.Instance.IsGameOver) break;
                if (Time.timeScale > 0f && !Mathf.Approximately(Time.timeScale, _timeScale)) Time.timeScale = _timeScale;
                if (!_avatar.IsAlive)
                {
                    if (firstDeath < 0f) firstDeath = Time.time - t0;
                    downtime += Time.deltaTime;
                }
                else if (!DimensionEvent.LocalInArena)
                {
                    downtime += Time.deltaTime;
                    if (ev.Phase == DimensionPhase.Open && !_teleportPending) { _teleportPending = true; DimensionEvent.DevEnter(); StartCoroutine(ClearPending()); }
                }
                else Drive();
                yield return null;
            }
            _measuring = false;
            float seconds = Time.time - t0;
            bool killed = _boss == null || _boss.IsDead;
            var sb = new StringBuilder();
            sb.Append($"{LogPrefix} {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name} | {_champion} | {(_dodge ? "dodge" : "careless")} | ");
            sb.Append(killed ? $"Boss tot nach {seconds:0.0} s" : $"ABBRUCH nach {seconds:0.0} s (Boss {(_boss != null ? _boss.CurrentHP : 0f):0}/{bossMax:0})");
            sb.Append($" | Boss-LP {bossMax:0} | Champion-Schaden {_dealt:0} ({_dealt / Mathf.Max(0.1f, seconds - downtime):0.0} DPS in der Arena) | erlitten {_taken:0} in {_hits} Treffern ({_taken / Mathf.Max(0.1f, seconds - downtime):0.0}/s) | Max-LP {playerMax:0} | Min-LP {(_minHP == float.MaxValue ? playerMax : _minHP):0} | Tode {_deaths}" +
                      $"{(firstDeath >= 0f ? $" (erster nach {firstDeath:0.0} s)" : "")} | Ausfallzeit {downtime:0.0} s | Karten {applied} | Boss-Schaden ×{dmgMult:0.00} | Zeitfaktor {_timeScale}");
            sb.Append($" | in Warnfläche {_dangerTime:0.0} s (kein Ausweg {_noSpotTime:0.0}, gesperrt {_lockedTime:0.0}, verlangsamt {_slowedTime:0.0})");
            sb.Append(" | Treffer:");
            foreach (var kv in _bySource) sb.Append($" {kv.Key} {kv.Value.x:0}× {kv.Value.y:0}");
            Finish(sb.ToString());
        }

        private readonly Dictionary<string, Vector2> _bySource = new Dictionary<string, Vector2>(); // x = Treffer, y = Schaden
        private float _dangerTime, _noSpotTime, _lockedTime, _slowedTime;
        private bool _teleportPending;
        private IEnumerator ClearPending()
        {
            yield return new WaitForSecondsRealtime(2.5f);
            _teleportPending = false;
        }

        private void Finish(string line)
        {
            LastResult = line;
            Debug.Log(line);
            try
            {
                System.IO.Directory.CreateDirectory("Temp/DimFight");
                System.IO.File.AppendAllText("Temp/DimFight/results.txt", System.DateTime.Now.ToString("HH:mm:ss") + " " + line + "\n");
            }
            catch (System.Exception e) { Debug.LogWarning($"{LogPrefix} Ergebnisdatei: {e.Message}"); }
            Time.timeScale = 1f;
            Destroy(gameObject);
        }

        // ---------------- Messung ----------------

        private void HandleDamageDealt(EnemyBrain enemy, float dealt, bool fromPlayer)
        {
            if (_measuring && enemy == _boss && fromPlayer) _dealt += dealt;
        }

        private void HandleDowned(PlayerAvatar a)
        {
            if (_measuring && a == _avatar) _deaths++;
        }

        private void HandleHealth()
        {
            float hp = _stats.CurrentHP, max = _stats.MaxHP;
            if (_measuring && Mathf.Approximately(max, _lastMax) && hp < _lastHP - 0.01f)
            {
                _taken += _lastHP - hp;
                _hits++;
                // Quelle grob zuordnen: läuft gerade eine Fähigkeit (Einschlag), sonst Nahkampf bzw. Regen-Puls
                var bb = _boss != null ? _boss.GetComponent<BossBrain>() : null;
                string src = bb != null && bb.CurrentAbility != null ? bb.CurrentAbility.Name : "Hieb/Regen";
                _bySource.TryGetValue(src, out Vector2 acc);
                _bySource[src] = acc + new Vector2(1f, _lastHP - hp);
                _minHP = Mathf.Min(_minHP, hp);
            }
            _lastHP = hp;
            _lastMax = max;
        }

        // ---------------- Steuerung ----------------

        private static bool InDanger(Vector3 p, float minAge)
        {
            var list = AoeTelegraph.Active;
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                if (t == null || t.IsEnding || t.Age < minAge) continue;
                if (t.Shape.Contains(p, 0.7f)) return true;
            }
            return false;
        }

        private void Drive()
        {
            Vector3 me = _avatar.transform.position;
            Vector3 bp = _boss.transform.position;
            Vector3 toBoss = bp - me;
            toBoss.y = 0f;
            float dist = toBoss.magnitude;
            Vector3 dirBoss = dist > 0.01f ? toBoss / dist : _avatar.transform.forward;
            bool melee = _pa.ActiveKit is KnightKit || _pa.ActiveKit is BerserkerKit;
            Vector3 anchor = DimensionEvent.Instance.Arena != null ? DimensionEvent.Instance.Arena.Anchor.position : DimensionEventRules.ArenaCenter;

            // Wohin? 1) aus Warnflächen heraus, 2) Abstand halten (Fernkampf) bzw. heran (Nahkampf)
            Vector3 move = Vector3.zero;
            if (_dodge && InDanger(me, ReactionTime))
            {
                _dangerTime += Time.deltaTime;
                if (FindSafeSpot(me, anchor, out Vector3 safe)) move = safe - me;
                else _noSpotTime += Time.deltaTime;
                if (_controller != null && _controller.MovementLocked) _lockedTime += Time.deltaTime;
                if (_controller != null && _controller.IsSlowed) _slowedTime += Time.deltaTime;
            }
            else if (melee)
            {
                if (dist > 2.3f) move = dirBoss * (dist - 2.0f);
            }
            else if (_dodge)
            {
                if (dist < KiteNear)
                {
                    Vector3 away = -dirBoss;
                    Vector3 next = me + away * 2f;
                    Vector3 fromCenter = next - anchor;
                    fromCenter.y = 0f;
                    if (fromCenter.magnitude > 13.5f)
                    {
                        // am Rand: seitlich am Boss vorbei statt in die Mauer
                        Vector3 tangent = Vector3.Cross(Vector3.up, dirBoss) * _strafeSign;
                        Vector3 probe = me + tangent * 2f - anchor;
                        probe.y = 0f;
                        if (probe.magnitude > 14.5f) { _strafeSign = -_strafeSign; tangent = -tangent; }
                        Vector3 inward = anchor - me;
                        inward.y = 0f;
                        away = (tangent + inward.normalized * 0.5f).normalized;
                    }
                    move = away * 2f;
                }
                else if (dist > KiteFar) move = dirBoss * (dist - KiteFar + 1f);
                // nie in eine sichtbare Warnfläche hineinlaufen
                if (move.sqrMagnitude > 0.01f && InDanger(me + move.normalized * 1.2f, 0f)) move = Vector3.zero;
            }
            else if (dist > KiteFar) move = dirBoss * (dist - KiteFar + 1f);

            move.y = 0f;
            if (move.sqrMagnitude > 0.0004f && _cc != null && _cc.enabled && _controller != null && !_controller.MovementLocked)
            {
                float speed = _controller.MoveSpeed * _controller.SpeedMultiplier * _controller.SlowMultiplier;
                Vector3 step = Vector3.ClampMagnitude(move, speed * Time.deltaTime);
                _cc.Move(step);
            }

            // Zielen und angreifen
            if (_controller == null || !_controller.RotationLocked) _avatar.transform.rotation = Quaternion.LookRotation(dirBoss);
            if (AimPointField != null && _controller != null)
            {
                AimPointField.SetValue(_controller, bp);
                HasAimPointField.SetValue(_controller, true);
            }
            try
            {
                // Element-Zauber, wenn erlernt, bereit und in Reichweite (Feuer ≤ 7 m, Eis ≤ 5,5 m; Nahkampf ≤ 3,2 m)
                TrySlot(AbilitySlot.Fire, dist <= (melee ? 3.2f : 7f));
                TrySlot(AbilitySlot.Ice, dist <= (melee ? 3.2f : 5.5f));
                var primary = _pa.GetAbility(AbilitySlot.Primary);
                if ((!melee || dist <= 3.2f) && _pa.IsAvailable(primary)) _pa.TryCast(primary);
            }
            catch (System.Exception e) { Debug.LogError($"{LogPrefix} TryCast: {e}"); }
        }

        private void TrySlot(AbilitySlot slot, bool inRange)
        {
            if (!inRange) return;
            var id = _pa.GetAbility(slot);
            if (_pa.IsUnlocked(id) && _pa.IsAvailable(id)) _pa.TryCast(id);
        }

        // Nächster sicherer Punkt: 16 Richtungen × 5 Abstände, in der Arena, auf dem NavMesh, in keiner Warnfläche
        private static bool FindSafeSpot(Vector3 me, Vector3 anchor, out Vector3 best)
        {
            best = me;
            float bestCost = float.MaxValue;
            // Start auf das NavMesh legen (die Figur steht etwas darüber – NavMesh.Raycast meldet sonst immer ein Hindernis)
            Vector3 from = NavMesh.SamplePosition(me, out NavMeshHit start, 1.5f, NavMesh.AllAreas) ? start.position : me;
            float[] radii = { 1.5f, 3f, 4.5f, 6f, 8f, 10f };
            for (int r = 0; r < radii.Length; r++)
            {
                for (int i = 0; i < 16; i++)
                {
                    float a = i / 16f * Mathf.PI * 2f;
                    Vector3 p = me + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * radii[r];
                    Vector3 c = p - anchor;
                    c.y = 0f;
                    if (c.magnitude > 15f) continue;
                    if (InDanger(p, 0f)) continue;
                    p.y = from.y; // Figur-Pivot liegt über dem Boden
                    if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 0.6f, NavMesh.AllAreas)) continue;
                    if (NavMesh.Raycast(from, hit.position, out _, NavMesh.AllAreas)) continue;
                    float cost = radii[r];
                    if (cost < bestCost) { bestCost = cost; best = hit.position; }
                }
                if (bestCost < float.MaxValue) return true;
            }
            return false;
        }
    }
}
#endif
