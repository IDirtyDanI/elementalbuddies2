using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ElementalBuddies
{
    // Dev-Modus zum Testen: Startwelle überspringen, unbegrenzte Ressourcen, Unverwundbarkeit, Hotkeys.
    // Liegt auf dem Managers-Objekt; im Inspector mit "Enabled" an-/ausschalten.
    // Mehrspieler: Cheats und Hotkeys nur beim Host (Net.IsServer); Clients erfahren über NetGame, ob Cheats aktiv sind.
    public class DevTools : MonoBehaviour
    {
        [Header("Dev-Modus")]
        public bool Enabled = true;
        [Tooltip("Welle, mit der das Spiel startet (1 = normal).")]
        public int StartWave = 4;
        public bool UnlimitedResources = true;
        public bool InvulnerableNexus = true;
        public bool GodMode = true;
        public bool UnlimitedBuddies = true;
        public bool UnlockAllSpells = false;
        [Tooltip("Champion beim Start erzwingen (statt Hauptmenü-Wahl). F6 wechselt im Spiel durch.")]
        public bool ForceChampion = false;
        public ChampionClass Champion = ChampionClass.Knight;
        [Tooltip("F7 öffnet immer diesen Händler (statt Beutel); wirkt auch auf die geplanten Händler-Wellen.")]
        public bool ForceMerchant = false;
        public MerchantKind ForcedMerchant = MerchantKind.Waffen;
        [Tooltip("Pro übersprungene 3 Wellen einen Buddy-Slot geben (wie im normalen Spiel).")]
        public bool GrantSkippedSlots = true;
        public bool ShowHelp = true;
        [Tooltip("F9 spawnt reihum den nächsten Boss am nächstgelegenen offenen Portal.")]
        public System.Collections.Generic.List<EnemyConfigSO> DevBossConfigs = new System.Collections.Generic.List<EnemyConfigSO>();
        private int _bossCursor;

        [Header("Erfolge")]
        [Tooltip("Erfolge auch vergeben, wenn Cheats aktiv sind (nur zum Testen).")]
        public bool AllowAchievementsWithCheats = false;
        [Tooltip("Im Spiel alle Meta-Freischaltungen (Champions, Fusionen, Stufe 4, Super-Elementare) aktiv – nur diese Session, nichts wird gespeichert.")]
        public bool UnlockAllContent = false;

        // Irgendein spielverändernder Cheat aktiv? (sperrt die Vergabe von Erfolgen, siehe AchievementManager)
        public bool CheatsActive => Enabled && (GodMode || UnlimitedResources || UnlimitedBuddies || InvulnerableNexus || UnlockAllSpells || UnlockAllContent || StartWave > 1);

        private const float ShardReserve = 9999f;
        private GUIStyle _box, _label;

        void Awake()
        {
#if !UNITY_EDITOR
            // Im Spieler-Build nie aktiv – unabhängig von der Inspector-Einstellung
            Enabled = false;
            enabled = false;
#endif
        }

        // Nur der Host (bzw. Einzelspieler) darf Cheats nutzen
        private bool Active => Enabled && Net.IsServer;

        IEnumerator Start()
        {
            if (!Active) yield break;
            yield return null; // nach allen Manager-Starts

            var wm = WaveManager.Instance;
            if (wm != null && StartWave > 1)
            {
                wm.DevSetStartWave(StartWave);
                // Belagerung (Brände, Zäune, Tageszeit) sofort auf den Stand der Startwelle bringen
                if (SiegeProgression.Instance != null) SiegeProgression.Instance.ApplyInstant(wm.CurrentWaveIndex);
                if (GrantSkippedSlots && BuddySlotManager.Instance != null)
                {
                    int every = EconomyManager.Instance != null && EconomyManager.Instance.Settings != null ? Mathf.Max(1, EconomyManager.Instance.Settings.SlotEveryNWaves) : 3;
                    int slots = (StartWave - 1) / every;
                    if (slots > 0) BuddySlotManager.Instance.AddSlot(slots);
                }
                var hud = FindFirstObjectByType<HUDManager>();
                if (hud != null) hud.RefreshWave();
            }

            if (ForceChampion) DevSetChampion(Champion);

            if (ForceMerchant && MerchantManager.Instance != null)
            {
                MerchantManager.Instance.ForceKind = true;
                MerchantManager.Instance.ForcedKind = ForcedMerchant;
            }

            if (UnlockAllSpells && PlayerAbilities.Instance != null)
                for (int i = 0; i < 4; i++) PlayerAbilities.Instance.UnlockElementAbility(i);

            Debug.Log($"DevTools: aktiv – Start in Welle {StartWave}.");
        }

        void Update()
        {
            Progression.SessionUnlockAllOverride = Active && UnlockAllContent;
            if (!Active) return;
            if (NetGame.Ready) NetGame.Instance.ServerSetHostCheats(CheatsActive);

            ApplyCheats();

            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.f1Key.wasPressedThisFrame) ShowHelp = !ShowHelp;
            if (kb.f2Key.wasPressedThisFrame && WaveManager.Instance != null) WaveManager.Instance.DevKillAllEnemies();
            if (kb.f3Key.wasPressedThisFrame && PlayerAbilities.Instance != null)
                for (int i = 0; i < 4; i++) PlayerAbilities.Instance.UnlockElementAbility(i);
            if (kb.f4Key.wasPressedThisFrame && ShrineManager.Instance != null) ShrineManager.Instance.DevAwakenNext();
            if (kb.f5Key.wasPressedThisFrame && BuddySlotManager.Instance != null) BuddySlotManager.Instance.AddSlot(1);
            if (kb.f6Key.wasPressedThisFrame && PlayerAbilities.Instance != null)
            {
                var pa = PlayerAbilities.Instance;
                var next = (ChampionClass)(((int)pa.ActiveClass + 1) % 3);
                DevSetChampion(next);
                ToastUI.Show("Champion: " + PauseMenuUI.ChampionName(next));
            }
            if (kb.f7Key.wasPressedThisFrame) DevActivateMerchant(kb.shiftKey.isPressed);
            if (kb.f8Key.wasPressedThisFrame) DevCaptureMerchant();
            if (kb.f9Key.wasPressedThisFrame) DevSpawnBoss();
        }

        void OnDestroy()
        {
            Progression.SessionUnlockAllOverride = false;
        }

        // F7: nächsten Händler sofort öffnen (Beutel bzw. ForceMerchant); Shift+F7: nächsten Händlertyp erzwingen
        public void DevActivateMerchant(bool cycleKind)
        {
            var mm = MerchantManager.Instance;
            if (mm == null) return;
            if (cycleKind)
            {
                ForcedMerchant = (MerchantKind)(((int)ForcedMerchant + 1) % MerchantInfo.Count);
                ForceMerchant = true;
            }
            Merchant m = null;
            if (ForceMerchant)
            {
                m = mm.GetMerchant(ForcedMerchant);
                if (m != null && !mm.Activate(m)) m = null;
            }
            else m = mm.ActivateNext();
            if (m == null) ToastUI.Show("DEV: kein Händler verfügbar");
        }

        // F9: nächsten Boss aus DevBossConfigs am nächsten offenen Portal (zum Spieler) spawnen, mit Wellen-HP-Bonus
        public EnemyBrain DevSpawnBoss()
        {
            var wm = WaveManager.Instance;
            if (wm == null || DevBossConfigs == null || DevBossConfigs.Count == 0) { ToastUI.Show("DEV: keine Boss-Configs"); return null; }
            EnemyConfigSO config = null;
            for (int k = 0; k < DevBossConfigs.Count && config == null; k++)
            {
                var c = DevBossConfigs[(_bossCursor + k) % DevBossConfigs.Count];
                if (c != null && c.Prefab != null) { config = c; _bossCursor = (_bossCursor + k + 1) % DevBossConfigs.Count; }
            }
            if (config == null) { ToastUI.Show("DEV: Boss-Config ohne Prefab"); return null; }

            Vector3 from = PlayerAvatar.Local != null ? PlayerAvatar.Local.transform.position
                : PlayerAbilities.Instance != null ? PlayerAbilities.Instance.transform.position : Vector3.zero;
            SpawnPortal best = null;
            float bestDist = float.MaxValue;
            foreach (var p in SpawnPortal.All)
            {
                if (p == null || !p.IsOpen) continue;
                float d = (p.SpawnTransform.position - from).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = p; }
            }
            Vector3 pos;
            if (best != null) pos = best.GetSpawnPosition();
            else if (!wm.TryGetRescuePosition(out pos)) { ToastUI.Show("DEV: kein Spawnpunkt"); return null; }
            return wm.SpawnEnemyAt(config, pos);
        }

        // Champion des lokalen Spielers wechseln. Im Netz über die Champion-Wahl des NetPlayers, damit alle Rechner
        // dieselbe Figur zeigen (PlayerAvatar wendet die Wahl an); ohne Netz direkt.
        private static void DevSetChampion(ChampionClass cls)
        {
            if (NetPlayer.Local != null) NetPlayer.Local.RequestChampion(cls);
            else if (PlayerAbilities.Instance != null) PlayerAbilities.Instance.SetChampion(cls);
        }

        // F8: aktiven Händler sofort einnehmen (Kartenauswahl öffnet sich)
        public void DevCaptureMerchant()
        {
            var mm = MerchantManager.Instance;
            if (mm != null && mm.ActiveMerchant != null && mm.ActiveMerchant.IsActive) mm.ActiveMerchant.Complete();
        }

        private void ApplyCheats()
        {
            var eco = EconomyManager.Instance;
            if (UnlimitedResources && eco != null)
            {
                if (eco.CurrentShards < ShardReserve) eco.AddShards(ShardReserve - eco.CurrentShards);
                if (eco.CurrentMana < eco.MaxMana) eco.AddMana(eco.MaxMana - eco.CurrentMana);
            }

            if (Nexus.Instance != null) Nexus.Instance.Invulnerable = InvulnerableNexus;
            if (BuddySlotManager.Instance != null) BuddySlotManager.Instance.SetUnlimited(UnlimitedBuddies);

            // Alle Spielfiguren (Schaden entscheidet der Server)
            if (PlayerAvatar.All.Count > 0)
            {
                foreach (var a in PlayerAvatar.All)
                    if (a != null && a.Stats != null) a.Stats.GodMode = GodMode;
            }
            else
            {
                var player = PlayerAbilities.Instance != null ? PlayerAbilities.Instance.GetComponent<PlayerStats>() : null;
                if (player != null) player.GodMode = GodMode;
            }
        }

        void OnGUI()
        {
            if (!Active) return;
            if (_box == null)
            {
                _box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, padding = new RectOffset(10, 10, 8, 8) };
                _label = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };
            }

            float y = Screen.height * 0.22f;
            if (!ShowHelp)
            {
                GUI.Label(new Rect(12, y, 200, 24), "<b><color=#ffcc33>DEV</color></b>  (F1)", _label);
                return;
            }

            string text =
                "<b><color=#ffcc33>DEV-MODUS</color></b>\n" +
                $"Ressourcen: {(UnlimitedResources ? "unbegrenzt" : "normal")}   Buddies: {(UnlimitedBuddies ? "unbegrenzt" : "Limit")}   Nexus: {(InvulnerableNexus ? "unverwundbar" : "normal")}   Spieler: {(GodMode ? "unverwundbar" : "normal")}\n" +
                "F1  Hilfe ein/aus\n" +
                "F2  alle Gegner töten\n" +
                "F3  alle Zauber freischalten\n" +
                "F4  nächsten Schrein erwecken\n" +
                "F5  +1 Buddy-Slot\n" +
                "F6  Champion wechseln\n" +
                "F7  nächsten Händler öffnen (Shift: Händlertyp durchschalten)\n" +
                "F8  aktiven Händler sofort einnehmen\n" +
                "F9  nächsten Boss spawnen";
            GUI.Box(new Rect(12, y, 520, 236), GUIContent.none, _box);
            GUI.Label(new Rect(22, y + 6, 510, 231), text, _label);
        }
    }
}
