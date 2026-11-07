using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace ElementalBuddies
{
    // Test-Fernsteuerung für Gerätetests (2026-11-02): Claude schickt per adb Befehle ans laufende Spiel und liest den
    // Zustand zurück – Ersatz für den Unity-MCP, der nur den Editor erreicht. Läuft NUR in Development-Builds im
    // Touch-Modus (PC- und Release-Builds bleiben unberührt).
    //   Befehle: adb shell "echo 'state' >> /sdcard/Android/data/<paket>/files/testcmd.txt" (eine Zeile je Befehl)
    //   Antworten: Log-Zeilen „[TestCtl] …“ (adb logcat -s Unity) und angehängt in files/testout.txt
    // Liste der Befehle: „help“. Unverwundbarkeit wirkt nur beim Host (Einzelspiel), wie die DevTools.
    public class MobileTestRemote : MonoBehaviour
    {
        private const string CmdFile = "testcmd.txt";
        private const string OutFile = "testout.txt";
        private const float PollInterval = 0.5f;

        private string _cmdPath, _outPath;
        private float _nextPoll;
        private bool _god;
        // hitch: Bilder über HitchMs melden (mit neu erschienenen Gegnern und Speicherbereinigungen seit dem Vorbild)
        private const float HitchMs = 100f;
        private bool _hitch;
        private readonly System.Collections.Generic.HashSet<EnemyBrain> _known = new System.Collections.Generic.HashSet<EnemyBrain>();
        private int _gc0;
        private float _hitchSince;
        private readonly FrameTiming[] _timings = new FrameTiming[1];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!Debug.isDebugBuild || !MobilePlatform.TouchMode) return;
            var go = new GameObject("MobileTestRemote");
            DontDestroyOnLoad(go);
            go.AddComponent<MobileTestRemote>();
        }

        void Awake()
        {
            _cmdPath = Path.Combine(Application.persistentDataPath, CmdFile);
            _outPath = Path.Combine(Application.persistentDataPath, OutFile);
            try { if (File.Exists(_cmdPath)) File.Delete(_cmdPath); } catch (Exception) { }
            Reply("bereit – Befehle in " + _cmdPath);
        }

        void Update()
        {
            if (_god) ApplyGod(true);
            if (_hitch) WatchHitches();
            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + PollInterval;

            string[] lines;
            try
            {
                if (!File.Exists(_cmdPath)) return;
                lines = File.ReadAllLines(_cmdPath);
                File.Delete(_cmdPath);
            }
            catch (Exception e)
            {
                Reply("Lesefehler: " + e.Message);
                return;
            }
            foreach (var raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                try { Execute(line); }
                catch (Exception e) { Reply($"Fehler bei „{line}“: {e.GetType().Name}: {e.Message}"); }
            }
        }

        private void Reply(string msg)
        {
            Debug.Log("[TestCtl] " + msg);
            try { File.AppendAllText(_outPath, DateTime.Now.ToString("HH:mm:ss") + " " + msg + "\n"); } catch (Exception) { }
        }

        private void Execute(string line)
        {
            var a = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string cmd = a[0].ToLowerInvariant();
            Reply("> " + line);
            switch (cmd)
            {
                case "help":
                    Reply("state | start [champion] [karte] | host [champion] [karte] | net | ready [off] | go | mem | wave | startwave N | kill | shards N | gold N | god on|off | " +
                          "draft | merchant [art] | buy i | reroll | done | skip | tp x z | tp nexus|shrine|merchant | " +
                          "timescale f | perf [s] | hitch on|off | probe | ad ok|fail|cancel|state | adreroll | adslot | adcontinue | adgiveup | nexusfall | adsuite | adconsent ask|reset");
                    break;
                case "state": Reply(State()); break;
                case "start": StartGame(a); break;
                // Koop-Test (2026-11-02): Online-Raum hosten, Netz-Zustand, Bereit, Spielstart für alle, Speicher
                case "host": HostRoom(a); break;
                case "net": Reply(NetState()); break;
                case "ready":
                    if (NetPlayer.Local == null) { Reply("kein NetPlayer"); break; }
                    NetPlayer.Local.RequestReady(a.Length < 2 || a[1] != "off");
                    Reply("bereit gesetzt");
                    break;
                case "go":
                    if (NetSession.Instance == null) { Reply("keine Sitzung"); break; }
                    NetSession.Instance.StartGame();
                    Reply("Spielstart für alle angefordert");
                    break;
                case "mem": Reply(MemState()); break;
                case "wave":
                    if (WaveManager.Instance == null) { Reply("keine Spielszene"); break; }
                    WaveManager.Instance.RequestStartWave();
                    Reply("Welle angefordert");
                    break;
                case "startwave":
                    if (WaveManager.Instance == null || a.Length < 2) { Reply("startwave N (nur vor der 1. Welle)"); break; }
                    WaveManager.Instance.DevSetStartWave(int.Parse(a[1]));
                    Reply("nächste Welle " + WaveManager.Instance.UpcomingWaveNumber);
                    break;
                case "kill":
                    if (WaveManager.Instance != null) WaveManager.Instance.DevKillAllEnemies();
                    Reply("alle Gegner getötet");
                    break;
                case "shards":
                    if (EconomyManager.Instance == null || a.Length < 2) { Reply("shards N (im Spiel)"); break; }
                    EconomyManager.Instance.AddShards(float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture));
                    Reply("Splitter " + Mathf.FloorToInt(EconomyManager.Instance.CurrentShards));
                    break;
                case "gold":
                    if (NetPlayer.Local == null || a.Length < 2) { Reply("gold N (im Spiel)"); break; }
                    NetPlayer.Local.AddGold(float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture));
                    Reply("Gold " + Mathf.FloorToInt(NetPlayer.Local.Gold.Value));
                    break;
                case "god":
                    _god = a.Length < 2 || a[1] != "off";
                    ApplyGod(_god);
                    Reply("Unverwundbar " + (_god ? "an" : "aus"));
                    break;
                case "draft":
                    if (UpgradeManager.Instance == null) { Reply("keine Spielszene"); break; }
                    UpgradeManager.Instance.PresentUpgrades();
                    StartCoroutine(ReplyStateLater());
                    break;
                case "merchant": OpenMerchant(a); break;
                case "buy":
                    if (a.Length < 2) { Reply("buy i (0-basiert)"); break; }
                    int slot = int.Parse(a[1]);
                    if (MerchantManager.IsLocalShopOpen) Reply("Händler kaufen: " + MerchantManager.Instance.BuyCard(slot));
                    else if (UpgradeManager.IsLocalChoosing) Reply("Karte nehmen: " + UpgradeManager.Instance.BuyCard(slot));
                    else { Reply("keine Kartenwahl offen"); break; }
                    StartCoroutine(ReplyStateLater());
                    break;
                case "reroll":
                    if (MerchantManager.IsLocalShopOpen) Reply("Händler neu würfeln: " + MerchantManager.Instance.Reroll());
                    else if (UpgradeManager.IsLocalChoosing) { UpgradeManager.Instance.RequestReroll(); Reply("Kartenwahl neu würfeln angefragt"); }
                    else { Reply("keine Kartenwahl offen"); break; }
                    StartCoroutine(ReplyStateLater());
                    break;
                case "done":
                    if (MerchantManager.IsLocalShopOpen) MerchantManager.Instance.CloseShop();
                    else if (UpgradeManager.IsLocalChoosing) UpgradeManager.Instance.FinishDraft();
                    else { Reply("keine Kartenwahl offen"); break; }
                    StartCoroutine(ReplyStateLater());
                    break;
                case "skip":
                    if (UpgradeManager.IsLocalChoosing) UpgradeManager.Instance.SkipDraft();
                    else { Reply("keine Kartenwahl offen"); break; }
                    StartCoroutine(ReplyStateLater());
                    break;
                case "tp": Teleport(a); break;
                case "timescale":
                    if (a.Length < 2) { Reply("timescale f"); break; }
                    Time.timeScale = float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture);
                    Reply("timeScale " + Time.timeScale);
                    break;
                case "hitch":
                    _hitch = a.Length < 2 || a[1] != "off";
                    _known.Clear();
                    foreach (var e in EnemyBrain.All) if (e != null) _known.Add(e);
                    _gc0 = GC.CollectionCount(0);
                    _hitchSince = Time.unscaledTime;
                    Reply("Hänger-Meldung " + (_hitch ? "an (> " + HitchMs + " ms)" : "aus"));
                    break;
                case "ad":
                    if (a.Length > 1 && a[1] != "state" && Enum.TryParse(a[1], true, out RewardedAds.TestVideo.Result res))
                        RewardedAds.TestVideo.NextResult = res;
                    Reply($"Werbung: Anbieter {RewardedAds.ProviderName}, Einwilligung {(AdConsent.Asked ? (AdConsent.Granted ? "ja" : "nein") : "offen")}, Testvideo-Ergebnis {RewardedAds.TestVideo.NextResult}, " +
                          $"übrig Neuwurf {RewardedAds.Remaining(AdReward.Reroll)} / Platz {RewardedAds.Remaining(AdReward.BuddySlot)} / Weiterspielen {RewardedAds.Remaining(AdReward.Continue)}, " +
                          $"jetzt anbietbar: {RewardedAds.CanOffer(AdReward.Reroll)}/{RewardedAds.CanOffer(AdReward.BuddySlot)}/{RewardedAds.CanOffer(AdReward.Continue)}");
                    break;
                case "adsuite":
                    Reply("LevelPlay-Test-Suite: " + LevelPlayAds.LaunchTestSuite());
                    break;
                case "adconsent":
                    if (a.Length > 1 && a[1] == "reset") { AdConsent.Reset(); Reply("Einwilligung vergessen – nächster Start fragt neu"); }
                    else { AdConsent.Ask(); Reply("Einwilligungs-Frage gezeigt"); }
                    break;
                case "adreroll":
                    Reply("Video Neuwurf: " + RewardedAds.OfferReroll());
                    StartCoroutine(ReplyStateLater(RewardedAds.TestVideo.Seconds + 0.6f));
                    break;
                case "adslot":
                    Reply("Video Buddy-Platz: " + RewardedAds.OfferBuddySlot());
                    StartCoroutine(ReplyStateLater(RewardedAds.TestVideo.Seconds + 0.6f));
                    break;
                case "adcontinue":
                    Reply("Video Weiterspielen: " + RewardedAds.OfferContinue());
                    StartCoroutine(ReplyStateLater(RewardedAds.TestVideo.Seconds + 0.6f));
                    break;
                case "adgiveup":
                    if (GameManager.Instance != null) GameManager.Instance.DeclineContinue();
                    StartCoroutine(ReplyStateLater());
                    break;
                case "nexusfall":
                    // Nexus sofort auf 0 (Unverwundbarkeit kurz aus) → Weiterspielen-Angebot bzw. Game Over
                    if (Nexus.Instance == null) { Reply("kein Nexus"); break; }
                    Nexus.Instance.Invulnerable = false;
                    Nexus.Instance.TakeDamage(Nexus.Instance.CurrentHP + 1f);
                    if (_god) Nexus.Instance.Invulnerable = true;
                    StartCoroutine(ReplyStateLater());
                    break;
                case "probe":
                    Reply(MobilePerfProbe.RunOnce() ? "Perf-Sonde läuft (~2 min, Ergebnisse als [PerfProbe] im Log)" : "Perf-Sonde läuft schon");
                    break;
                case "perf":
                    StartCoroutine(Perf(a.Length > 1 ? float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture) : 3f));
                    break;
                default: Reply("unbekannt – „help“ listet die Befehle"); break;
            }
        }

        // ---------------- Zustand ----------------

        private string State()
        {
            var sb = new StringBuilder();
            sb.Append("Szene ").Append(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            sb.Append(" | timeScale ").Append(Time.timeScale.ToString("0.##"));
            var wm = WaveManager.Instance;
            if (wm != null)
                sb.Append($" | Welle {wm.UpcomingWaveNumber}{(wm.IsWaveActive ? " läuft" : " wartet")}, Gegner {EnemyBrain.All.Count}");
            if (EconomyManager.Instance != null) sb.Append(" | Splitter ").Append(Mathf.FloorToInt(EconomyManager.Instance.CurrentShards));
            if (NetPlayer.Local != null) sb.Append(" | Gold ").Append(Mathf.FloorToInt(NetPlayer.Local.Gold.Value));
            var av = PlayerAvatar.Local;
            if (av != null)
            {
                var p = av.transform.position;
                sb.Append($" | Spieler ({p.x:0.0}, {p.y:0.0}, {p.z:0.0})");
                if (av.Stats != null) sb.Append($" HP {av.Stats.CurrentHP:0}/{av.Stats.MaxHP:0}");
            }
            if (GameManager.Instance != null && GameManager.Instance.IsGameOver) sb.Append(" | GAME OVER");
            if (GameManager.Instance != null && GameManager.Instance.IsContinueOffered) sb.Append(" | WEITERSPIELEN?");
            if (GameManager.Instance != null && GameManager.Instance.Continued) sb.Append(" | fortgesetzt");
            if (Nexus.Instance != null) sb.Append($" | Nexus {Nexus.Instance.CurrentHP:0}/{Nexus.Instance.MaxHP:0}");
            if (BuddySlotManager.Instance != null) sb.Append($" | Plätze {BuddySlotManager.Instance.MaxSlots}");
            if (RewardedAds.IsShowing) sb.Append(" | VIDEO");
            if (PauseManager.IsPaused) sb.Append(" | Pause");

            var um = UpgradeManager.Instance;
            if (um != null && um.IsChoosing)
            {
                sb.Append($"\n  Kartenwahl: Preis {um.OfferCardPrice}, Neuwurf {(um.OfferRerolls > 0 ? "frei(" + um.OfferRerolls + ")" : um.OfferRerollCost.ToString())}, " +
                          $"Splitter verfügbar {um.SpendableShards}, genommen {um.OfferPicks}");
                for (int i = 0; i < um.LocalOffer.Count; i++)
                {
                    var c = um.LocalOffer[i];
                    sb.Append($"\n   [{i}] {(c != null ? c.Title : "?")}{(um.IsSlotBought(i) ? " (gekauft)" : um.IsSlotAvailable(i) ? "" : " (nicht wählbar)")}");
                }
            }
            var mm = MerchantManager.Instance;
            if (mm != null && mm.IsChoosing)
            {
                sb.Append($"\n  Händler {(mm.OfferMerchant != null ? mm.OfferMerchant.name : "?")}: Preis {mm.NextCardPrice}, Neuwurf {mm.RerollPrice}, Gold {Mathf.FloorToInt(MerchantManager.LocalGold)}");
                for (int i = 0; i < mm.CurrentOffer.Count; i++)
                {
                    var c = mm.CurrentOffer[i];
                    var curse = mm.CurseOf(i);
                    sb.Append($"\n   [{i}] {(c != null ? c.Title : "?")}{(mm.IsBought(i) ? " (gekauft)" : "")}{(curse != MerchantCurse.None ? " (Fluch " + curse + ")" : "")}");
                }
            }
            return sb.ToString();
        }

        // Kauf/Neuwurf laufen über den (lokalen) Server – Zustand einen Moment später melden
        private IEnumerator ReplyStateLater(float delay = 0.3f)
        {
            yield return new WaitForSecondsRealtime(delay);
            Reply(State());
        }

        // ---------------- Aktionen ----------------

        // start [champion] [karte]: aus dem Hauptmenü ein Einzelspiel starten wie der „Spielen“-Knopf (MainMenuUI.LoadGame),
        // aber ohne Freischalt-Sperre (z. B. Walddorf vor dem ersten Morgengrauen) – gespeichert wird nur die Map-Wahl
        // host [champion] [karte]: Online-Raum (Relay) erstellen, Raumcode melden; Start danach mit „go“
        private async void HostRoom(string[] a)
        {
            for (int i = 1; i < a.Length; i++)
            {
                if (Enum.TryParse(a[i], true, out ChampionClass cls) && Enum.IsDefined(typeof(ChampionClass), cls)) { GameSession.SelectedChampion = cls; continue; }
                foreach (var m in MapRegistry.All)
                    if (m != null && (string.Equals(m.Id, a[i], StringComparison.OrdinalIgnoreCase) || string.Equals(m.DisplayName, a[i], StringComparison.OrdinalIgnoreCase)))
                        GameSession.MapId = m.Id;
            }
            var ns = NetSession.Instance;
            if (ns == null) { Reply("keine NetSession"); return; }
            string code = await ns.HostOnlineAsync();
            Reply(code != null ? "RAUMCODE " + code : "Raum erstellen fehlgeschlagen");
        }

        private string NetState()
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            var sb = new StringBuilder();
            sb.Append("Modus ").Append(NetSession.Instance?.Mode).Append(" | Code ").Append(NetSession.Instance?.RoomCode ?? "-");
            sb.Append(" | sleepTimeout ").Append(Screen.sleepTimeout == SleepTimeout.NeverSleep ? "nie" : Screen.sleepTimeout.ToString());
            if (nm == null) return sb.Append(" | kein NetworkManager").ToString();
            sb.Append($" | Listening {nm.IsListening} Server {nm.IsServer} Clients {nm.ConnectedClientsIds.Count}");
            var utp = nm.NetworkConfig.NetworkTransport;
            if (utp is Unity.Netcode.Transports.UTP.UnityTransport ut && ut.GetNetworkDriver().IsCreated)
            {
                var cs = ut.GetNetworkDriver().CurrentSettings;
                sb.Append(" | reconnect ").Append(Unity.Networking.Transport.CommonNetworkParametersExtensions.GetNetworkConfigParameters(ref cs).reconnectionTimeoutMS).Append(" ms");
            }
            if (nm.IsServer)
                foreach (var id in nm.ConnectedClientsIds)
                    if (id != nm.LocalClientId) sb.Append($" | RTT {id}: {utp.GetCurrentRtt(id)} ms");
            if (!nm.IsServer && nm.IsConnectedClient) sb.Append($" | RTT Host: {utp.GetCurrentRtt(Unity.Netcode.NetworkManager.ServerClientId)} ms");
            foreach (var p in NetPlayer.All) sb.Append($" | {p.DisplayName} {p.ChampionClass} bereit={p.Ready.Value}");
            return sb.ToString();
        }

        private static string MemState()
        {
            const float MB = 1024f * 1024f;
            return $"Speicher: Unity belegt {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / MB:0} MB, " +
                   $"reserviert {UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / MB:0} MB, " +
                   $"Mono {UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / MB:0}/{UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong() / MB:0} MB, " +
                   $"Grafik {UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver() / MB:0} MB, Gegner {EnemyBrain.All.Count}";
        }

        private void StartGame(string[] a)
        {
            if (FindFirstObjectByType<MainMenuUI>() == null) { Reply("nicht im Hauptmenü"); return; }
            for (int i = 1; i < a.Length; i++)
            {
                if (Enum.TryParse(a[i], true, out ChampionClass cls) && Enum.IsDefined(typeof(ChampionClass), cls))
                {
                    GameSession.SelectedChampion = cls;
                    continue;
                }
                MapDefinitionSO found = null;
                foreach (var m in MapRegistry.All)
                    if (m != null && (string.Equals(m.Id, a[i], StringComparison.OrdinalIgnoreCase)
                        || string.Equals(m.DisplayName, a[i], StringComparison.OrdinalIgnoreCase))) found = m;
                if (found != null) GameSession.MapId = found.Id;
                else Reply("unbekannt: " + a[i] + " (Champions: " + string.Join(",", Enum.GetNames(typeof(ChampionClass))) + ")");
            }
            var ns = NetSession.Instance;
            if (ns != null && ns.StartSolo()) ns.StartGame();
            else UnityEngine.SceneManagement.SceneManager.LoadScene(GameSession.GameScene);
            Reply($"Start: {GameSession.SelectedChampion} auf {GameSession.Map.DisplayName}");
        }

        // merchant [art]: Händler aktivieren und sofort einnehmen (wie DevTools F7 + F8) → Laden öffnet sich
        private void OpenMerchant(string[] a)
        {
            var mm = MerchantManager.Instance;
            if (mm == null) { Reply("keine Spielszene"); return; }
            Merchant m = mm.ActiveMerchant != null && mm.ActiveMerchant.IsActive ? mm.ActiveMerchant : null;
            if (m == null)
            {
                if (a.Length > 1 && Enum.TryParse(a[1], true, out MerchantKind kind))
                {
                    m = mm.GetMerchant(kind);
                    if (m != null && !mm.Activate(m)) m = null;
                }
                else m = mm.ActivateNext();
            }
            if (m == null) { Reply("kein Händler verfügbar (Arten: " + string.Join(",", Enum.GetNames(typeof(MerchantKind))) + ")"); return; }
            m.Complete();
            StartCoroutine(ReplyStateLater());
        }

        // tp x z | tp nexus | tp shrine | tp merchant – Höhe vom Terrain
        private void Teleport(string[] a)
        {
            var av = PlayerAvatar.Local;
            if (av == null) { Reply("keine Spielfigur"); return; }
            Vector3 target;
            if (a.Length >= 3)
                target = new Vector3(float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture), 0f,
                                     float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture));
            else if (a.Length == 2 && a[1] == "nexus" && Nexus.Instance != null) target = Nexus.Instance.transform.position + new Vector3(0f, 0f, -6f);
            else if (a.Length == 2 && a[1] == "shrine")
            {
                var sm = ShrineManager.Instance;
                Shrine s = sm != null ? (sm.ActiveShrine != null ? sm.ActiveShrine : sm.GetNextScheduled(out _)) : null;
                if (s == null) { var all = FindObjectsByType<Shrine>(FindObjectsSortMode.None); s = all.Length > 0 ? all[0] : null; }
                if (s == null) { Reply("kein Schrein"); return; }
                target = s.CenterPosition + new Vector3(3f, 0f, 0f);
            }
            else if (a.Length == 2 && a[1] == "merchant")
            {
                var mm = MerchantManager.Instance;
                var m = mm != null ? mm.ActiveMerchant : null;
                if (m == null) { Reply("kein aktiver Händler"); return; }
                target = m.transform.position + new Vector3(3f, 0f, 0f);
            }
            else { Reply("tp x z | tp nexus | tp shrine | tp merchant"); return; }

            var t = Terrain.activeTerrain;
            target.y = t != null ? t.SampleHeight(target) + t.transform.position.y + 0.5f : target.y + 1f;
            av.TeleportLocal(target, av.transform.rotation);
            Reply($"Teleport nach ({target.x:0.0}, {target.y:0.0}, {target.z:0.0})");
        }

        private static void ApplyGod(bool on)
        {
            if (!Net.IsServer) return;
            foreach (var av in PlayerAvatar.All)
                if (av != null && av.Stats != null) av.Stats.GodMode = on;
            if (Nexus.Instance != null) Nexus.Instance.Invulnerable = on;
        }

        private void WatchHitches()
        {
            var fresh = new StringBuilder();
            int freshCount = 0;
            foreach (var e in EnemyBrain.All)
            {
                if (e == null || !_known.Add(e)) continue;
                if (freshCount++ < 6) fresh.Append(e.Config != null ? e.Config.name : e.name).Append(' ');
            }
            int gc = GC.CollectionCount(0);
            float ms = Time.unscaledDeltaTime * 1000f;
            if (ms > HitchMs && Time.unscaledTime - _hitchSince > 1f)
                Reply($"Hänger {ms:0} ms | Gegner {EnemyBrain.All.Count}, neu {freshCount}: {fresh}| GC {gc - _gc0}");
            _gc0 = gc;
            _known.RemoveWhere(e => e == null);
        }

        // perf [s]: mittlere Bildzeit sowie CPU/GPU-Zeit (FrameTimingManager) über s Sekunden
        private IEnumerator Perf(float seconds)
        {
            int frames = 0;
            double cpu = 0, gpu = 0;
            int timed = 0;
            float worst = 0f;
            float start = Time.unscaledTime;
            while (Time.unscaledTime - start < seconds)
            {
                yield return null;
                frames++;
                worst = Mathf.Max(worst, Time.unscaledDeltaTime);
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, _timings) > 0)
                {
                    cpu += _timings[0].cpuFrameTime;
                    gpu += _timings[0].gpuFrameTime;
                    timed++;
                }
            }
            float elapsed = Time.unscaledTime - start;
            float ms = elapsed * 1000f / Mathf.Max(1, frames);
            Reply($"Perf {seconds:0.#} s: {ms:0.0} ms ({frames / elapsed:0} fps), schlechtestes Bild {worst * 1000f:0} ms" +
                  (timed > 0 ? $", CPU {cpu / timed:0.0} ms, GPU {gpu / timed:0.0} ms" : "") + $", Gegner {EnemyBrain.All.Count}");
        }
    }
}
