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
        private TextMeshProUGUI _mTitle, _mStatus;
        private Image _mFill, _mIcon;
        private RectTransform _mRect;
        private Vector2 _mBasePos;

        void Start()
        {
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
        }

        void Update()
        {
            Shrine s = FindAwakened();
            if (s != _shrine)
            {
                _shrine = s;
                if (Panel != null) Panel.SetActive(s != null);
                if (s != null) SetupFor(s);
            }

            if (_shrine != null) Refresh(_shrine);

            UpdateMerchant();
        }

        // ---------------- Händler ----------------

        private void UpdateMerchant()
        {
            if (MerchantPanel == null) return;
            var mgr = MerchantManager.Instance;
            Merchant m = mgr != null && mgr.ActiveMerchant != null && mgr.ActiveMerchant.IsActive ? mgr.ActiveMerchant : null;
            if (m != _merchant)
            {
                _merchant = m;
                MerchantPanel.SetActive(m != null);
                if (m != null)
                {
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
                    _mStatus.text = _merchant.ProgressSeconds > 0f ? $"Betritt den Kreis! ({t})" : "Betritt den Kreis am Stand!";
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
