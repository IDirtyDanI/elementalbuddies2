using UnityEngine;
using UnityEngine.UI;

namespace ElementalBuddies
{
    // Schwebende ✓/✗-Knöpfe am Bau-Geist (Mobil-Version, Paket B). Eigenes Overlay-Canvas mit Pixel-Maßen (Knöpfe in dp),
    // zur Laufzeit vom InteractionManager im Touch-Modus erzeugt. ✓ = bauen, ✗ = abbrechen; ungültiger Bauplatz: ✓ rot
    // (Antippen zeigt dann nur den Grund). Lage: über dem Fußpunkt des Geists, im sicheren Bereich (TouchBuildRules).
    public class TouchBuildOverlay : MonoBehaviour
    {
        // Über den Ping-Randmarken (40), unter Pause-/Kartenfenstern
        public const int SortingOrder = 41;

        public static readonly Color ValidColor = new Color(0.27f, 0.62f, 0.22f, 1f);
        public static readonly Color InvalidColor = new Color(0.72f, 0.18f, 0.13f, 1f);
        public static readonly Color CancelColor = new Color(0.36f, 0.25f, 0.16f, 1f);

        public event System.Action OnConfirm;
        public event System.Action OnCancel;

        private RectTransform _root;
        private RectTransform _confirm, _cancel;
        private Image _confirmDisc;
        private bool _shown;

        public static TouchBuildOverlay Create(Transform parent)
        {
            var go = new GameObject("TouchBuildOverlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(parent, false);
            var overlay = go.AddComponent<TouchBuildOverlay>();
            overlay.Build();
            return overlay;
        }

        private void Build()
        {
            var canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            var scaler = GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; // 1 Einheit = 1 Pixel, Größen über DpToPixels
            scaler.scaleFactor = 1f;
            _root = (RectTransform)transform;

            _confirm = MakeButton("Confirm", TouchBuildUi.CheckSprite, ValidColor, () => OnConfirm?.Invoke(), out _confirmDisc);
            _cancel = MakeButton("Cancel", TouchBuildUi.CrossSprite, CancelColor, () => OnCancel?.Invoke(), out _);
            SetVisible(false);
        }

        private RectTransform MakeButton(string name, Sprite icon, Color color, UnityEngine.Events.UnityAction onClick, out Image disc)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_root, false);
            rt.anchorMin = rt.anchorMax = Vector2.zero; // Position = Bildschirm-Pixel
            rt.pivot = new Vector2(0.5f, 0.5f);
            disc = go.GetComponent<Image>();
            disc.sprite = TouchBuildUi.DiscSprite;
            disc.color = color;
            var button = go.GetComponent<Button>();
            button.targetGraphic = disc;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(onClick);

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            var irt = (RectTransform)iconGo.transform;
            irt.SetParent(rt, false);
            irt.anchorMin = new Vector2(0.18f, 0.18f);
            irt.anchorMax = new Vector2(0.82f, 0.82f);
            irt.offsetMin = irt.offsetMax = Vector2.zero;
            var img = iconGo.GetComponent<Image>();
            img.sprite = icon;
            img.raycastTarget = false;
            return rt;
        }

        // Knöpfe am Geist zeigen; ghostScreen = Bildschirmpunkt des Geist-Fußpunkts (Pixel)
        public void Show(Vector2 ghostScreen, bool valid)
        {
            float size = MobilePlatform.DpToPixels(TouchBuildRules.ConfirmButtonDp);
            TouchBuildRules.ConfirmButtons(ghostScreen, MobilePlatform.DpToPixels(TouchBuildRules.ConfirmLiftDp), size,
                MobilePlatform.DpToPixels(TouchBuildRules.ConfirmGapDp), MobilePlatform.SafeArea, out Vector2 ok, out Vector2 no);
            var sd = new Vector2(size, size);
            _confirm.sizeDelta = sd;
            _cancel.sizeDelta = sd;
            _confirm.anchoredPosition = ok;
            _cancel.anchoredPosition = no;
            _confirmDisc.color = valid ? ValidColor : InvalidColor;
            SetVisible(true);
        }

        public void Hide() => SetVisible(false);

        private void SetVisible(bool on)
        {
            if (_shown == on && _confirm.gameObject.activeSelf == on) return;
            _shown = on;
            _confirm.gameObject.SetActive(on);
            _cancel.gameObject.SetActive(on);
        }
    }
}
