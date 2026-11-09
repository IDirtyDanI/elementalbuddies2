using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

namespace ElementalBuddies
{
    // Balancing-Messwerkzeug (nur Play Mode, Einzelspiel/Host): vergleicht die fünf Champions in festen Szenarien.
    // Aufruf: ChampionBench.Run("all") – oder Menü BuddyTD → Balancing → Champion-Bench (Play Mode).
    //
    // Szenarien (je Champion):
    //   single  – 1 Skelett-Ritter (Rüstung 0,5) 2 m vor dem Spieler, nur Grundangriff (LMB), 10 s
    //   line    – 5 Läufer in einer Reihe (1 m Abstand, ab 2 m vor dem Spieler, Lane-Lage), LMB, 8 s
    //   cluster – 5 Läufer in einer 1,5-m-Scheibe 2 m vor dem Spieler, LMB, 8 s
    //   tank    – 4 Läufer + 1 Ritter mit Welle-10-Werten rund um den Spieler (≤ 3 m), kein Angriff, 10 s erlittener Schaden
    //
    // Weitere Schlüsselwörter im scenarios-String (Komma/Leerzeichen getrennt, Groß/klein egal):
    //   mage/knight/archer/berserker/shaman – nur diese Champions (sonst alle)
    //   free     – Gegner in den DPS-Szenarien nicht festhalten (sie laufen dann auf den Spieler zu)
    //   infmana  – Mana jedes Frame auffüllen (sonst nur zu Beginn jedes Laufs; Wut startet bei ihrem Startwert)
    //   x1       – Zeitfaktor 1 statt 2
    //
    // Ergebnis: eine Log-Zeile „[ChampionBench] …“ pro (Champion, Szenario) und eine Markdown-Tabelle in LastReport.
    // „Schaden“ = roher Champion-Schaden nach Gegner-Rüstung (EnemyBrain.OnDamageDealt mit Champion-Quelle);
    // „alle Quellen“ zählt zusätzlich DoT-Ticks/Buddy-Schaden an den Bench-Gegnern. Händlerkarten/Boni der Runde zählen mit.
    public class ChampionBench : MonoBehaviour
    {
        public const string LogPrefix = "[ChampionBench]";

        // Letzter vollständiger Bericht (Markdown-Tabelle); leer, solange nichts gelaufen ist
        public static string LastReport { get; private set; } = "";
        public static bool IsRunning => _instance != null;

        private static ChampionBench _instance;

        // ---------------- Einstellungen ----------------

        private const float DpsHpMultiplier = 10000f;   // Gegner-HP in den DPS-Szenarien (Läufer 60 → 600 000)
        private const float PlayerHpBonus = 100000f;    // Puffer, damit der Spieler nie fällt (wird danach wieder abgezogen)
        private const int TankWave = 10;
        private const float SetupTimeout = 30f;         // Echtzeit-Sekunden für das Warten auf Spieler/WaveManager
        private const string ConfigDir = "Assets/ScriptableObjects/Configs/";

        private class Scenario
        {
            public string Id;
            public float Duration;
            public bool Attack;
            public bool Hold;
        }

        private class Row
        {
            public ChampionClass Champion;
            public string Scenario;
            public float Seconds;
            public float Dealt, DealtAll, Taken, MaxHP, Drift;
            public int Targets, Enemies;
            public string Note = "";
            public float Dps => Seconds > 0.01f ? Dealt / Seconds : 0f;
        }

        // ---------------- Einstieg ----------------

        public static void Run(string scenarios = "all")
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning($"{LogPrefix} Nur im Play Mode.");
                return;
            }
            if (_instance != null)
            {
                Debug.LogWarning($"{LogPrefix} Läuft bereits.");
                return;
            }
            var go = new GameObject("ChampionBench");
            var bench = go.AddComponent<ChampionBench>();
            _instance = bench;
            bench._args = scenarios ?? "all";
            bench.StartCoroutine(bench.Routine());
        }

        // ---------------- Zustand ----------------

        private string _args;
        private PlayerAvatar _avatar;
        private PlayerAbilities _pa;
        private PlayerStats _stats;
        private PlayerMana _mana;
        private PlayerController _controller;
        private WaveManager _wm;
        private DevTools _dev;

        private ChampionClass _origChampion;
        private Vector3 _origPos;
        private Quaternion _origRot;
        private float _origTimeScale = 1f;
        private bool _origNexusInvuln, _origDevGod, _origDevNexus, _origControllerEnabled, _origGodMode;
        private bool _saved, _restored, _hpBonusApplied;

        private float _timeScale = 2f;
        private bool _hold = true, _infMana;

        private Vector3 _spot, _forward = Vector3.forward;

        private readonly List<EnemyBrain> _bench = new List<EnemyBrain>();
        private readonly HashSet<EnemyBrain> _benchSet = new HashSet<EnemyBrain>();
        private readonly HashSet<EnemyBrain> _hitTargets = new HashSet<EnemyBrain>();
        private readonly List<Vector3> _anchors = new List<Vector3>();
        private bool _measuring;
        private float _dealt, _dealtAll, _taken, _lastHP, _lastMax;

        private readonly List<Row> _rows = new List<Row>();

        // Wachhund: Wirft eine verschachtelte Coroutine, läuft die äußere nie weiter (Unity) – dann nach
        // WatchdogTimeout Echtzeit-Sekunden ohne Lebenszeichen abbrechen und alles zurücksetzen (OnDestroy)
        private const float WatchdogTimeout = 30f;
        private float _heartbeat;

        private static readonly FieldInfo AimPointField = typeof(PlayerController).GetField("_aimPoint", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo HasAimPointField = typeof(PlayerController).GetField("_hasAimPoint", BindingFlags.NonPublic | BindingFlags.Instance);

        // ---------------- Ablauf ----------------

        private IEnumerator Routine()
        {
            _heartbeat = Time.unscaledTime;
            try
            {
                // Warten auf Spielfigur und WaveManager (Netz-Spawn kann ein paar Frames dauern)
                float until = Time.unscaledTime + SetupTimeout;
                while (Time.unscaledTime < until && (FindAbilities() == null || WaveManager.Instance == null)) { _heartbeat = Time.unscaledTime; yield return null; }
                _pa = FindAbilities();
                _wm = WaveManager.Instance;
                if (_pa == null || _wm == null)
                {
                    Debug.LogError($"{LogPrefix} Kein Spieler oder kein WaveManager gefunden – Abbruch.");
                    yield break;
                }
                _avatar = _pa.Avatar != null ? _pa.Avatar : PlayerAvatar.Local;
                _stats = _pa.GetComponent<PlayerStats>();
                _mana = _pa.Mana;
                _controller = _pa.GetComponent<PlayerController>();
                if (_stats == null || _controller == null)
                {
                    Debug.LogError($"{LogPrefix} PlayerStats/PlayerController fehlt – Abbruch.");
                    yield break;
                }
                if (!Net.IsServer)
                {
                    Debug.LogError($"{LogPrefix} Nur als Host/Einzelspieler (Server) möglich – Abbruch.");
                    yield break;
                }
                if (_wm.IsWaveActive)
                {
                    Debug.LogError($"{LogPrefix} Welle läuft – bitte zwischen zwei Wellen starten. Abbruch.");
                    yield break;
                }

                // Zeit darf nicht stehen (Draft, Pause)
                until = Time.unscaledTime + SetupTimeout;
                while (Time.unscaledTime < until && (Time.timeScale <= 0f || PauseManager.IsPaused)) { _heartbeat = Time.unscaledTime; yield return null; }
                if (Time.timeScale <= 0f || PauseManager.IsPaused)
                {
                    Debug.LogError($"{LogPrefix} Spielzeit steht (Pause/Auswahl offen) – Abbruch.");
                    yield break;
                }

                var scenarios = ParseArgs(_args, out var champions);
                if (scenarios.Count == 0 || champions.Count == 0)
                {
                    Debug.LogError($"{LogPrefix} Keine gültigen Szenarien/Champions in \"{_args}\". Erlaubt: all, single, line, cluster, tank, mage, knight, archer, berserker, free, infmana, x1.");
                    yield break;
                }

                EnemyConfigSO runnerCfg = FindConfig("Runner");
                EnemyConfigSO knightCfg = FindConfig("Knight");
                if (runnerCfg == null || knightCfg == null)
                {
                    Debug.LogError($"{LogPrefix} EnemyConfig Runner/Knight nicht gefunden – Abbruch.");
                    yield break;
                }

                SaveState();
                ApplyBenchState();
                if (!FindOpenSpot(out _spot, out _forward))
                {
                    Debug.LogError($"{LogPrefix} Kein freier NavMesh-Platz gefunden – Abbruch.");
                    yield break;
                }
                Debug.Log($"{LogPrefix} Start: Szenarien [{string.Join(", ", scenarios.ConvertAll(s => s.Id))}], Champions [{string.Join(", ", champions)}], " +
                          $"Zeitfaktor {_timeScale}, Festhalten {(_hold ? "an" : "aus")}, Platz {_spot}, Blickrichtung {_forward}.");

                EnemyBrain.OnDamageDealt += HandleDamageDealt;
                _stats.OnHealthChanged += HandleHealthChanged;

                foreach (var cls in champions)
                {
                    _heartbeat = Time.unscaledTime;
                    SwitchChampion(cls);
                    yield return new WaitForSecondsRealtime(0.3f);
                    if (_pa.ActiveClass != cls)
                    {
                        Debug.LogError($"{LogPrefix} Champion-Wechsel zu {cls} fehlgeschlagen (aktiv: {_pa.ActiveClass}) – übersprungen.");
                        continue;
                    }
                    foreach (var sc in scenarios)
                        yield return RunScenario(cls, sc, runnerCfg, knightCfg);
                }

                BuildReport();
            }
            finally
            {
                RestoreState();
                Destroy(gameObject);
            }
        }

        private IEnumerator RunScenario(ChampionClass cls, Scenario sc, EnemyConfigSO runnerCfg, EnemyConfigSO knightCfg)
        {
            _heartbeat = Time.unscaledTime;
            var row = new Row { Champion = cls, Scenario = sc.Id };
            Time.timeScale = _timeScale;

            // Spieler an den Messplatz, Ressource zurücksetzen, LP-Puffer
            Teleport(_spot, Quaternion.LookRotation(_forward));
            ResetResource();
            if (_stats.IsDead) ReviveNow();
            row.MaxHP = _stats.MaxHP;
            _stats.AddMaxHPBonus(PlayerHpBonus);
            _hpBonusApplied = true;
            _stats.Heal(_stats.MaxHP);

            // Gegner aufstellen
            SpawnFor(sc, runnerCfg, knightCfg);
            row.Enemies = _bench.Count;
            if (_bench.Count == 0)
            {
                row.Note = "keine Gegner gespawnt";
                Debug.LogError($"{LogPrefix} {cls}/{sc.Id}: keine Gegner gespawnt.");
                CleanupScenario();
                _rows.Add(row);
                yield break;
            }
            HoldEnemies(sc);
            yield return new WaitForSeconds(0.4f); // Spawn/Start der Gegner abwarten
            HoldEnemies(sc);

            // Messung
            _dealt = _dealtAll = _taken = 0f;
            _hitTargets.Clear();
            _lastHP = _stats.CurrentHP;
            _lastMax = _stats.MaxHP;
            _measuring = true;
            float start = Time.time;
            float realLimit = Time.unscaledTime + sc.Duration / Mathf.Max(0.1f, _timeScale) * 3f + 10f;
            float nextHold = 0f;
            while (Time.time - start < sc.Duration)
            {
                _heartbeat = Time.unscaledTime;
                if (Time.unscaledTime > realLimit)
                {
                    row.Note = "Zeitlimit (Spielzeit stand?)";
                    Debug.LogError($"{LogPrefix} {cls}/{sc.Id}: Zeitlimit erreicht – Spielzeit stand vermutlich still.");
                    break;
                }
                if (_pa == null || _stats == null) break;
                if (AliveCount() == 0) { row.Note = "alle Gegner tot"; break; }
                if (sc.Hold && Time.time >= nextHold) { HoldEnemies(sc); nextHold = Time.time + 0.5f; }
                if (_infMana && _mana != null && !_mana.HasProfile) _mana.Refill();
                if (sc.Attack) TryAttack();
                yield return null;
            }
            _measuring = false;
            row.Seconds = Time.time - start;
            row.Dealt = _dealt;
            row.DealtAll = _dealtAll;
            row.Taken = _taken;
            row.Targets = _hitTargets.Count;
            row.Drift = AverageDrift();
            if (_stats != null && _stats.IsDead) row.Note += (row.Note.Length > 0 ? "; " : "") + "Spieler gefallen";

            CleanupScenario();
            _rows.Add(row);
            Debug.Log(FormatLine(row));
            yield return new WaitForSeconds(0.6f); // fliegende Projektile/Effekte auslaufen lassen
        }

        // ---------------- Gegner ----------------

        private void SpawnFor(Scenario sc, EnemyConfigSO runnerCfg, EnemyConfigSO knightCfg)
        {
            _bench.Clear();
            _benchSet.Clear();
            _anchors.Clear();
            Vector3 f = _forward;
            Vector3 r = Vector3.Cross(Vector3.up, f).normalized;
            switch (sc.Id)
            {
                case "single":
                    Spawn(knightCfg, _spot + f * 2f, DpsHpMultiplier, -1f);
                    break;
                case "line":
                    for (int i = 0; i < 5; i++) Spawn(runnerCfg, _spot + f * (2f + i), DpsHpMultiplier, -1f);
                    break;
                case "cluster":
                {
                    Vector3 c = _spot + f * 2f;
                    Spawn(runnerCfg, c, DpsHpMultiplier, -1f);
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
                        Spawn(runnerCfg, c + (f * Mathf.Cos(a) + r * Mathf.Sin(a)) * 0.75f, DpsHpMultiplier, -1f);
                    }
                    break;
                }
                case "tank":
                {
                    float dmg = _wm.DamageMultiplier(TankWave);
                    for (int i = 0; i < 5; i++)
                    {
                        float a = i * Mathf.PI * 2f / 5f;
                        Vector3 p = _spot + (f * Mathf.Cos(a) + r * Mathf.Sin(a)) * 2f;
                        var cfg = i == 0 ? knightCfg : runnerCfg;
                        Spawn(cfg, p, _wm.HpMultiplierFor(cfg, TankWave), dmg);
                    }
                    break;
                }
            }
        }

        // damageMult < 0: Schadensfaktor der kommenden Welle (wie SpawnEnemyAt) beibehalten
        private void Spawn(EnemyConfigSO cfg, Vector3 pos, float hpMult, float damageMult)
        {
            try
            {
                if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 1.5f, NavMesh.AllAreas)) pos = hit.position;
                var e = _wm.SpawnEnemyAt(cfg, pos, hpMult, false);
                if (e == null) return;
                if (damageMult >= 0f) e.Initialize(hpMult, damageMult); // HP + Schaden auf die gewünschte Welle
                _bench.Add(e);
                _benchSet.Add(e);
                _anchors.Add(pos);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LogPrefix} Spawn fehlgeschlagen: {ex}");
            }
        }

        // DPS-Szenarien: Gegner festhalten (Betäubung, greifen dann auch nicht an), damit die Aufstellung erhalten bleibt
        private void HoldEnemies(Scenario sc)
        {
            if (!sc.Hold) return;
            foreach (var e in _bench)
                if (e != null && !e.IsDead) e.Stun(1.5f);
        }

        private int AliveCount()
        {
            int n = 0;
            foreach (var e in _bench) if (e != null && !e.IsDead) n++;
            return n;
        }

        private EnemyBrain NearestAlive(Vector3 from)
        {
            EnemyBrain best = null;
            float bestD = float.MaxValue;
            foreach (var e in _bench)
            {
                if (e == null || e.IsDead) continue;
                Vector3 d = e.transform.position - from;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq < bestD) { bestD = sq; best = e; }
            }
            return best;
        }

        private float AverageDrift()
        {
            float sum = 0f;
            int n = 0;
            for (int i = 0; i < _bench.Count && i < _anchors.Count; i++)
            {
                var e = _bench[i];
                if (e == null) continue;
                Vector3 d = e.transform.position - _anchors[i];
                d.y = 0f;
                sum += d.magnitude;
                n++;
            }
            return n > 0 ? sum / n : 0f;
        }

        // Bench-Gegner ohne Belohnung entfernen (kein Kill, keine Beute, keine Statistik)
        private void CleanupScenario()
        {
            _measuring = false;
            foreach (var e in _bench)
            {
                try { if (e != null && !e.IsDead) e.ServerBanish(); }
                catch (Exception ex) { Debug.LogError($"{LogPrefix} Entfernen fehlgeschlagen: {ex}"); }
            }
            _bench.Clear();
            _benchSet.Clear();
            _anchors.Clear();

            if (_hpBonusApplied && _stats != null)
            {
                _hpBonusApplied = false;
                _stats.AddMaxHPBonus(-PlayerHpBonus);
            }
            if (_stats != null)
            {
                if (_stats.IsDead) ReviveNow();
                _stats.Heal(_stats.MaxHP);
            }
        }

        // ---------------- Spieler ----------------

        private static PlayerAbilities FindAbilities()
        {
            var local = PlayerAvatar.Local;
            if (local != null && local.Abilities != null) return local.Abilities;
            return PlayerAbilities.Instance;
        }

        // Grundangriff Richtung nächster Gegner: Drehung und Zielpunkt direkt vor dem Wirken setzen
        private void TryAttack()
        {
            var target = NearestAlive(_pa.transform.position);
            if (target == null) return;
            Vector3 tp = target.transform.position;
            Vector3 d = tp - _pa.transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > 0.0001f) _pa.transform.rotation = Quaternion.LookRotation(d.normalized);
            // Nahkämpfer laufen dem Ziel hinterher wie ein Spieler (Rückstoß schiebt festgehaltene Gegner sonst aus der
            // Reichweite, PlayerController ist während der Messung aus) – Lauftempo 6 m/s bis 1,6 m Abstand
            bool melee = _pa.ActiveKit is KnightKit || _pa.ActiveKit is BerserkerKit;
            if (melee && d.magnitude > 1.6f)
            {
                var cc = _pa.GetComponent<CharacterController>();
                if (cc != null && cc.enabled) cc.Move(d.normalized * Mathf.Min(6f * Time.deltaTime, d.magnitude - 1.6f));
            }
            SetAimPoint(tp);
            try
            {
                var id = _pa.GetAbility(AbilitySlot.Primary);
                if (_pa.IsAvailable(id)) _pa.TryCast(id);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LogPrefix} TryCast fehlgeschlagen: {ex}");
            }
        }

        private void SetAimPoint(Vector3 point)
        {
            if (_controller == null || AimPointField == null || HasAimPointField == null) return;
            AimPointField.SetValue(_controller, point);
            HasAimPointField.SetValue(_controller, true);
        }

        private void Teleport(Vector3 pos, Quaternion rot)
        {
            if (_avatar != null) _avatar.TeleportLocal(pos, rot);
            else if (_pa != null)
            {
                var cc = _pa.GetComponent<CharacterController>();
                bool on = cc != null && cc.enabled;
                if (on) cc.enabled = false;
                _pa.transform.SetPositionAndRotation(pos, rot);
                if (on) cc.enabled = true;
            }
        }

        // Mana: voll; Wut (Ressourcen-Profil): auf den Startwert, sonst liefe der Berserker sofort in Raserei
        private void ResetResource()
        {
            if (_mana == null) return;
            if (_mana.HasProfile)
            {
                float excess = _mana.CurrentMana - _mana.Profile.StartValue;
                if (excess > 0f) _mana.TrySpend(excess);
                else if (excess < 0f) _mana.Add(-excess);
            }
            else _mana.Refill();
        }

        private void ReviveNow()
        {
            if (_avatar != null && _avatar.IsSpawned) _avatar.ServerRevive();
            else if (_stats != null) _stats.Revive();
        }

        // Im Netz über die Champion-Wahl des NetPlayers (sonst setzt PlayerAvatar die Wahl zurück), zusätzlich direkt
        private void SwitchChampion(ChampionClass cls)
        {
            try
            {
                var np = NetPlayer.Local;
                if (np != null) np.RequestChampion(cls);
                if (_pa != null && (_pa.ActiveKit == null || _pa.ActiveClass != cls)) _pa.SetChampion(cls);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LogPrefix} Champion-Wechsel zu {cls}: {ex}");
            }
        }

        // ---------------- Messung (Events) ----------------

        private void HandleDamageDealt(EnemyBrain enemy, float dealt, bool fromPlayer)
        {
            if (!_measuring || enemy == null || !_benchSet.Contains(enemy)) return;
            _dealtAll += dealt;
            if (!fromPlayer) return;
            _dealt += dealt;
            if (dealt > 0f) _hitTargets.Add(enemy);
        }

        private void HandleHealthChanged()
        {
            if (_stats == null) return;
            float hp = _stats.CurrentHP, max = _stats.MaxHP;
            if (_measuring && Mathf.Approximately(max, _lastMax) && hp < _lastHP) _taken += _lastHP - hp;
            _lastHP = hp;
            _lastMax = max;
        }

        // ---------------- Sichern / Wiederherstellen ----------------

        private void SaveState()
        {
            _origChampion = _pa.ActiveClass;
            _origPos = _pa.transform.position;
            _origRot = _pa.transform.rotation;
            _origTimeScale = Time.timeScale;
            _origControllerEnabled = _controller.enabled;
            _origGodMode = _stats.GodMode;
            if (Nexus.Instance != null) _origNexusInvuln = Nexus.Instance.Invulnerable;
            _dev = FindFirstObjectByType<DevTools>();
            if (_dev != null)
            {
                _origDevGod = _dev.GodMode;
                _origDevNexus = _dev.InvulnerableNexus;
            }
            _saved = true;
        }

        private void ApplyBenchState()
        {
            // DevTools setzt GodMode/Nexus jedes Frame – dort umstellen, sonst überschreibt es unsere Werte.
            // GodMode aus: sonst würde der Tank-Test jeden Treffer verschlucken (inkl. Rüstung/Block).
            if (_dev != null)
            {
                _dev.GodMode = false;
                _dev.InvulnerableNexus = true;
            }
            _stats.GodMode = false;
            if (Nexus.Instance != null) Nexus.Instance.Invulnerable = true;
            // Steuerung aus: keine Maus-Drehung/-Eingabe dazwischen, die Bench zielt selbst (Eingabe-Asset wird mit abgeschaltet)
            _controller.enabled = false;
        }

        private void RestoreState()
        {
            if (_restored) return;
            _restored = true;
            EnemyBrain.OnDamageDealt -= HandleDamageDealt;
            if (_stats != null) _stats.OnHealthChanged -= HandleHealthChanged;
            if (!_saved) return;
            try
            {
                CleanupScenario();
                if (_pa != null)
                {
                    SwitchChampion(_origChampion);
                    Teleport(_origPos, _origRot);
                }
                if (_controller != null) _controller.enabled = _origControllerEnabled;
                if (_stats != null) _stats.GodMode = _origGodMode;
                if (_dev != null)
                {
                    _dev.GodMode = _origDevGod;
                    _dev.InvulnerableNexus = _origDevNexus;
                }
                if (Nexus.Instance != null) Nexus.Instance.Invulnerable = _origNexusInvuln;
                ResetResource();
                Time.timeScale = _origTimeScale > 0f ? _origTimeScale : 1f;
                Debug.Log($"{LogPrefix} Zustand wiederhergestellt (Champion {_origChampion}).");
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LogPrefix} Wiederherstellen fehlgeschlagen: {ex}");
            }
        }

        void Update()
        {
            if (Time.unscaledTime - _heartbeat < WatchdogTimeout) return;
            Debug.LogError($"{LogPrefix} Keine Rückmeldung seit {WatchdogTimeout:0} s (Ausnahme in der Messung?) – Abbruch, Zustand wird zurückgesetzt.");
            _heartbeat = float.MaxValue;
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            RestoreState();
            if (_instance == this) _instance = null;
        }

        // ---------------- Platzwahl ----------------

        // Offener NavMesh-Platz: weit von NavMesh-Kanten (Gebäuden) und außerhalb der Reichweite von Buddies
        // (deren Schaden/Flüche würden die Messung verfälschen). Blickrichtung = längste freie Richtung.
        private bool FindOpenSpot(out Vector3 spot, out Vector3 forward)
        {
            spot = _pa.transform.position;
            forward = Flat(_pa.transform.forward);
            var candidates = new List<Vector3> { _pa.transform.position };
            if (NetBootstrap.Instance != null)
            {
                NetBootstrap.Instance.GetSpawnPose(0, out Vector3 sp, out _);
                candidates.Add(sp);
            }
            Vector3 center = Nexus.Instance != null ? Nexus.Instance.transform.position : _pa.transform.position;
            for (float radius = 8f; radius <= 40f; radius += 4f)
                for (int i = 0; i < 16; i++)
                {
                    float a = i * Mathf.PI * 2f / 16f;
                    candidates.Add(center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius);
                }

            var buddies = FindObjectsByType<ElementalBuddy>(FindObjectsSortMode.None);
            float bestScore = float.MinValue;
            bool found = false;
            foreach (var c in candidates)
            {
                if (!NavMesh.SamplePosition(c, out NavMeshHit hit, 3f, NavMesh.AllAreas)) continue;
                Vector3 p = hit.position;
                float edge = NavMesh.FindClosestEdge(p, out NavMeshHit edgeHit, NavMesh.AllAreas) ? edgeHit.distance : 0f;
                float buddyClear = 30f;
                foreach (var b in buddies)
                {
                    if (b == null) continue;
                    float range = 0f;
                    try { range = b.EffectiveRange; } catch { }
                    buddyClear = Mathf.Min(buddyClear, Vector3.Distance(Flat3(b.transform.position), Flat3(p)) - range - 3f);
                }
                float nexusDist = Nexus.Instance != null ? Vector3.Distance(Flat3(Nexus.Instance.transform.position), Flat3(p)) : 20f;
                Vector3 dir = BestDirection(p, out float free);
                // Wichtig: Raum nach vorn (Reihe bis 6 m), Kantenabstand, keine Buddies; Nexus nicht direkt daneben
                float score = Mathf.Min(free, 8f) * 2f + Mathf.Min(edge, 6f) + Mathf.Clamp(buddyClear, -20f, 10f) * 1.5f + Mathf.Min(nexusDist, 8f) * 0.25f;
                if (score > bestScore)
                {
                    bestScore = score;
                    spot = p;
                    forward = dir;
                    found = true;
                }
            }
            return found;
        }

        private static Vector3 BestDirection(Vector3 p, out float free)
        {
            Vector3 best = Vector3.forward;
            free = -1f;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 2f / 8f;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float dist = NavMesh.Raycast(p, p + d * 8f, out NavMeshHit hit, NavMesh.AllAreas) ? hit.distance : 8f;
                if (dist > free) { free = dist; best = d; }
            }
            return best;
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
        }

        private static Vector3 Flat3(Vector3 v) => new Vector3(v.x, 0f, v.z);

        // ---------------- Konfiguration ----------------

        private EnemyConfigSO FindConfig(string assetName)
        {
            EnemyConfigSO cfg = null;
#if UNITY_EDITOR
            cfg = UnityEditor.AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(ConfigDir + assetName + ".asset");
            if (cfg != null) return cfg;
#endif
            // Rückfall: aus den Wellen des WaveManagers bzw. allen geladenen Configs
            if (_wm != null && _wm.Waves != null)
                foreach (var w in _wm.Waves)
                {
                    if (w == null || w.EnemiesToSpawn == null) continue;
                    foreach (var g in w.EnemiesToSpawn)
                        if (g != null && g.EnemyType != null && g.EnemyType.name == assetName) return g.EnemyType;
                }
            if (_wm != null && _wm.ExtraGroups != null)
                foreach (var g in _wm.ExtraGroups)
                    if (g != null && g.Config != null && g.Config.name == assetName) return g.Config;
            foreach (var c in Resources.FindObjectsOfTypeAll<EnemyConfigSO>())
                if (c != null && c.name == assetName) return c;
            return null;
        }

        private List<Scenario> ParseArgs(string args, out List<ChampionClass> champions)
        {
            var all = new List<Scenario>
            {
                new Scenario { Id = "single", Duration = 10f, Attack = true, Hold = true },
                new Scenario { Id = "line", Duration = 8f, Attack = true, Hold = true },
                new Scenario { Id = "cluster", Duration = 8f, Attack = true, Hold = true },
                new Scenario { Id = "tank", Duration = 30f, Attack = false, Hold = false }, // 30 s: Treffer-Zeitpunkte streuen stark
            };
            var result = new List<Scenario>();
            champions = new List<ChampionClass>();
            bool any = false;
            foreach (var raw in args.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = raw.Trim().ToLowerInvariant();
                if (t == "all") { any = true; continue; }
                if (t == "free") { _hold = false; continue; }
                if (t == "infmana") { _infMana = true; continue; }
                if (t == "x1") { _timeScale = 1f; continue; }
                if (t == "x2") { _timeScale = 2f; continue; }
                var sc = all.Find(s => s.Id == t);
                if (sc != null) { if (!result.Contains(sc)) result.Add(sc); continue; }
                if (Enum.TryParse(t, true, out ChampionClass cls) && Enum.IsDefined(typeof(ChampionClass), cls))
                {
                    if (!champions.Contains(cls)) champions.Add(cls);
                    continue;
                }
                Debug.LogWarning($"{LogPrefix} Unbekanntes Schlüsselwort \"{raw}\" ignoriert.");
            }
            // „all“ oder nur Optionen/Champions angegeben → alle Szenarien
            if (result.Count == 0 || any) { result.Clear(); result.AddRange(all); }
            if (champions.Count == 0)
                foreach (ChampionClass c in Enum.GetValues(typeof(ChampionClass))) champions.Add(c);
            if (!_hold) foreach (var s in result) s.Hold = false;
            return result;
        }

        // ---------------- Bericht ----------------

        private static string FormatLine(Row r)
        {
            return $"{LogPrefix} {r.Champion} | {r.Scenario} | {r.Seconds:0.0} s | Schaden {r.Dealt:0} | DPS {r.Dps:0.0} | Ziele {r.Targets}/{r.Enemies} | " +
                   $"alle Quellen {r.DealtAll:0} | erlitten {r.Taken:0} ({(r.Seconds > 0.01f ? r.Taken / r.Seconds : 0f):0.0}/s) | MaxHP {r.MaxHP:0} | Drift {r.Drift:0.00} m" +
                   (string.IsNullOrEmpty(r.Note) ? "" : $" | {r.Note}");
        }

        private void BuildReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Champion-Bench ({DateTime.Now:yyyy-MM-dd HH:mm}) – Zeitfaktor {_timeScale}, Festhalten {(_hold ? "an" : "aus")}, " +
                          $"Mana {(_infMana ? "unbegrenzt" : "nur Start voll")}, DPS-Gegner-HP ×{DpsHpMultiplier:0}, Tank: Welle {TankWave} (Schaden ×{_wm.DamageMultiplier(TankWave):0.00})");
            sb.AppendLine();
            sb.AppendLine("| Champion | Szenario | s | Schaden | DPS | Ziele | alle Quellen | erlitten | erlitten/s | MaxHP | Drift m | Hinweis |");
            sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|");
            foreach (var r in _rows)
            {
                float tps = r.Seconds > 0.01f ? r.Taken / r.Seconds : 0f;
                sb.AppendLine($"| {r.Champion} | {r.Scenario} | {r.Seconds:0.0} | {r.Dealt:0} | {r.Dps:0.0} | {r.Targets}/{r.Enemies} | {r.DealtAll:0} | {r.Taken:0} | {tps:0.0} | {r.MaxHP:0} | {r.Drift:0.00} | {r.Note} |");
            }
            LastReport = sb.ToString();
            Debug.Log($"{LogPrefix} Fertig ({_rows.Count} Zeilen).\n{LastReport}");
        }
    }
}
