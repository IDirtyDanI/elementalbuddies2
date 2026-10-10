using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ElementalBuddies
{
    // Top-Bar & Wellen-Knopf. Die eigene Spielfigur spawnt erst über das Netz (PlayerAvatar.Local) –
    // HP/Mana werden daher verzögert gebunden. Zusätzlich: persönliches Gold (NetPlayer.Local.Gold) neben den
    // Seelensplittern und die Mitspieler-Anzeige (TeamHUD, nur im Mehrspieler sichtbar).
    public class HUDManager : MonoBehaviour
    {
        [Header("Stats")]
        public Slider HPSlider;
        public TextMeshProUGUI HPText; // Optional
        public Slider ManaSlider;
        public TextMeshProUGUI ManaText;
        public TextMeshProUGUI ShardText; // Seelensplitter (Bau-Währung, Teamkasse)

        [Header("Gold (persönlich)")]
        [Tooltip("Optional. Fehlt es, wird zur Laufzeit eine Kopie von ShardIcon/ShardText rechts daneben erzeugt.")]
        public TextMeshProUGUI GoldText;
        [Tooltip("Optional: Münz-Icon. Ohne wird das Splitter-Icon goldgelb eingefärbt.")]
        public Sprite GoldIcon;
        public Color GoldTint = new Color(1f, 0.8f, 0.25f);
        public Color GoldTextColor = new Color(1f, 0.86f, 0.4f);

        [Header("Wave")]
        public TextMeshProUGUI WaveText;
        public Button StartWaveButton;

        [Header("Buddies")]
        public TextMeshProUGUI BuddySlotText; // Optional

        [Header("Mitspieler")]
        [Tooltip("Porträts in der Reihenfolge von ChampionClass (Magier, Schwertkämpfer, Bogenschütze) für TeamHUD.")]
        public Sprite[] ChampionPortraits = new Sprite[0];

        private PlayerStats _playerStats;
        private PlayerMana _mana;
        private TextMeshProUGUI _waveButtonLabel;
        private string _waveButtonDefault;
        private string _lastWaveLabel;

        void Start()
        {
            if (GetComponent<WavePreviewUI>() == null) WavePreviewUI.Create(this); // Wellenvorschau (Plan Fesselung C1)
            if (EconomyManager.Instance != null)
            {
                EconomyManager.Instance.OnShardsChanged += UpdateShards;
                UpdateShards();
            }

            if (WaveManager.Instance != null)
            {
                WaveManager.Instance.OnWaveStart += UpdateWaveInfo;
                WaveManager.Instance.OnWaveEnd += UpdateWaveInfo;
                UpdateWaveInfo();

                if (StartWaveButton != null)
                {
                    StartWaveButton.onClick.RemoveAllListeners(); // Clean slate to avoid doubles
                    // Koop: Bereit-Abstimmung (Server startet, wenn alle bereit sind und keine Sperre aktiv ist)
                    StartWaveButton.onClick.AddListener(() =>
                    {
                        if (WaveManager.Instance != null) WaveManager.Instance.RequestStartWave();
                    });
                    _waveButtonLabel = StartWaveButton.GetComponentInChildren<TextMeshProUGUI>(true);
                    if (_waveButtonLabel != null) _waveButtonDefault = _waveButtonLabel.text;
                }
                else
                {
                    Debug.LogError("HUDManager: StartWaveButton is NOT assigned in Inspector!");
                }
            }

            if (BuddySlotManager.Instance != null)
            {
                BuddySlotManager.Instance.OnSlotsChanged += UpdateBuddySlots;
            }
            UpdateBuddySlots();

            SetupGold();
            NetPlayer.OnGoldChanged += HandleGoldChanged;
            UpdateGold();

            PlayerMana.OnLocalManaChanged += UpdateMana;
            PlayerAvatar.OnLocalAvatarSpawned += HandleLocalAvatarSpawned;
            TryBindPlayer();
            UpdateMana();

            // Mitspieler-Anzeige + Namensschilder (baut ihr eigenes Overlay)
            var team = GetComponent<TeamHUD>();
            if (team == null) team = gameObject.AddComponent<TeamHUD>();
            team.Setup(ShardText != null ? ShardText.font : null, ChampionPortraits);
        }

        void OnDestroy()
        {
            if (EconomyManager.Instance != null)
            {
                EconomyManager.Instance.OnShardsChanged -= UpdateShards;
            }
            if (WaveManager.Instance != null)
            {
                 WaveManager.Instance.OnWaveStart -= UpdateWaveInfo;
                 WaveManager.Instance.OnWaveEnd -= UpdateWaveInfo;
            }
            if (_playerStats != null) _playerStats.OnHealthChanged -= UpdateHP;
            if (BuddySlotManager.Instance != null) BuddySlotManager.Instance.OnSlotsChanged -= UpdateBuddySlots;
            NetPlayer.OnGoldChanged -= HandleGoldChanged;
            PlayerMana.OnLocalManaChanged -= UpdateMana;
            PlayerAvatar.OnLocalAvatarSpawned -= HandleLocalAvatarSpawned;
        }

        void Update()
        {
            // Figur/Mana können später spawnen oder neu gespawnt werden
            if (_playerStats == null || (PlayerAvatar.Local != null && PlayerAvatar.Local.Stats != _playerStats)) TryBindPlayer();
            if (_mana != PlayerMana.Local) { _mana = PlayerMana.Local; UpdateMana(); }
            UpdateWaveButton();
        }

        // ---------------- Spielfigur ----------------

        private void HandleLocalAvatarSpawned(PlayerAvatar avatar) => TryBindPlayer();

        private void TryBindPlayer()
        {
            var av = PlayerAvatar.Local;
            var stats = av != null ? av.Stats : null;
            if (stats == _playerStats) return;
            if (_playerStats != null) _playerStats.OnHealthChanged -= UpdateHP;
            _playerStats = stats;
            if (_playerStats != null)
            {
                _playerStats.OnHealthChanged += UpdateHP;
                UpdateHP();
            }
        }

        private void UpdateBuddySlots()
        {
            if (BuddySlotText == null) return;
            if (BuddySlotManager.Instance == null)
            {
                BuddySlotText.text = "";
                return;
            }
            BuddySlotText.text = BuddySlotManager.Instance.Unlimited
                ? $"{BuddySlotManager.Instance.UsedSlots}/∞"
                : $"{BuddySlotManager.Instance.UsedSlots}/{BuddySlotManager.Instance.MaxSlots}";
        }

        // Mana der eigenen Figur (PlayerMana.Local)
        private void UpdateMana()
        {
            var m = PlayerMana.Local;
            if (m == null) return;
            float current = m.CurrentMana;
            float max = m.MaxMana;

            if (ManaSlider != null)
            {
                ManaSlider.maxValue = max;
                ManaSlider.value = current;
            }
            if (ManaText != null) ManaText.text = $"{Mathf.FloorToInt(current)}/{Mathf.FloorToInt(max)}";
        }

        private void UpdateShards()
        {
            if (ShardText == null || EconomyManager.Instance == null) return;
            ShardText.text = $"{Mathf.FloorToInt(EconomyManager.Instance.CurrentShards)}";
        }

        private void UpdateHP()
        {
            if (_playerStats == null) return;
            if (HPSlider != null)
            {
                HPSlider.maxValue = _playerStats.MaxHP;
                HPSlider.value = _playerStats.CurrentHP;
            }
            if (HPText != null) HPText.text = $"{Mathf.CeilToInt(Mathf.Max(0f, _playerStats.CurrentHP))}/{Mathf.FloorToInt(_playerStats.MaxHP)}";
        }

        // ---------------- Gold ----------------

        // Gold-Anzeige rechts neben den Splittern: Kopie von ShardIcon + ShardText, falls keine eigene zugewiesen ist
        private void SetupGold()
        {
            if (GoldText != null || ShardText == null) return;
            var parent = ShardText.transform.parent;
            var shardRt = ShardText.rectTransform;
            var shardIcon = parent != null ? parent.Find("ShardIcon") as RectTransform : null;

            // Splitter-Zahl schmaler (max. 4 Stellen), Gold direkt dahinter
            float textW = Mathf.Min(shardRt.sizeDelta.x, 104f);
            shardRt.sizeDelta = new Vector2(textW, shardRt.sizeDelta.y);
            float x = shardRt.anchoredPosition.x + textW + 4f;

            if (shardIcon != null)
            {
                var icon = Instantiate(shardIcon.gameObject, parent);
                icon.name = "GoldIcon";
                var irt = (RectTransform)icon.transform;
                irt.anchoredPosition = new Vector2(x + irt.sizeDelta.x * irt.pivot.x, shardIcon.anchoredPosition.y);
                x += irt.sizeDelta.x + 4f;
                var img = icon.GetComponent<Image>();
                if (img != null)
                {
                    if (GoldIcon != null) img.sprite = GoldIcon;
                    else img.color = GoldTint;
                }
            }

            GoldText = Instantiate(ShardText, parent);
            GoldText.name = "GoldText";
            GoldText.rectTransform.anchoredPosition = new Vector2(x + textW * GoldText.rectTransform.pivot.x, shardRt.anchoredPosition.y);
            GoldText.color = GoldTextColor;
            GoldText.text = "0";
        }

        private void HandleGoldChanged(NetPlayer player, float value)
        {
            if (player == NetPlayer.Local) UpdateGold();
        }

        private void UpdateGold()
        {
            if (GoldText == null) return;
            var np = NetPlayer.Local;
            GoldText.text = np != null ? Mathf.FloorToInt(np.Gold.Value).ToString() : "0";
        }

        // ---------------- Welle ----------------

        public void RefreshWave() => UpdateWaveInfo();

        private void UpdateWaveInfo()
        {
            if (WaveManager.Instance == null) return;
            if (WaveText != null)
            {
                // Schwierigkeit dezent als zweite Zeile unter der Wellennummer (kleiner, halbtransparent; passt in die 200er-Box)
                var diff = WaveManager.Instance.Difficulty;
                string wave = $"Welle {WaveManager.Instance.CurrentWaveIndex + 1}";
                WaveText.text = diff != null
                    ? $"<line-height=80%>{wave}\n<size=50%><alpha=#B0>{diff.DisplayName}</line-height>"
                    : wave;
            }
            UpdateWaveButton();
        }

        // Knopf zwischen den Wellen: Solo "Welle starten", Koop "Bereit (1/3)" bzw. Wartegrund
        private void UpdateWaveButton()
        {
            var wm = WaveManager.Instance;
            if (StartWaveButton == null || wm == null) return;

            bool active = wm.IsWaveActive;
            // Koop: nochmal klicken zieht die Bereit-Stimme zurück
            StartWaveButton.interactable = !active;

            if (_waveButtonLabel == null) return;
            string label = _waveButtonDefault;
            if (Net.IsMultiplayer && !active)
            {
                string reason = wm.WaitReason;
                if (!wm.IsLocalReady) label = $"Bereit ({wm.ReadyCount}/{wm.ReadyNeeded})";
                else if (!string.IsNullOrEmpty(reason)) label = reason;
                else label = $"Warte auf Mitspieler ({wm.ReadyCount}/{wm.ReadyNeeded})";
            }
            // Früher Start: sinkender Splitter-Bonus (nur solange man selbst noch starten bzw. zustimmen kann)
            if (!active && (!Net.IsMultiplayer || !wm.IsLocalReady))
            {
                float early = wm.EarlyCallBonus;
                if (early >= 1f) label += $"  <color=#9fe8ff>+{early:0}</color>";
            }
            if (label != _lastWaveLabel)
            {
                _lastWaveLabel = label;
                _waveButtonLabel.text = label;
            }
        }
    }
}
