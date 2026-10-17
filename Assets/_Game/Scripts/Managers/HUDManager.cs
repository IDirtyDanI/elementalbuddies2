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
        [Tooltip("Porträts in der Reihenfolge von ChampionClass (Magier, Schwertkämpfer, Bogenschütze, Berserker) für TeamHUD.")]
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
                    if (_waveButtonLabel != null)
                    {
                        _waveButtonDefault = _waveButtonLabel.text;
                        // Lange Beschriftungen („Warte auf Mitspieler (1/3)“, „Welle starten +14“) bleiben im Knopf
                        _waveButtonLabel.textWrappingMode = TextWrappingModes.NoWrap;
                        _waveButtonLabel.fontSizeMax = _waveButtonLabel.fontSize;
                        _waveButtonLabel.fontSizeMin = Mathf.Round(_waveButtonLabel.fontSize * 0.6f);
                        _waveButtonLabel.enableAutoSizing = true;
                        if (_waveButtonLabel.margin == Vector4.zero) _waveButtonLabel.margin = new Vector4(24f, 0f, 24f, 0f);
                    }
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

            // Runde Minimap oben links unter der Top-Bar (baut sich zur Laufzeit in den HUD-Canvas)
            MinimapUI.Create(this);
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
                ApplyResourceLook(m);
            }
            if (ManaText != null)
                ManaText.text = m.HasProfile
                    ? $"{m.ResourceName} {Mathf.FloorToInt(current)}/{Mathf.FloorToInt(max)}"
                    : $"{Mathf.FloorToInt(current)}/{Mathf.FloorToInt(max)}";
        }

        // Berserker: Mana-Leiste wird zur roten Wut-Leiste (Farbe aus dem Ressourcen-Profil), sonst Originalfarbe
        private Image _manaFill;
        private Color _manaFillDefault;
        private bool _manaFillCached;
        private void ApplyResourceLook(PlayerMana m)
        {
            if (!_manaFillCached)
            {
                _manaFillCached = true;
                _manaFill = ManaSlider.fillRect != null ? ManaSlider.fillRect.GetComponent<Image>() : null;
                if (_manaFill != null) _manaFillDefault = _manaFill.color;
            }
            if (_manaFill != null)
            {
                Color target = m.HasProfile ? m.Profile.Color : _manaFillDefault;
                if (_manaFill.color != target) _manaFill.color = target;
            }
            ApplyResourceIcon(m);
        }

        // Symbol links neben der Leiste: Mana-Tropfen bzw. Profil-Symbol (Berserker: Wut, #169)
        [Tooltip("Symbol neben der Mana-Leiste (leer: Kind-Objekt „ManaIcon“ im HUD wird gesucht).")]
        public Image ManaIcon;
        private Sprite _manaIconDefault;
        private bool _manaIconCached;
        private void ApplyResourceIcon(PlayerMana m)
        {
            if (!_manaIconCached)
            {
                _manaIconCached = true;
                if (ManaIcon == null)
                {
                    // Szene: „ManaIcon“ sitzt neben der Leiste im selben HUD-Zweig
                    for (Transform t = ManaSlider.transform; t != null && ManaIcon == null; t = t.parent)
                    {
                        var found = FindDeep(t, "ManaIcon");
                        if (found != null) ManaIcon = found.GetComponent<Image>();
                    }
                }
                if (ManaIcon != null) _manaIconDefault = ManaIcon.sprite;
            }
            if (ManaIcon == null) return;
            Sprite target = m.HasProfile && m.Profile.Icon != null ? m.Profile.Icon : _manaIconDefault;
            if (ManaIcon.sprite != target) ManaIcon.sprite = target;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var r = FindDeep(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
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
                // Schwierigkeit dezent in derselben Zeile hinter der Wellennummer (kleiner, halbtransparent).
                // Früher zweite Zeile darunter – die ragte unten aus der Top-Bar (Bekannte-Probleme #158).
                var diff = WaveManager.Instance.Difficulty;
                string wave = $"Welle {WaveManager.Instance.CurrentWaveIndex + 1}";
                WaveText.text = diff != null
                    ? $"{wave} <size=60%><alpha=#B0>· {diff.DisplayName}</size>"
                    : wave;
                FitWaveText();
            }
            UpdateWaveButton();
        }

        // ---------------- Wellen-Anzeige in der Top-Bar ----------------

        private bool _waveLayoutInit;
        private float _waveBaseWidth, _waveBaseFont, _waveIconGap;
        private RectTransform _waveIcon;

        // Wellen-Text einzeilig in der Top-Bar halten: Box wächst nach links (Symbol rückt mit), bis höchstens
        // WaveTextMaxWidth; reicht das nicht, verkleinert TMP-Auto-Size die Schrift. Nichts ragt aus der Leiste.
        [Tooltip("Größte Breite des Wellen-Textes in der Top-Bar (Canvas-Einheiten), danach wird die Schrift kleiner.")]
        public float WaveTextMaxWidth = 380f;
        [Tooltip("Mindestabstand des Wellen-Textes zum rechten Ende der Top-Bar (Holzrahmen), Canvas-Einheiten.")]
        public float WaveTextRightInset = 40f;

        private void FitWaveText()
        {
            var rt = WaveText.rectTransform;
            if (!_waveLayoutInit)
            {
                _waveLayoutInit = true;
                _waveBaseWidth = rt.sizeDelta.x;
                _waveBaseFont = WaveText.fontSize;
                WaveText.textWrappingMode = TextWrappingModes.NoWrap;
                WaveText.overflowMode = TextOverflowModes.Ellipsis;
                WaveText.fontSizeMax = _waveBaseFont;
                WaveText.fontSizeMin = Mathf.Round(_waveBaseFont * 0.6f);
                // Symbol links neben dem Text (Geschwister „WaveIcon“ mit gleichem Anker)
                var parent = rt.parent;
                var icon = parent != null ? parent.Find("WaveIcon") as RectTransform : null;
                if (icon != null && icon.anchorMin == rt.anchorMin && icon.anchorMax == rt.anchorMax)
                {
                    _waveIcon = icon;
                    float textLeft = rt.anchoredPosition.x - rt.pivot.x * rt.sizeDelta.x;
                    float iconRight = icon.anchoredPosition.x + (1f - icon.pivot.x) * icon.sizeDelta.x;
                    _waveIconGap = textLeft - iconRight;
                }
                // Text endet jetzt bündig mit der Box → etwas Luft zum Holzrahmen lassen
                if (rt.anchorMin.x >= 1f && rt.pivot.x >= 1f && rt.anchoredPosition.x > -WaveTextRightInset)
                    rt.anchoredPosition = new Vector2(-WaveTextRightInset, rt.anchoredPosition.y);
            }

            WaveText.enableAutoSizing = false;
            WaveText.fontSize = _waveBaseFont;
            float pref = WaveText.GetPreferredValues(WaveText.text, Mathf.Infinity, Mathf.Infinity).x + 6f;
            float width = Mathf.Clamp(pref, _waveBaseWidth, Mathf.Max(_waveBaseWidth, WaveTextMaxWidth));
            WaveText.enableAutoSizing = pref > width; // nur wenn die Maximalbreite nicht reicht
            rt.sizeDelta = new Vector2(width, rt.sizeDelta.y);

            if (_waveIcon != null)
            {
                float textLeft = rt.anchoredPosition.x - rt.pivot.x * width;
                float x = textLeft - _waveIconGap - (1f - _waveIcon.pivot.x) * _waveIcon.sizeDelta.x;
                _waveIcon.anchoredPosition = new Vector2(x, _waveIcon.anchoredPosition.y);
            }
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
