using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ElementalBuddies
{
    // Info-Panel für den ausgewählten Buddy: Stufe, Werte, Aufwerten (nur zwischen den Wellen) und Verkaufen.
    // Script auf ein immer aktives Objekt (z. B. Canvas) oder direkt aufs Panel legen (Panel muss beim Start aktiv sein).
    public class BuddyInfoPanelUI : MonoBehaviour
    {
        public GameObject Panel;
        public TextMeshProUGUI NameText;
        public TextMeshProUGUI LevelText;
        public TextMeshProUGUI StatsText;

        [Header("Emblem")]
        public Image EmblemImage;
        // Index = Element wie ElementalBuddy.ElementIndex / UnitType-Reihenfolge: 0 Feuer, 1 Eis, 2 Erde, 3 Licht
        public Sprite[] Stage1Sprites; // Kleine Wesen
        public Sprite[] Stage2Sprites; // Humanoid
        public Sprite[] Stage3Sprites; // Humanoid mit Rüstung
        public Sprite[] Stage4Sprites; // Stufe 4 (Krone, Waffe, Aura); fehlt der Eintrag → Stage3Sprites
        public Sprite[] EmblemSprites; // Fallback, falls das Stufen-Array leer ist / keinen Eintrag hat

        [Header("Buttons")]
        public Button UpgradeButton;
        public TextMeshProUGUI UpgradeButtonText;
        public Button SellButton;
        public TextMeshProUGUI SellButtonText;
        public Button CloseButton;

        [Header("Darstellung")]
        public string Arrow = "→"; // Falls die Schrift keinen Pfeil hat: "->"
        public Color NextValueColor = new Color(0.25f, 0.65f, 0.2f);

        [Header("Fusion")]
        public GameObject FusionSection;              // nur für normale Buddies sichtbar
        public TextMeshProUGUI FusionHint;            // Grund, warum nicht verschmolzen werden kann / "Verschmelzen mit:"
        public RectTransform FusionOptionsContainer;  // VerticalLayoutGroup
        public Button FusionOptionTemplate;           // inaktiv; Kinder "Icon" (Image), "Label" (TMP)
        public TextMeshProUGUI FusionInfoText;        // für Fusions-Buddies: Beschreibung der Fähigkeit (statt der Sektion)
        [Tooltip("Optional: Hintergrund-Box um FusionInfoText, wird mit ihm ein-/ausgeblendet.")]
        public GameObject FusionInfoBox;
        public Color UnaffordableColor = new Color(0.85f, 0.2f, 0.2f);

        [Header("Fusions-Liste: Platz & Scrollen")]
        [Tooltip("HUD unten links (Fähigkeitenleiste); die Fusions-Box endet darüber. Leer → Objekt \"AbilityBar\" im Canvas, sonst BottomReserve.")]
        public RectTransform BottomHud;
        [Tooltip("HUD oben (TopBar); das Panel wird höchstens bis darunter nach oben geschoben. Leer → \"TopBar\", sonst TopReserve.")]
        public RectTransform TopHud;
        public float HudMargin = 12f;
        public float BottomReserve = 160f;   // Canvas-Einheiten, falls BottomHud fehlt
        public float TopReserve = 110f;      // Canvas-Einheiten, falls TopHud fehlt
        [Tooltip("Zeilenhöhe der kompakten Tri-Fusions-Optionen (2er-Optionen behalten die Vorlagen-Höhe).")]
        public float TriOptionHeight = 54f;
        public Color ScrollTrackColor = new Color(0.24f, 0.15f, 0.08f, 0.15f);
        public Color ScrollHandleColor = new Color(0.45f, 0.29f, 0.14f, 0.85f);
        [Tooltip("Mindestabstand zwischen Werte-Block und Button-Reihe; das Panel wächst nach unten, wenn der Platz nicht reicht.")]
        public float StatsButtonGap = 12f;

        private ElementalBuddy _buddy;

        // Fusions-Optionen: Buttons werden nur neu gebaut, wenn sich die Menge der Optionen ändert
        private class OptionEntry
        {
            public Button Button;
            public TextMeshProUGUI Label;
            public ElementalBuddy Partner;
        }
        private readonly List<OptionEntry> _optionEntries = new List<OptionEntry>();
        private string _optionsKey = "";
        private readonly StringBuilder _keyBuilder = new StringBuilder();
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // Fusions-Liste in einer ScrollRect (zur Laufzeit gebaut); Panel wird bei Bedarf nach oben geschoben
        private RectTransform _panelRect;
        private float _panelBaseY;
        private float _panelBaseHeight, _statsTop, _buttonsTop;
        private float _growth; // zusätzliche Panel-Höhe für lange Werte-Blöcke
        private ScrollRect _optionsScroll;
        private LayoutElement _optionsScrollLayout;
        private readonly Vector3[] _corners = new Vector3[4];

        // Schloss-Icons vor Sperrhinweisen (Meta-Freischaltungen, zur Laufzeit erzeugt)
        private Image _upgradeLock, _fusionLock;
        private Vector4 _upgradeTextMargin, _fusionHintMargin;
        private bool _marginsCached;

        void Start()
        {
            if (UpgradeButton != null)
            {
                UpgradeButton.onClick.RemoveAllListeners();
                UpgradeButton.onClick.AddListener(() =>
                {
                    if (InteractionManager.Instance != null) InteractionManager.Instance.UpgradeSelected();
                    Refresh();
                });
            }
            if (SellButton != null)
            {
                SellButton.onClick.RemoveAllListeners();
                SellButton.onClick.AddListener(() =>
                {
                    if (InteractionManager.Instance != null) InteractionManager.Instance.SellSelected();
                });
            }
            if (CloseButton != null)
            {
                CloseButton.onClick.RemoveAllListeners();
                CloseButton.onClick.AddListener(() =>
                {
                    if (InteractionManager.Instance != null) InteractionManager.Instance.DeselectBuddy();
                });
            }

            if (InteractionManager.Instance != null) InteractionManager.Instance.OnBuddySelected += HandleBuddySelected;
            if (EconomyManager.Instance != null) EconomyManager.Instance.OnShardsChanged += Refresh;
            if (GameManager.Instance != null) GameManager.Instance.OnGameOver += HandleGameOver;

            if (FusionOptionTemplate != null) FusionOptionTemplate.gameObject.SetActive(false);

            _panelRect = Panel != null ? Panel.transform as RectTransform : null;
            if (_panelRect != null) _panelBaseY = _panelRect.anchoredPosition.y;
            SetupStatsArea();
            if (BottomHud == null) BottomHud = FindInCanvas("AbilityBar");
            if (TopHud == null) TopHud = FindInCanvas("TopBar");
            SetupOptionsScroll();

            HandleBuddySelected(InteractionManager.Instance != null ? InteractionManager.Instance.SelectedBuddy : null);
        }

        void OnDestroy()
        {
            if (InteractionManager.Instance != null) InteractionManager.Instance.OnBuddySelected -= HandleBuddySelected;
            if (EconomyManager.Instance != null) EconomyManager.Instance.OnShardsChanged -= Refresh;
            if (GameManager.Instance != null) GameManager.Instance.OnGameOver -= HandleGameOver;
            if (_buddy != null) _buddy.OnLevelChanged -= Refresh;
        }

        // Spielphase (Bau/Kampf/Pause) ändert die Button-Zustände -> leicht pollen
        void Update()
        {
            if (_buddy != null && Time.frameCount % 10 == 0) Refresh();
        }

        private void HandleGameOver(string reason)
        {
            HandleBuddySelected(null);
        }

        private void HandleBuddySelected(ElementalBuddy buddy)
        {
            if (_buddy != null) _buddy.OnLevelChanged -= Refresh;
            _buddy = buddy;
            if (_buddy != null) _buddy.OnLevelChanged += Refresh;

            ClearFusionOptions();
            if (_buddy == null)
            {
                if (FusionSection != null) FusionSection.SetActive(false);
                if (FusionInfoText != null) FusionInfoText.gameObject.SetActive(false);
                if (FusionInfoBox != null) FusionInfoBox.SetActive(false);
            }

            if (Panel != null) Panel.SetActive(_buddy != null);
            Refresh();
        }

        private void Refresh()
        {
            if (_buddy == null) return;

            int level = _buddy.Level;
            bool canUpgrade = _buddy.CanUpgrade;
            int next = level + 1;

            var fusion = _buddy as FusionBuddy;
            var super = _buddy as SuperBuddy;
            var fm = FusionManager.Instance;
            string fusionDescription = fusion != null && fm != null ? fm.GetDescription(fusion.Element) : null;
            Sprite fusionIcon = fusion != null && fm != null ? fm.GetIcon(fusion.Element) : null;

            if (NameText != null) NameText.text = _buddy.StageName;
            if (LevelText != null)
                LevelText.text = super != null
                    ? $"Super-Elementar aus {ElementInfo.Name(super.ParentA)} + {ElementInfo.Name(super.ParentB)} + {ElementInfo.Name(super.ParentC)}"
                    : fusion != null
                    ? $"Fusion aus {ElementInfo.Name(fusion.ParentA)} + {ElementInfo.Name(fusion.ParentB)}"
                    : $"Stufe {level} / {_buddy.MaxLevel}";

            if (EmblemImage != null)
            {
                int idx = _buddy.ElementIndex;
                Sprite[] stageSprites = _buddy.Stage >= 3 ? Stage3Sprites : (_buddy.Stage == 2 ? Stage2Sprites : Stage1Sprites);
                Sprite sprite = fusionIcon;
                if (sprite == null && _buddy.Stage >= 4) sprite = PickSprite(Stage4Sprites, idx);
                if (sprite == null) sprite = PickSprite(stageSprites, idx);
                if (sprite == null) sprite = PickSprite(EmblemSprites, idx);
                EmblemImage.sprite = sprite;
                EmblemImage.enabled = sprite != null;
            }

            if (StatsText != null) StatsText.text = fusion != null ? BuildFusionStats(fusion) : BuildStats(canUpgrade, level, next);

            // Aufwerten
            if (UpgradeButton != null || UpgradeButtonText != null)
            {
                string label;
                bool interactable;
                if (fusion != null)
                {
                    label = "Keine Stufen";
                    interactable = false;
                }
                else if (!canUpgrade && _buddy.IsStage4Locked && level == ElementalBuddy.PerkLevel - 1)
                {
                    // Meta-Freischaltung fehlt (Erfolg siehe Werte-Block)
                    label = "Stufe 4 gesperrt";
                    interactable = false;
                }
                else if (!canUpgrade)
                {
                    label = "Maximale Stufe";
                    interactable = false;
                }
                else if (!ElementalBuddy.IsUpgradePhase)
                {
                    label = "Nur zwischen den Wellen";
                    interactable = false;
                }
                else
                {
                    float cost = _buddy.NextUpgradeCost;
                    label = $"Aufwerten ({Mathf.CeilToInt(cost)})";
                    interactable = EconomyManager.Instance != null && EconomyManager.Instance.CanAfford(cost);
                }
                if (UpgradeButtonText != null) UpgradeButtonText.text = label;
                if (UpgradeButton != null) UpgradeButton.interactable = interactable;
                SetLockIcon(UpgradeButtonText, ref _upgradeLock, fusion == null && !canUpgrade && _buddy.IsStage4Locked && level == ElementalBuddy.PerkLevel - 1);
            }

            // Verkaufen (nur in der Bauphase, wie Rechtsklick)
            var im = InteractionManager.Instance;
            if (im != null)
            {
                if (SellButtonText != null) SellButtonText.text = $"Verkaufen (+{Mathf.FloorToInt(im.GetSellRefund(_buddy))})";
                if (SellButton != null) SellButton.interactable = im.CanSellNow;
            }

            // Fusion
            if (FusionInfoText != null)
            {
                bool show = fusion != null && !string.IsNullOrEmpty(fusionDescription);
                FusionInfoText.gameObject.SetActive(show);
                if (FusionInfoBox != null && FusionInfoBox.activeSelf != show) FusionInfoBox.SetActive(show);
                if (show) FusionInfoText.text = fusionDescription;
            }
            // Super-Elementare: keine weitere Fusion; 2er-Fusionen können zum Super-Elementar verschmelzen
            RefreshFusionSection(super != null);
            FitStatsArea();
            LayoutFusionArea();
        }

        // Schloss-Icon links im Text (AchievementDatabase.LockIcon); Text rückt per Margin nach rechts
        private void SetLockIcon(TextMeshProUGUI text, ref Image icon, bool show)
        {
            if (text == null) return;
            if (!_marginsCached)
            {
                _marginsCached = true;
                if (UpgradeButtonText != null) _upgradeTextMargin = UpgradeButtonText.margin;
                if (FusionHint != null) _fusionHintMargin = FusionHint.margin;
            }
            Vector4 baseMargin = text == FusionHint ? _fusionHintMargin : _upgradeTextMargin;
            var db = AchievementDatabaseSO.Instance;
            Sprite sprite = db != null ? db.LockIcon : null;
            show &= sprite != null;
            if (show && icon == null)
            {
                var go = new GameObject("LockIcon", typeof(RectTransform));
                var rt = (RectTransform)go.transform;
                rt.SetParent(text.rectTransform, false);
                icon = go.AddComponent<Image>();
                icon.sprite = sprite;
                icon.preserveAspect = true;
                icon.raycastTarget = false;
            }
            if (icon == null) return;
            if (icon.gameObject.activeSelf != show) icon.gameObject.SetActive(show);
            float size = Mathf.Round(text.fontSize * 1.25f);
            if (show)
            {
                var rt = icon.rectTransform;
                // Am Button-Text mittig links, am Hinweis oben links (erste Zeile)
                bool top = text == FusionHint;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, top ? 1f : 0.5f);
                rt.pivot = new Vector2(0f, top ? 1f : 0.5f);
                rt.anchoredPosition = new Vector2(baseMargin.x + (top ? 0f : 10f), top ? -2f : 0f);
                rt.sizeDelta = new Vector2(size, size);
            }
            text.margin = show ? new Vector4(baseMargin.x + size + (text == FusionHint ? 6f : 16f), baseMargin.y, baseMargin.z, baseMargin.w) : baseMargin;
        }

        // ---------------- Werte-Block ----------------

        // Ausgangsmaße merken; StatsText bricht um, damit lange Zeilen nicht seitlich aus dem Panel ragen
        private void SetupStatsArea()
        {
            if (_panelRect == null || StatsText == null) return;
            _panelBaseHeight = _panelRect.sizeDelta.y;
            var stats = StatsText.rectTransform;
            _statsTop = -(stats.anchoredPosition.y + stats.rect.height * (1f - stats.pivot.y)); // Abstand zur Panel-Oberkante
            StatsText.textWrappingMode = TextWrappingModes.Normal;
            // Oberkante der Button-Reihe über der Panel-Unterkante (Buttons unten verankert)
            _buttonsTop = 0f;
            foreach (var b in new[] { UpgradeButton, SellButton })
            {
                if (b == null) continue;
                var rt = (RectTransform)b.transform;
                if (rt.anchorMax.y > 0.001f) continue;
                _buttonsTop = Mathf.Max(_buttonsTop, rt.anchoredPosition.y + rt.rect.height * (1f - rt.pivot.y));
            }
        }

        // Panel nach unten verlängern, bis der ganze Werte-Block über der Button-Reihe Platz hat (Oberkante bleibt)
        private void FitStatsArea()
        {
            if (_panelRect == null || StatsText == null || _panelBaseHeight <= 0f) return;
            var stats = StatsText.rectTransform;
            if (stats.anchorMin.y < 0.999f) return; // nur oben verankerter Werte-Block
            float textHeight = StatsText.GetPreferredValues(StatsText.text, stats.rect.width, 0f).y;
            float needed = _statsTop + textHeight + StatsButtonGap + _buttonsTop;
            float height = Mathf.Max(_panelBaseHeight, Mathf.Ceil(needed));
            _growth = height - _panelBaseHeight;
            if (!Mathf.Approximately(_panelRect.sizeDelta.y, height))
                _panelRect.sizeDelta = new Vector2(_panelRect.sizeDelta.x, height);
            // Werte-Block füllt genau den Platz bis über die Buttons (Oberkante bleibt)
            float statsHeight = height - _statsTop - StatsButtonGap - _buttonsTop;
            if (!Mathf.Approximately(stats.sizeDelta.y, statsHeight))
            {
                stats.sizeDelta = new Vector2(stats.sizeDelta.x, statsHeight);
                stats.anchoredPosition = new Vector2(stats.anchoredPosition.x, -_statsTop - statsHeight * (1f - stats.pivot.y));
            }
        }

        // Ausgangs-Y ohne Fusions-Verschiebung: bei gewachsenem Panel so weit tiefer, dass die Oberkante bleibt
        private float PanelBaseY => _panelBaseY - _growth * (1f - _panelRect.pivot.y);

        // ---------------- Platz für die Fusions-Box ----------------

        private RectTransform FindInCanvas(string objectName)
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return null;
            foreach (var t in canvas.rootCanvas.GetComponentsInChildren<RectTransform>(true))
                if (t.name == objectName) return t;
            return null;
        }

        // Options-Container in eine ScrollRect (Viewport mit RectMask2D + schmale Pergament-Scrollleiste) einhängen
        private void SetupOptionsScroll()
        {
            if (_optionsScroll != null || FusionOptionsContainer == null || FusionSection == null) return;
            var content = FusionOptionsContainer;
            Transform section = content.parent;
            int index = content.GetSiblingIndex();

            var root = new GameObject("OptionsScroll", typeof(RectTransform));
            var rootRect = (RectTransform)root.transform;
            rootRect.SetParent(section, false);
            rootRect.SetSiblingIndex(index);
            _optionsScrollLayout = root.AddComponent<LayoutElement>();
            _optionsScrollLayout.flexibleWidth = 1f;
            _optionsScrollLayout.minHeight = _optionsScrollLayout.preferredHeight = content.rect.height;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
            var vpRect = (RectTransform)viewport.transform;
            vpRect.SetParent(rootRect, false);
            Stretch(vpRect);
            var vpImage = viewport.GetComponent<Image>();
            vpImage.color = new Color(1f, 1f, 1f, 0f); // unsichtbar, fängt das Mausrad auch zwischen den Zeilen

            content.SetParent(vpRect, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, content.sizeDelta.y);
            var fitter = content.GetComponent<ContentSizeFitter>();
            if (fitter == null) fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var bar = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            var barRect = (RectTransform)bar.transform;
            barRect.SetParent(rootRect, false);
            barRect.anchorMin = new Vector2(1f, 0f);
            barRect.anchorMax = new Vector2(1f, 1f);
            barRect.pivot = new Vector2(1f, 0.5f);
            barRect.sizeDelta = new Vector2(8f, 0f);
            barRect.anchoredPosition = Vector2.zero;
            bar.GetComponent<Image>().color = ScrollTrackColor;

            var area = new GameObject("Sliding Area", typeof(RectTransform));
            var areaRect = (RectTransform)area.transform;
            areaRect.SetParent(barRect, false);
            Stretch(areaRect);
            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            var handleRect = (RectTransform)handle.transform;
            handleRect.SetParent(areaRect, false);
            Stretch(handleRect);
            var handleImage = handle.GetComponent<Image>();
            handleImage.color = ScrollHandleColor;

            var scrollbar = bar.GetComponent<Scrollbar>();
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            _optionsScroll = root.AddComponent<ScrollRect>();
            _optionsScroll.horizontal = false;
            _optionsScroll.vertical = true;
            _optionsScroll.movementType = ScrollRect.MovementType.Clamped;
            _optionsScroll.inertia = false;
            _optionsScroll.scrollSensitivity = 30f;
            _optionsScroll.viewport = vpRect;
            _optionsScroll.content = content;
            _optionsScroll.verticalScrollbar = scrollbar;
            _optionsScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            _optionsScroll.verticalScrollbarSpacing = 4f;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
        }

        // Unter dem Panel hängt die Fusions-Box (bzw. FusionInfoBox). Sie darf nicht in die Fähigkeitenleiste ragen:
        // erst das Panel nach oben schieben (höchstens bis unter die TopBar), reicht das nicht, wird die Liste gescrollt.
        private void LayoutFusionArea()
        {
            if (_panelRect == null || _panelRect.parent == null) return;
            var canvasRect = _panelRect.parent as RectTransform;
            if (canvasRect == null) return;

            RectTransform section = null;
            float sectionHeight = 0f;
            bool fusionList = FusionSection != null && FusionSection.activeSelf;
            float overhead = 0f, desired = 0f;
            if (fusionList)
            {
                section = FusionSection.transform as RectTransform;
                var vlg = FusionSection.GetComponent<VerticalLayoutGroup>();
                if (vlg != null) overhead = vlg.padding.vertical;
                if (FusionHint != null && FusionHint.gameObject.activeSelf)
                    overhead += LayoutUtility.GetPreferredHeight(FusionHint.rectTransform) + (vlg != null ? vlg.spacing : 0f);
                if (_optionsScroll != null && FusionOptionsContainer != null)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(FusionOptionsContainer);
                    desired = LayoutUtility.GetPreferredHeight(FusionOptionsContainer);
                }
                sectionHeight = overhead + desired;
            }
            else if (FusionInfoBox != null && FusionInfoBox.activeSelf)
            {
                section = FusionInfoBox.transform as RectTransform;
                sectionHeight = LayoutUtility.GetPreferredHeight(section);
            }

            // Panel-Grenzen in Canvas-Koordinaten ohne aktuelle Verschiebung
            float baseY = PanelBaseY;
            float currentShift = _panelRect.anchoredPosition.y - baseY;
            _panelRect.GetWorldCorners(_corners);
            float panelBottom = canvasRect.InverseTransformPoint(_corners[0]).y - currentShift;
            float panelTop = canvasRect.InverseTransformPoint(_corners[1]).y - currentShift;
            Rect canvas = canvasRect.rect;

            float bottomLimit = canvas.yMin + BottomReserve;
            if (BottomHud != null && BottomHud.gameObject.activeInHierarchy)
            {
                BottomHud.GetWorldCorners(_corners);
                bottomLimit = canvasRect.InverseTransformPoint(_corners[1]).y + HudMargin;
            }
            float topLimit = canvas.yMax - TopReserve;
            if (TopHud != null && TopHud.gameObject.activeInHierarchy)
            {
                TopHud.GetWorldCorners(_corners);
                topLimit = canvasRect.InverseTransformPoint(_corners[0]).y - HudMargin;
            }

            float maxShift = Mathf.Max(0f, topLimit - panelTop);
            float gap = section != null ? -section.anchoredPosition.y : 0f; // Abstand Panel-Unterkante → Box
            float freeBelow = panelBottom - gap - bottomLimit;

            if (fusionList && _optionsScrollLayout != null)
            {
                float maxView = freeBelow + maxShift - overhead;
                float minView = Mathf.Min(desired, TriOptionHeight);
                float view = Mathf.Min(desired, Mathf.Max(maxView, minView));
                if (!Mathf.Approximately(_optionsScrollLayout.preferredHeight, view))
                {
                    _optionsScrollLayout.minHeight = _optionsScrollLayout.preferredHeight = view;
                    if (view >= desired - 0.5f && _optionsScroll != null) _optionsScroll.verticalNormalizedPosition = 1f;
                }
                sectionHeight = overhead + view;
            }

            float shift = section != null ? Mathf.Clamp(sectionHeight - freeBelow, 0f, maxShift) : 0f;
            float y = baseY + shift;
            if (!Mathf.Approximately(_panelRect.anchoredPosition.y, y))
                _panelRect.anchoredPosition = new Vector2(_panelRect.anchoredPosition.x, y);
        }

        // ---------------- Fusion ----------------

        private void RefreshFusionSection(bool isSuper)
        {
            var fm = FusionManager.Instance;
            bool show = !isSuper && fm != null;
            if (FusionSection != null && FusionSection.activeSelf != show) FusionSection.SetActive(show);
            if (!show)
            {
                ClearFusionOptions();
                return;
            }

            var options = fm.GetOptions(_buddy);
            bool canFuse = fm.CanFuseNow;

            if (FusionHint != null)
            {
                if (options.Count == 0) FusionHint.text = fm.GetBlockReason(_buddy) ?? "";
                else FusionHint.text = canFuse ? "Verschmelzen mit:" : "Verschmelzen mit: <i>(nur zwischen den Wellen)</i>";
                SetLockIcon(FusionHint, ref _fusionLock, options.Count == 0 && fm.GetLockReason(_buddy) != null);
            }

            // Menge der Optionen geändert? -> Buttons neu bauen, sonst nur Texte/Zustände aktualisieren
            _keyBuilder.Clear();
            foreach (var o in options)
                _keyBuilder.Append(o.Partner.GetInstanceID()).Append('+').Append(o.Partner2 != null ? o.Partner2.GetInstanceID() : 0)
                    .Append(':').Append((int)o.Result).Append(';');
            string key = _keyBuilder.ToString();
            if (key != _optionsKey) RebuildFusionOptions(options, key);

            string red = ColorUtility.ToHtmlStringRGB(UnaffordableColor);
            for (int i = 0; i < _optionEntries.Count && i < options.Count; i++)
            {
                var o = options[i];
                var entry = _optionEntries[i];
                if (entry.Label != null)
                {
                    string cost = $"{Mathf.CeilToInt(o.Cost)} Splitter";
                    if (!o.Affordable) cost = $"<color=#{red}>{cost}</color>";
                    string dist = $"({o.Distance.ToString("0.#", Inv)} m)";
                    if (o.IsTri)
                    {
                        // kompakt, zwei Zeilen: "Phönix · 200 Splitter", darunter die Partner (Elemente der Partner zeigt der Hover-Link)
                        string with = o.Partner2 != null ? $"{o.Partner.StageName} & {o.Partner2.StageName}" : o.Partner.StageName;
                        entry.Label.text = $"<b>{FusionInfo.DisplayName(o.Result)}</b>  ·  {cost}\n<size=85%>mit {with} {dist}</size>";
                    }
                    else
                        entry.Label.text = $"<b>{FusionInfo.DisplayName(o.Result)}</b>  ·  mit {o.Partner.StageName} {dist}  ·  {cost}";
                }
                if (entry.Button != null) entry.Button.interactable = canFuse && o.Affordable;
            }
        }

        private void RebuildFusionOptions(List<FusionManager.FusionOption> options, string key)
        {
            ClearFusionOptions();
            _optionsKey = key;
            if (FusionOptionTemplate == null) return;
            Transform parent = FusionOptionsContainer != null ? FusionOptionsContainer : FusionOptionTemplate.transform.parent;

            foreach (var o in options)
            {
                var button = Instantiate(FusionOptionTemplate, parent);
                button.gameObject.name = "FusionOption_" + FusionInfo.Name(o.Result);
                button.gameObject.SetActive(true);

                var icon = FindChild<Image>(button.transform, "Icon");
                if (icon != null)
                {
                    icon.sprite = o.Icon;
                    icon.gameObject.SetActive(o.Icon != null);
                }

                var entry = new OptionEntry { Button = button, Label = FindChild<TextMeshProUGUI>(button.transform, "Label"), Partner = o.Partner };
                if (o.IsTri) MakeCompact(button, icon, entry.Label);
                ElementalBuddy partner = o.Partner, partner2 = o.Partner2;
                var option = o;

                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => OnFusionOptionClicked(option));

                var hover = button.GetComponent<FusionOptionHover>();
                if (hover == null) hover = button.gameObject.AddComponent<FusionOptionHover>();
                hover.OnEnter = () =>
                {
                    if (FusionManager.Instance != null && _buddy != null && partner != null) FusionManager.Instance.ShowLink(_buddy, partner, partner2);
                };
                hover.OnExit = () =>
                {
                    if (FusionManager.Instance != null) FusionManager.Instance.HideLink();
                };

                _optionEntries.Add(entry);
            }
        }

        // Tri-Optionen: flachere Zeile, kleineres Porträt, etwas kleinere Schrift
        private void MakeCompact(Button button, Image icon, TextMeshProUGUI label)
        {
            var row = button.GetComponent<LayoutElement>();
            if (row != null) row.minHeight = TriOptionHeight;
            var hlg = button.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null) hlg.padding = new RectOffset(hlg.padding.left, hlg.padding.right, 4, 4);
            var iconLayout = icon != null ? icon.GetComponent<LayoutElement>() : null;
            if (iconLayout != null) iconLayout.preferredWidth = iconLayout.preferredHeight = TriOptionHeight - 12f;
            if (label != null)
            {
                label.fontSize = Mathf.Min(label.fontSize, 17f);
                label.lineSpacing = -6f;
            }
        }

        private void ClearFusionOptions()
        {
            foreach (var e in _optionEntries)
                if (e.Button != null)
                {
                    e.Button.gameObject.SetActive(false); // Destroy ist verzögert → sonst zählt die Layout-Berechnung im selben Frame noch mit
                    Destroy(e.Button.gameObject);
                }
            _optionEntries.Clear();
            _optionsKey = "";
            if (FusionManager.Instance != null) FusionManager.Instance.HideLink();
        }

        private void OnFusionOptionClicked(FusionManager.FusionOption option)
        {
            var fm = FusionManager.Instance;
            if (fm == null || _buddy == null || option.Partner == null) return;
            fm.HideLink();
            // Anfrage an den Server; bei Erfolg wird der neue Buddy beim Anfragenden ausgewählt -> HandleBuddySelected baut das Panel neu
            if (!fm.RequestFuse(_buddy, option)) Refresh();
        }

        // Werte eines Fusions-Buddys (keine Stufen -> keine Pfeile)
        private string BuildFusionStats(FusionBuddy fusion)
        {
            float dmg = fusion.GetDamageAtLevel(1);
            float rate = fusion.GetFireRateAtLevel(1);
            float range = fusion.GetRangeAtLevel(1);
            string rateLine = Line("Rate", rate, rate, false, "/s", "0.##");
            string rangeLine = Line("Reichweite", range, range, false, " m", "0.#");

            string stats;
            switch (fusion)
            {
                case LightningBuddy l:
                    stats = Line("Schaden", dmg, dmg, false, "", "0.#") + "\n"
                          + $"Sprünge: {l.GetJumps(false)} (+1 bei nassem Ziel)\n"
                          + rateLine + "\n" + rangeLine;
                    break;
                case WaterBuddy w:
                    stats = Line("Schaden", dmg, dmg, false, "", "0.#") + "\n"
                          + rateLine + "\n" + rangeLine + "\n"
                          + $"Strudel alle {w.WhirlpoolEvery} Schüsse";
                    break;
                case AirBuddy a:
                    stats = Line("Böe-Schaden", dmg, dmg, false, "", "0.#") + "\n"
                          + Line("Böe alle", Interval(rate), Interval(rate), false, " s", "0.#") + "\n"
                          + $"Aura: +{Mathf.RoundToInt(a.FireRateBonus * 100f)} % Feuerrate\n"
                          + Line("Radius", range, range, false, " m", "0.#");
                    break;
                case ShadowBuddy s:
                    stats = Line("Fluch-Schaden", dmg, dmg, false, "/s", "0.#") + "\n"
                          + $"Ziele: {s.Targets}\n"
                          + $"+{Mathf.RoundToInt(s.DamageTakenBonus * 100f)} % Schaden genommen\n"
                          + rangeLine;
                    break;
                case MagmaBuddy m:
                    float puddleDps = dmg * m.PuddleDamageFactor;
                    stats = Line("Einschlag-Schaden", dmg, dmg, false, "", "0.#") + "\n"
                          + rateLine + "\n" + rangeLine + "\n"
                          + $"Lavapfütze: {puddleDps.ToString("0.#", Inv)}/s für {m.PuddleLifetime.ToString("0.#", Inv)} s";
                    break;
                case CrystalBuddy c:
                    stats = $"Leben: {Mathf.CeilToInt(c.CurrentHP)}/{Mathf.CeilToInt(c.MaxHP)}\n"
                          + $"Frost-Aura: −{Mathf.RoundToInt(c.AuraSlow * 100f)} % Tempo\n"
                          + Line("Spott alle", Interval(rate), Interval(rate), false, " s", "0.#") + "\n"
                          + $"Splitter-Ladung: {Mathf.RoundToInt(c.Charge01 * 100f)} %\n"
                          + Line("Radius", range, range, false, " m", "0.#");
                    break;
                case VolcanoTitanBuddy v:
                    stats = $"Leben: {Mathf.CeilToInt(v.CurrentHP)}/{Mathf.CeilToInt(v.MaxHP)} (−{Mathf.RoundToInt(v.DamageReduction * 100f)} % Schaden)\n"
                          + Line("Meteor", dmg, dmg, false, "", "0.#") + $" (r {v.ImpactRadius.ToString("0.#", Inv)} m)\n"
                          + Line("Meteor alle", Interval(rate), Interval(rate), false, " s", "0.#") + "\n" + rangeLine + "\n"
                          + $"Krater: −{Mathf.RoundToInt(v.CraterSlow * 100f)} % Tempo, {v.CraterDps.ToString("0.#", Inv)}/s";
                    break;
                case StormLordBuddy st:
                    stats = Line("Blitz", dmg, dmg, false, "", "0.#") + $" (×{st.WetMultiplier.ToString("0.#", Inv)} nass)\n"
                          + Line("Wolke alle", Interval(rate), Interval(rate), false, " s", "0.#") + "\n" + rangeLine + "\n"
                          + $"Aura: −{Mathf.RoundToInt(st.AuraSlow * 100f)} % Tempo in {st.AuraRadius.ToString("0.#", Inv)} m";
                    break;
                case PhoenixBuddy ph:
                    stats = Line("Sturzflug", dmg, dmg, false, "", "0.#") + $" + Brand {ph.DiveBurnDps.ToString("0.#", Inv)}/s\n"
                          + Line("Sturzflug alle", Interval(rate), Interval(rate), false, " s", "0.#") + "\n"
                          + $"Aura: +{Mathf.RoundToInt(ph.DamageBonus * 100f)} % Schaden in {ph.AuraRadius.ToString("0.#", Inv)} m\n"
                          + (ph.RebirthUsed ? "Wiedergeburt: in dieser Welle verbraucht" : "Wiedergeburt: bereit");
                    break;
                case WorldTreeBuddy wt:
                    stats = $"Leben: {Mathf.CeilToInt(wt.CurrentHP)}/{Mathf.CeilToInt(wt.MaxHP)} (−{Mathf.RoundToInt(wt.DamageReduction * 100f)} % Schaden)\n"
                          + Line("Wurzeln", dmg, dmg, false, "", "0.#") + $" · {wt.MaxRootTargets} Ziele · {wt.RootDuration.ToString("0.#", Inv)} s\n"
                          + $"Heilung: {Mathf.RoundToInt(wt.HealPercent * 100f)} % + {wt.HealFlat.ToString("0.#", Inv)} pro s\n"
                          + rangeLine;
                    break;
                default:
                    stats = Line("Schaden", dmg, dmg, false, "", "0.#") + "\n" + rateLine + "\n" + rangeLine;
                    break;
            }
            return stats + AuraLine();
        }

        // Buffs durch Nachbarn: Rückenwind (Luft), Phönix-Aura, Steinhaut (Bergkönig), Schild (Sonnenerzengel)
        private string AuraLine()
        {
            string hex = ColorUtility.ToHtmlStringRGB(NextValueColor);
            string text = "";
            float m = AirBuddy.GetFireRateMultiplier(_buddy);
            if (m > 1.0001f) text += $"\n<color=#{hex}>Rückenwind: +{Mathf.RoundToInt((m - 1f) * 100f)} % Feuerrate</color>";
            float p = PhoenixBuddy.GetDamageMultiplier(_buddy);
            if (p > 1.0001f) text += $"\n<color=#{hex}>Phönix-Glut: +{Mathf.RoundToInt((p - 1f) * 100f)} % Schaden</color>";
            float t = TankBuddy.GetDamageTakenMultiplier(_buddy);
            if (t < 0.9999f) text += $"\n<color=#{hex}>Steinhaut: −{Mathf.RoundToInt((1f - t) * 100f)} % Schaden</color>";
            float shield = _buddy.ShieldAmount;
            if (shield > 0f) text += $"\n<color=#{hex}>Schild: {Mathf.CeilToInt(shield)}</color>";
            return text;
        }

        // Stufe-4-Bonus (Perk) eines Basis-Buddys: aktiv bzw. Vorschau beim Aufwerten 3 → 4
        private static string PerkText(ElementalBuddy b)
        {
            switch (b)
            {
                case ShooterBuddy s when s.ElementIndex == 0:
                    return $"Durchschlag: +{s.Stage4PierceCount} Gegner, Brand {s.Stage4BurnDps.ToString("0.#", Inv)}/s für {s.Stage4BurnDuration.ToString("0.#", Inv)} s";
                case ShooterBuddy s when s.ElementIndex == 1:
                    return $"Jeder {s.Stage4FreezeEvery}. Schuss friert {s.Stage4FreezeDuration.ToString("0.#", Inv)} s ein";
                case TankBuddy t:
                    return $"Steinhaut: Buddies im Radius −{Mathf.RoundToInt(t.Stage4StoneSkinReduction * 100f)} % Schaden,\nSpott-Radius ×{t.Stage4TauntRadiusFactor.ToString("0.##", Inv)}";
                case HealerBuddy h:
                    return $"Segen ×{h.Stage4HealMultiplier.ToString("0.#", Inv)}, geheilte Buddies erhalten\n{h.Stage4ShieldAmount.ToString("0", Inv)} Schild für {h.Stage4ShieldDuration.ToString("0.#", Inv)} s";
            }
            return null;
        }

        private static T FindChild<T>(Transform root, string childName) where T : Component
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == childName) return t.GetComponent<T>();
            return null;
        }

        private string BuildStats(bool canUpgrade, int level, int next)
        {
            if (_buddy is TankBuddy)
            {
                // Tank: Damage = Aura-DPS, FireRate = Spott-Rate, Range = Spott-/Aura-Radius
                return Line("Aura-Schaden", _buddy.GetDamageAtLevel(level), _buddy.GetDamageAtLevel(next), canUpgrade, "/s", "0.#") + "\n"
                     + Line("Spott alle", Interval(_buddy.GetFireRateAtLevel(level)), Interval(_buddy.GetFireRateAtLevel(next)), canUpgrade, " s", "0.#") + "\n"
                     + Line("Radius", _buddy.GetRangeAtLevel(level), _buddy.GetRangeAtLevel(next), canUpgrade, " m", "0.#")
                     + PerkLine(canUpgrade, level, next) + AuraLine();
            }

            var healer = _buddy as HealerBuddy;
            string stats = Line(healer != null ? "Strahl" : "Schaden", _buddy.GetDamageAtLevel(level), _buddy.GetDamageAtLevel(next), canUpgrade, "", "0.#") + "\n"
                 + Line(healer != null ? "Angriffsrate" : "Feuerrate", _buddy.GetFireRateAtLevel(level), _buddy.GetFireRateAtLevel(next), canUpgrade, "/s", "0.##") + "\n"
                 + Line("Reichweite", _buddy.GetRangeAtLevel(level), _buddy.GetRangeAtLevel(next), canUpgrade, " m", "0.#");
            if (healer != null)
            {
                // Segen skaliert wie der Schaden mit der Stufe
                stats += "\n" + Line("Segen", healer.GetBlessHealAtLevel(level), healer.GetBlessHealAtLevel(next), canUpgrade, $" HP / {healer.BlessInterval:0.#} s", "0.#");
            }
            var shooter = _buddy as ShooterBuddy;
            if (shooter != null && (shooter.IsSniperAtLevel(level) || (canUpgrade && shooter.IsSniperAtLevel(next))))
            {
                // Scharfschütze: Zielwahl und Boss-Bonus (beim Aufwerten als Vorschau);
                // zweizeilig und kleiner (StatsText ist nur 300 px breit)
                int bonus = Mathf.RoundToInt((shooter.SniperBossDamageMultiplier - 1f) * 100f);
                stats += "\n<size=80%>" + (shooter.IsSniperAtLevel(level)
                    ? $"<color=#B4500A>Scharfschütze: Bosse & Fernkämpfer zuerst,\n+{bonus} % Schaden gegen Bosse</color>"
                    : $"<color=#3E7A26>Ab Stufe {shooter.SniperFromLevel}: Scharfschütze – Bosse &\nFernkämpfer zuerst, +{bonus} % gegen Bosse</color>") + "</size>";
            }
            return stats + PerkLine(canUpgrade, level, next) + AuraLine();
        }

        private string PerkLine(bool canUpgrade, int level, int next)
        {
            string perk = PerkText(_buddy);
            if (perk == null) return "";
            if (level >= ElementalBuddy.PerkLevel) return $"\n<size=80%><color=#B4500A>Stufe 4: {perk}</color></size>";
            if (canUpgrade && next == ElementalBuddy.PerkLevel) return $"\n<size=80%><color=#3E7A26>Ab Stufe 4: {perk}</color></size>";
            if (level == ElementalBuddy.PerkLevel - 1 && _buddy.IsStage4Locked)
                return $"\n<size=80%><color=#7A6A55>Gesperrt: {Progression.RequirementText(Progression.Stage4Unlock(_buddy.ElementIndex))}</color></size>";
            return "";
        }

        private static Sprite PickSprite(Sprite[] sprites, int idx)
        {
            return sprites != null && idx >= 0 && idx < sprites.Length ? sprites[idx] : null;
        }

        private static float Interval(float rate) => rate > 0f ? 1f / rate : 0f;

        // "Schaden: 12 → 16.2" (Pfeil + nächster Wert nur, wenn aufwertbar)
        private string Line(string label, float current, float nextValue, bool showNext, string unit, string format)
        {
            string text = $"{label}: {current.ToString(format, Inv)}{unit}";
            if (showNext)
            {
                string hex = ColorUtility.ToHtmlStringRGB(NextValueColor);
                text += $" {Arrow} <color=#{hex}>{nextValue.ToString(format, Inv)}{unit}</color>";
            }
            return text;
        }
    }
}
