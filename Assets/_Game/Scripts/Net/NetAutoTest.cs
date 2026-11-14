using System.Reflection;
using System.Text;
using UnityEngine;

namespace ElementalBuddies
{
    // Automatisierter Mehrspieler-Test (nur mit Startparameter -nettest aktiv):
    // protokolliert alle 2 s den Spielzustand aus Sicht dieses Rechners ("[NetTest] …"), meldet sich zur Welle bereit,
    // nimmt im Kartenladen die Gratis-Karte und kauft (wenn der Anteil reicht) eine zweite, schließt den Händlerladen, läuft im Kreis und wirkt die Primärfähigkeit.
    public class NetAutoTest : MonoBehaviour
    {
        private static bool? _enabled;
        public static bool Enabled
        {
            get
            {
                if (_enabled == null) _enabled = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-nettest") >= 0;
                return _enabled.Value;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!Enabled) return;
            var go = new GameObject("NetAutoTest");
            DontDestroyOnLoad(go);
            go.AddComponent<NetAutoTest>();
        }

        private static readonly bool NoBuild = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-nettestbuy") >= 0;
        // -nettestdim: bei einem Dimensionsriss selbst zum nächsten begehbaren Portal springen (der echte Auslöser
        // schickt die Figur dann hinein) und nach dem Boss-Tod zum Rückportal
        private static readonly bool AutoDimension = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-nettestdim") >= 0;
        // -nettestwave N (nur Host, Editor/Development-Build): vor der ersten Welle auf Welle N springen (z. B. 20 = Dimensionsriss)
        private static readonly int StartWave = ReadIntArg("-nettestwave");
        private bool _startWaveSet;
        private bool _rotationLocked;
        private static int ReadIntArg(string name)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int n) ? n : 0;
        }
        private float _nextDimJump;
        private float _nextLog;
        private float _nextAct;
        private float _angle;
        private bool _built;
        private static readonly MethodInfo TryCastMethod =
            typeof(PlayerAbilities).GetMethod("TryCast", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        void Update()
        {
            if (Time.unscaledTime >= _nextLog)
            {
                _nextLog = Time.unscaledTime + 2f;
                Debug.Log(Status());
            }
            if (Time.unscaledTime >= _nextAct)
            {
                _nextAct = Time.unscaledTime + 0.5f;
                Act();
            }
        }

        private void Act()
        {
            var um = UpgradeManager.Instance;
            if (UpgradeManager.IsLocalChoosing && um != null)
            {
                // Kartenladen: Gratis-Karte nehmen, dann (falls der eigene Anteil reicht) eine Karte dazukaufen, dann „Fertig“
                if (um.IsRequestPending) return;
                var offer = um.LocalOffer;
                if (um.OfferPicks < 2)
                {
                    for (int i = 0; i < offer.Count; i++)
                    {
                        if (!um.IsSlotAvailable(i)) continue;
                        if (um.OfferPicks == 0 || um.CanAffordCard)
                        {
                            Debug.Log($"[NetTest] Kartenladen: {(um.OfferPicks == 0 ? "nehme Gratis-Karte" : "kaufe Karte")} {offer[i].name} " +
                                      $"(Preis {um.OfferCardPrice}, Anteil {um.SpendableShards}, Kasse {(EconomyManager.Instance != null ? EconomyManager.Instance.CurrentShards : 0f):F0})");
                            um.BuyCard(i);
                            return;
                        }
                        break;
                    }
                }
                Debug.Log($"[NetTest] Kartenladen fertig: {um.OfferPicks} Karte(n), Rest-Anteil {um.SpendableShards}");
                um.FinishDraft();
                return;
            }
            if (MerchantManager.IsLocalShopOpen)
            {
                Debug.Log("[NetTest] schließe Laden");
                MerchantManager.Instance.CloseShop();
                return;
            }
            var wm = WaveManager.Instance;
            var gm = GameManager.Instance;
            // Einmal pro Spiel einen Feuer-Buddy über den Server-RPC bauen (prüft Client → Server-Bauweg)
            // -nettestbuy: keinen Buddy bauen, damit nach Welle 1 genug Splitter für einen Kauf im Kartenladen da sind
            if (!_built && NoBuild) _built = true;
            if (!_built && gm != null && gm.CurrentState == GameState.Building && NetBootstrap.Instance != null && PlayerAvatar.Local != null)
            {
                _built = true;
                Vector3 c = NetBootstrap.Instance.transform.position;
                Vector3 p = new Vector3(Mathf.Round(c.x) + (Net.IsClientOnly ? -3f : 3f), c.y, Mathf.Round(c.z) - 5f);
                Debug.Log("[NetTest] baue Feuer-Buddy bei " + p.ToString("F1") + " (Buddies vorher " + ElementalBuddy.ActiveCount + ")");
                NetGame.RequestBuild(0, p);
                return;
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!_startWaveSet && StartWave > 1 && wm != null && Net.IsServer && Net.IsRunning && !wm.IsWaveActive && wm.CurrentWaveIndex == 0)
            {
                _startWaveSet = true;
                wm.DevSetStartWave(StartWave);
                if (SiegeProgression.Instance != null) SiegeProgression.Instance.ApplyInstant(wm.CurrentWaveIndex);
                Debug.Log("[NetTest] Startwelle " + wm.UpcomingWaveNumber);
                return;
            }
#endif
            if (wm != null && gm != null && gm.CurrentState == GameState.Building && !wm.IsLocalReady)
            {
                Debug.Log("[NetTest] melde bereit");
                wm.RequestStartWave();
            }

            // Figur bewegen (Besitzer-autoritativ → wird synchronisiert) und Primärfähigkeit wirken
            var me = PlayerAvatar.Local;
            if (AutoDimension && me != null && me.IsAlive && DimensionEvent.IsActive)
            {
                var ev = DimensionEvent.Instance;
                if (Time.unscaledTime >= _nextDimJump)
                {
                    if (ev.Phase == DimensionPhase.Open && !DimensionEvent.LocalInArena)
                    {
                        var portal = DimensionEvent.NearestEnterablePortal(me.transform.position);
                        if (portal != null)
                        {
                            _nextDimJump = Time.unscaledTime + 4f;
                            Debug.Log("[NetTest] Dimensionsriss: springe zum Portal " + portal.DisplayName);
                            me.TeleportLocal(portal.SpawnTransform.position + new Vector3((me.OwnerClientId % 4) * 1.1f - 0.55f, 1.1f, 0f), me.transform.rotation);
                        }
                    }
                    else if (ev.Phase == DimensionPhase.BossDead && DimensionEvent.LocalInArena && ev.Arena != null)
                    {
                        _nextDimJump = Time.unscaledTime + 4f;
                        Debug.Log("[NetTest] Dimensionsriss: springe zum Rückportal");
                        me.TeleportLocal(ev.Arena.ReturnTriggerPosition + new Vector3((me.OwnerClientId % 4) * 1.1f - 0.55f, 1.1f, 0f), me.transform.rotation);
                    }
                }
                // Betreten braucht Stillstand im Portal-Kreis (EnterHold): nach dem Sprung nicht um den Nexus kreisen
                if (ev.Phase == DimensionPhase.Open && !DimensionEvent.LocalInArena && Time.unscaledTime < _nextDimJump) return;
                // in der Arena nicht um den Nexus kreisen (die Figur würde aus der Arena gezogen) – nur angreifen
                if (DimensionEvent.LocalInArena)
                {
                    // zum Boss drehen und auf Nahkampf-Abstand herangehen (Rückstöße schieben die Figur sonst an den
                    // Rand, wo die Primärfähigkeit ins Leere geht) – die Maus-Drehung ist solange gesperrt
                    var boss = ev.FindBoss();
                    if (boss != null)
                    {
                        Vector3 d = boss.transform.position - me.transform.position;
                        d.y = 0f;
                        if (me.Controller != null) { me.Controller.RotationLocked = true; _rotationLocked = true; }
                        if (d.sqrMagnitude > 0.01f) me.transform.rotation = Quaternion.LookRotation(d);
                        var body = me.GetComponent<CharacterController>();
                        if (body != null && body.enabled && d.magnitude > 3f) body.Move(d.normalized * Mathf.Min(1.5f, d.magnitude - 2.5f));
                    }
                    if (TryCastMethod != null && me.Abilities != null)
                    {
                        var primary = me.Abilities.GetAbility(default(AbilitySlot));
                        try { TryCastMethod.Invoke(me.Abilities, new object[] { primary }); }
                        catch (System.Exception e) { Debug.LogWarning("[NetTest] Cast: " + e.InnerException?.Message); }
                    }
                    return;
                }
            }
            if (_rotationLocked && me != null && !DimensionEvent.LocalInArena)
            {
                _rotationLocked = false;
                if (me.Controller != null) me.Controller.RotationLocked = false;
            }
            if (me != null && me.IsAlive && gm != null && gm.CurrentState == GameState.Combat && NetBootstrap.Instance != null)
            {
                _angle += 0.6f;
                Vector3 c = NetBootstrap.Instance.transform.position;
                Vector3 target = c + new Vector3(Mathf.Cos(_angle), 0f, Mathf.Sin(_angle)) * 4f;
                var cc = me.GetComponent<CharacterController>();
                if (cc != null && cc.enabled) cc.Move(target - me.transform.position);
                if (TryCastMethod != null && me.Abilities != null)
                {
                    var primary = me.Abilities.GetAbility(default(AbilitySlot));
                    try { TryCastMethod.Invoke(me.Abilities, new object[] { primary }); }
                    catch (System.Exception e) { Debug.LogWarning("[NetTest] Cast: " + e.InnerException?.Message); }
                }
            }
        }

        public static string Status()
        {
            var sb = new StringBuilder("[NetTest] ");
            var nm = Net.Manager;
            sb.Append(Net.IsClientOnly ? "CLIENT" : (Net.IsRunning ? "HOST" : "OFF"));
            sb.Append(" scene=").Append(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            sb.Append(" players=").Append(NetPlayer.All.Count);
            sb.Append(" avatars=").Append(PlayerAvatar.All.Count);
            foreach (var a in PlayerAvatar.All)
            {
                if (a == null) continue;
                sb.Append(" [").Append(a.OwnerClientId).Append(a.IsLocal ? "*" : "").Append(' ').Append(a.Champion)
                  .Append(" pos=").Append(a.transform.position.ToString("F1"))
                  .Append(" hp=").Append(a.Stats != null ? a.Stats.CurrentHP.ToString("F0") : "?").Append(']');
            }
            // Gruppenanzeige (Porträt je Mitspieler-Zeile, Spieltest 2026-10-17 #4)
            var team = FindFirstObjectByType<TeamHUD>();
            if (team != null) sb.Append(" team=[").Append(team.DebugRows()).Append(']');
            var wm = WaveManager.Instance;
            var gm = GameManager.Instance;
            if (gm != null) sb.Append(" state=").Append(gm.CurrentState);
            if (wm != null) sb.Append(" wave=").Append(wm.CurrentWaveIndex).Append(" left=").Append(wm.EnemiesRemaining)
                              .Append(" ready=").Append(wm.ReadyCount).Append('/').Append(wm.ReadyNeeded)
                              .Append(" wait='").Append(wm.WaitReason).Append('\'');
            sb.Append(" enemies=").Append(FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None).Length);
            sb.Append(" buddies=").Append(ElementalBuddy.ActiveCount);
            if (EconomyManager.Instance != null) sb.Append(" shards=").Append(EconomyManager.Instance.CurrentShards.ToString("F0"));
            if (NetPlayer.Local != null) sb.Append(" gold=").Append(NetPlayer.Local.Gold.Value.ToString("F0"));
            if (PlayerMana.Local != null) sb.Append(" mana=").Append(PlayerMana.Local.CurrentMana.ToString("F0"));
            if (Nexus.Instance != null) sb.Append(" nexus=").Append(Nexus.Instance.CurrentHP.ToString("F0"));
            sb.Append(" drops=").Append(FindObjectsByType<ShardPickup>(FindObjectsSortMode.None).Length);
            sb.Append(' ').Append(DimensionEvent.DebugState());
            if (nm != null && nm.IsListening) sb.Append(" rtt=").Append(nm.IsServer ? 0 : nm.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManagerServerId()));
            return sb.ToString();
        }

        private static ulong NetworkManagerServerId() => Unity.Netcode.NetworkManager.ServerClientId;
    }
}
