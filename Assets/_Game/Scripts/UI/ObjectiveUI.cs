using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ElementalBuddies
{
    // Ziel-Panel (oben rechts unter der Top-Bar) für den gerade erwachten Schrein. Versteckt, solange keiner erwacht ist.
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

        private Shrine _shrine;

        void Start()
        {
            if (Panel != null) Panel.SetActive(false);
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
