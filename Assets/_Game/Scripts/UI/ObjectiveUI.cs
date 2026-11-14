using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ElementalBuddies
{
    // Ziel-Panel (oben rechts unter der Top-Bar) für den gerade erwachten Schrein. Versteckt, solange keiner erwacht ist.
    // Ein geöffneter Händler bekommt ein zweites Panel (Kopie von Panel, zur Laufzeit erzeugt) – darunter, oder
    // an erster Stelle, wenn gerade kein Schrein erwacht ist.
    public class ObjectiveUI : MonoBehaviour
    {
        [Tooltip("Wird ein-/ausgeblendet (sollte NICHT das Objekt mit diesem Script sein).")]
        public GameObject Panel;
        public TextMeshProUGUI TitleText;
        public TextMeshProUGUI StatusText;
        [Tooltip("Image vom Typ Filled (Horizontal).")]
        public Image ProgressFill;
        [Tooltip("Optional: alternativ zum Fill-Image.")]
        public Slider ProgressSlider;
        public Image Icon;
        [Tooltip("Feuer, Eis, Erde, Licht")]
        public Sprite[] ElementIcons = new Sprite[4];

        [Header("Farben")]
        public bool TintFillWithElement = true;
        public Color StatusNormal = new Color(0.25f, 0.18f, 0.1f);
        public Color StatusWarning = new Color(0.75f, 0.15f, 0.1f);

        [Header("Händler")]
        [Tooltip("Optional: eigenes Panel für den Händler (leer = Kopie von Panel).")]
        public GameObject MerchantPanel;
        [Tooltip("Abstand des Händler-Panels unter dem Schrein-Panel (Canvas-Einheiten).")]
        public float MerchantPanelSpacing = 160f;

        private Shrine _shrine;
        private Merchant _merchant;
        private string _mWhere;
        private TextMeshProUGUI _mTitle, _mStatus;
        private Image _mFill, _mIcon;
        private RectTransform _mRect;
        private Vector2 _mBasePos;

        // Dimensionsriss: drittes Panel (Kopie von Panel), steht unter Schrein/Händler
        private GameObject _dPanel;
        private TextMeshProUGUI _dTitle, _dStatus;
        private Image _dFill, _dIcon;
        private RectTransform _dRect;
        private Vector2 _dBasePos;
        private bool _dShown;

        void Start()
        {
            // Lange Titel/Status (Händler-Ortsangabe „… vor der Nordmauer!“, Umkämpft + Zeit) dürfen nicht aus dem
            // Pergament ragen: einzeilig, Schrift schrumpft bei Bedarf (vor dem Kopieren → gilt auch fürs Händler-Panel)
            FitToBox(TitleText);
            FitToBox(StatusText);
            if (Panel != null) Panel.SetActive(false);
            if (MerchantPanel == null && Panel != null)
            {
                MerchantPanel = Instantiate(Panel, Panel.transform.parent);
                MerchantPanel.name = "MerchantPanel";
            }
            if (MerchantPanel != null)
            {
                _mTitle = FindChild<TextMeshProUGUI>(MerchantPanel.transform, "Title");
                _mStatus = FindChild<TextMeshProUGUI>(MerchantPanel.transform, "Status");
                _mFill = FindChild<Image>(MerchantPanel.transform, "Fill");
                _mIcon = FindChild<Image>(MerchantPanel.transform, "Icon");
                _mRect = MerchantPanel.transform as RectTransform;
                if (_mRect != null) _mBasePos = _mRect.anchoredPosition;
                MerchantPanel.SetActive(false);
            }
            if (Panel != null)
            {
                _dPanel = Instantiate(Panel, Panel.transform.parent);
                _dPanel.name = "DimensionPanel";
                _dTitle = FindChild<TextMeshProUGUI>(_dPanel.transform, "Title");
                _dStatus = FindChild<TextMeshProUGUI>(_dPanel.transform, "Status");
                _dFill = FindChild<Image>(_dPanel.transform, "Fill");
                _dIcon = FindChild<Image>(_dPanel.transform, "Icon");
                _dRect = _dPanel.transform as RectTransform;
                if (_dRect != null) _dBasePos = _dRect.anchoredPosition;
                if (_dIcon != null) _dIcon.enabled = false;
                _dPanel.SetActive(false);
            }
        }

        void Update()
        {
            // In der Dimension sind die Ziele der Karte (Schrein, Händler) ausgeblendet (#562)
            Shrine s = DimensionEvent.LocalInArena ? null : FindAwakened();
            if (s != _shrine)
            {
                _shrine = s;
                if (Panel != null) Panel.SetActive(s != null);
                if (s != null) SetupFor(s);
            }

            if (_shrine != null) Refresh(_shrine);

            UpdateMerchant();
            UpdateDimension();
        }

        // ---------------- Dimensionsriss ----------------

        // Draußen: „Betretet das Portal“ + Countdown bis zum Verfall bzw. „Der Riss zehrt am Nexus!“ (Balken = Boss-Leben).
        // Drinnen: „Erschlagt den {Boss}“ + Nexus-Leben (Heimatfront im Blick); nach dem Boss-Tod der Weg zurück.
        private void UpdateDimension()
        {
            if (_dPanel == null) return;
            var ev = DimensionEvent.Instance;
            bool inArena = DimensionEvent.LocalInArena;
            bool show = ev != null && (ev.Phase == DimensionPhase.Open || (ev.Phase == DimensionPhase.BossDead && inArena));
            if (show != _dShown)
            {
                _dShown = show;
                _dPanel.SetActive(show);
            }
            if (!show) return;

            if (_dRect != null)
            {
                int above = (_shrine != null ? 1 : 0) + (_merchant != null ? 1 : 0);
                _dRect.anchoredPosition = _dBasePos + Vector2.down * MerchantPanelSpacing * above;
            }

            Color rift = DimensionEvent.RiftColor;
            string title, status;
            Color statusColor = StatusNormal;
            float fill;
            Color fillColor = rift;
            var nexus = Nexus.Instance;
            float nexus01 = nexus != null && nexus.MaxHP > 0f ? Mathf.Clamp01(nexus.CurrentHP / nexus.MaxHP) : 1f;
            if (ev.Phase == DimensionPhase.BossDead)
            {
                title = "Der Riss schließt sich";
                status = $"Zurück durch das Portal ({ev.Countdown} s)";
                fill = nexus01;
            }
            else if (inArena)
            {
                title = $"Erschlagt den {DimensionEvent.BossName}";
                status = nexus != null ? $"Nexus: {Mathf.CeilToInt(nexus.CurrentHP)} / {Mathf.RoundToInt(nexus.MaxHP)}" : "";
                if (nexus01 < 0.35f) statusColor = StatusWarning;
                fill = nexus01;
                fillColor = Color.Lerp(new Color(0.85f, 0.2f, 0.15f), new Color(0.35f, 0.75f, 1f), nexus01);
            }
            else
            {
                title = "Dimensionsriss – Betretet das Portal";
                var boss = ev.FindBoss();
                fill = boss != null && boss.MaxHP > 0f ? Mathf.Clamp01(boss.CurrentHP / boss.MaxHP) : 1f;
                switch (ev.Drain)
                {
                    case DimensionEventRules.DrainPhase.Grace:
                        status = $"Der Riss zehrt in {ev.Countdown} s am Nexus!";
                        if (ev.Countdown <= 10) statusColor = StatusWarning;
                        break;
                    case DimensionEventRules.DrainPhase.Draining:
                        status = "Der Riss zehrt am Nexus!";
                        statusColor = StatusWarning;
                        break;
                    case DimensionEventRules.DrainPhase.Held:
                        status = $"Ein Champion kämpft im Riss – {DimensionEvent.BossName}";
                        break;
                    default:
                        status = "";
                        break;
                }
            }
            // Halten im Portal-Kreis (#569): Fortschritt statt der üblichen Zeile
            float hold = DimensionEvent.HoldProgress;
            if (hold > 0f)
            {
                status = hold >= 1f ? "Durchschreiten …" : $"Durchschreiten … {Mathf.RoundToInt(hold * 100f)} %";
                statusColor = StatusNormal;
                fill = hold;
                fillColor = Color.Lerp(rift, Color.white, 0.35f);
            }
            if (_dTitle != null) _dTitle.text = title;
            if (_dStatus != null) { _dStatus.text = status; _dStatus.color = statusColor; }
            if (_dFill != null) { _dFill.fillAmount = fill; _dFill.color = fillColor; }
        }

        // ---------------- Händler ----------------

        private void UpdateMerchant()
        {
            if (MerchantPanel == null) return;
            var mgr = MerchantManager.Instance;
            Merchant m = mgr != null && mgr.ActiveMerchant != null && mgr.ActiveMerchant.IsActive ? mgr.ActiveMerchant : null;
            if (DimensionEvent.LocalInArena) m = null;
            if (m != _merchant)
            {
                _merchant = m;
                MerchantPanel.SetActive(m != null);
                if (m != null)
                {
                    _mWhere = m.LocationPhrase;
                    if (_mTitle != null) _mTitle.text = $"{m.DisplayName} ist da!";
                    if (_mIcon != null)
                    {
                        Sprite sp = mgr != null ? mgr.GetEmblem(m.Kind) : null;
                        _mIcon.sprite = sp;
                        _mIcon.enabled = sp != null;
                    }
                    if (_mFill != null) _mFill.color = m.Color;
                }
            }
            if (_merchant == null) return;

            // Unter das Schrein-Panel rücken, falls beide offen sind
            if (_mRect != null) _mRect.anchoredPosition = _mBasePos + (_shrine != null ? Vector2.down * MerchantPanelSpacing : Vector2.zero);

            if (_mStatus != null)
            {
                string t = $"{Mathf.FloorToInt(_merchant.ProgressSeconds)} / {Mathf.RoundToInt(_merchant.RequiredTime)} s";
                if (!_merchant.PlayerInside)
                {
                    _mStatus.text = _merchant.ProgressSeconds > 0f ? $"Betritt den Kreis! ({t})" : $"Betritt den Kreis {_mWhere}!";
                    _mStatus.color = StatusWarning;
                }
                else if (_merchant.Contested)
                {
                    _mStatus.text = $"Umkämpft – langsamer! {t}";
                    _mStatus.color = StatusWarning;
                }
                else
                {
                    _mStatus.text = $"Halte den Kreis: {t}";
                    _mStatus.color = StatusNormal;
                }
            }
            if (_mFill != null) _mFill.fillAmount = _merchant.Progress01;
        }

        private static void FitToBox(TextMeshProUGUI t)
        {
            if (t == null) return;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.fontSizeMax = t.fontSize;
            t.fontSizeMin = Mathf.Round(t.fontSize * 0.7f);
            t.enableAutoSizing = true;
        }

        private static T FindChild<T>(Transform root, string childName) where T : Component
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == childName) return t.GetComponent<T>();
            return null;
        }

        private static Shrine FindAwakened()
        {
            var mgr = ShrineManager.Instance;
            if (mgr != null && mgr.ActiveShrine != null && mgr.ActiveShrine.IsAwakened) return mgr.ActiveShrine;
            var all = Shrine.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].IsAwakened) return all[i];
            return null;
        }

        private void SetupFor(Shrine s)
        {
            if (TitleText != null) TitleText.text = $"{s.DisplayName} erwacht!";

            if (Icon != null)
            {
                Sprite sp = ElementIcons != null && s.ElementIndex < ElementIcons.Length ? ElementIcons[s.ElementIndex] : null;
                Icon.sprite = sp;
                Icon.enabled = sp != null;
            }

            if (ProgressFill != null && TintFillWithElement) ProgressFill.color = ElementInfo.GetColor(s.ElementIndex);
        }

        private void Refresh(Shrine s)
        {
            if (StatusText != null)
            {
                if (!s.PlayerInside)
                {
                    StatusText.text = s.ProgressSeconds > 0f
                        ? $"Betritt den Kreis! ({Mathf.FloorToInt(s.ProgressSeconds)} / {Mathf.RoundToInt(s.RequiredTime)} s)"
                        : "Betritt den Kreis!";
                    StatusText.color = StatusWarning;
                }
                else if (s.Contested)
                {
                    StatusText.text = $"Umkämpft – langsamer! {Mathf.FloorToInt(s.ProgressSeconds)} / {Mathf.RoundToInt(s.RequiredTime)} s";
                    StatusText.color = StatusWarning;
                }
                else
                {
                    StatusText.text = $"Halte den Kreis: {Mathf.FloorToInt(s.ProgressSeconds)} / {Mathf.RoundToInt(s.RequiredTime)} s";
                    StatusText.color = StatusNormal;
                }
            }

            if (ProgressFill != null) ProgressFill.fillAmount = s.Progress01;
            if (ProgressSlider != null)
            {
                ProgressSlider.minValue = 0f;
                ProgressSlider.maxValue = 1f;
                ProgressSlider.value = s.Progress01;
            }
        }
    }
}
