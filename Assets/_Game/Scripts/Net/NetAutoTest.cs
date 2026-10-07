using System.Reflection;
using System.Text;
using UnityEngine;

namespace ElementalBuddies
{
    // Automatisierter Mehrspieler-Test (nur mit Startparameter -nettest aktiv):
    // protokolliert alle 2 s den Spielzustand aus Sicht dieses Rechners ("[NetTest] …"), meldet sich zur Welle bereit,
    // wählt die erste angebotene Karte, schließt den Händlerladen, läuft im Kreis und wirkt die Primärfähigkeit.
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
                var f = typeof(UpgradeManager).GetField("_localOffer", BindingFlags.Instance | BindingFlags.NonPublic);
                var offer = f != null ? f.GetValue(um) as System.Collections.IList : null;
                if (offer != null && offer.Count > 0)
                {
                    var card = offer[0] as UpgradeDefinitionSO;
                    Debug.Log("[NetTest] wähle Karte " + (card != null ? card.name : "?"));
                    um.SelectUpgrade(card);
                }
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
            if (!_built && gm != null && gm.CurrentState == GameState.Building && NetBootstrap.Instance != null && PlayerAvatar.Local != null)
            {
                _built = true;
                Vector3 c = NetBootstrap.Instance.transform.position;
                Vector3 p = new Vector3(Mathf.Round(c.x) + (Net.IsClientOnly ? -3f : 3f), c.y, Mathf.Round(c.z) - 5f);
                Debug.Log("[NetTest] baue Feuer-Buddy bei " + p.ToString("F1") + " (Buddies vorher " + ElementalBuddy.ActiveCount + ")");
                NetGame.RequestBuild(0, p);
                return;
            }
            if (wm != null && gm != null && gm.CurrentState == GameState.Building && !wm.IsLocalReady)
            {
                Debug.Log("[NetTest] melde bereit");
                wm.RequestStartWave();
            }

            // Figur bewegen (Besitzer-autoritativ → wird synchronisiert) und Primärfähigkeit wirken
            var me = PlayerAvatar.Local;
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
            if (nm != null && nm.IsListening) sb.Append(" rtt=").Append(nm.IsServer ? 0 : nm.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManagerServerId()));
            return sb.ToString();
        }

        private static ulong NetworkManagerServerId() => Unity.Netcode.NetworkManager.ServerClientId;
    }
}
