using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ElementalBuddies
{
    // Hover-Tooltip (Pergament) für die Slots der AbilityBar: Name, Taste, Mana, Abklingzeit und Beschreibung.
    // Werte kommen live vom aktiven ChampionKit (inkl. Schadens-Upgrades). Hover-Erkennung per Rect-Test,
    // daher brauchen die Slots keine Raycast-Targets. Script auf ein immer aktives Objekt legen.
    // Touch (Paket U): langes Drücken (≥ 0,4 s, MobileUiMath.LongPressSeconds) auf einen sichtbaren Slot zeigt den Tooltip,
    // Loslassen schließt ihn. Eigene Touch-Knöpfe können ihn über ShowFor/HideExternal anzeigen.
    public class AbilityTooltipUI : MonoBehaviour
    {
        public AbilityBarUI Bar;
        [Tooltip("Wird ein-/ausgeblendet (sollte NICHT das Objekt mit diesem Script sein).")]
        public RectTransform Panel;
        public TextMeshProUGUI TitleText;
        public TextMeshProUGUI MetaText;
        public TextMeshProUGUI BodyText;
        public TextMeshProUGUI LockedText;
        [Tooltip("Abstand zwischen Slot-Oberkante und Tooltip (Canvas-Einheiten).")]
        public float Gap = 14f;
        public float ShowDelay = 0.12f;
        public float FadeSpeed = 12f;

        private CanvasGroup _group;
        private AbilityBarUI.Slot _hovered;
        private float _hoverTime;
        private Canvas _canvas;

        void Start()
        {
            if (Bar == null) Bar = FindFirstObjectByType<AbilityBarUI>();
            if (Panel != null)
            {
                _group = Panel.GetComponent<CanvasGroup>();
                if (_group == null) _group = Panel.gameObject.AddComponent<CanvasGroup>();
                _group.blocksRaycasts = false;
                _group.interactable = false;
                _group.alpha = 0f;
                _canvas = Panel.GetComponentInParent<Canvas>();
            }
        }

        public static AbilityTooltipUI Instance { get; private set; }
        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        // Von außen (Touch-Fähigkeitsknöpfe): Tooltip für eine Fähigkeit über anchor zeigen, bis HideExternal
        private bool _external;
        private AbilityId _externalId;
        private RectTransform _externalAnchor;

        public void ShowFor(AbilityId id, RectTransform anchor)
        {
            _external = anchor != null;
            _externalId = id;
            _externalAnchor = anchor;
        }

        public void HideExternal()
        {
            _external = false;
            _externalAnchor = null;
        }

        void Update()
        {
            if (Panel == null || _group == null) return;
            if (_external && _externalAnchor != null && _externalAnchor.gameObject.activeInHierarchy)
            {
                Fill(_externalId);
                Place(_externalAnchor);
                _group.alpha = Mathf.MoveTowards(_group.alpha, 1f, FadeSpeed * Time.unscaledDeltaTime);
                return;
            }
            if (Bar == null) { _group.alpha = Mathf.MoveTowards(_group.alpha, 0f, FadeSpeed * Time.unscaledDeltaTime); return; }

            AbilityBarUI.Slot slot = MobilePlatform.TouchMode ? FindHeldSlot() : FindHoveredSlot();
            if (slot != _hovered)
            {
                _hovered = slot;
                _hoverTime = 0f;
            }

            bool show = false;
            if (_hovered != null)
            {
                _hoverTime += Time.unscaledDeltaTime;
                show = MobilePlatform.TouchMode || _hoverTime >= ShowDelay; // Touch: Haltezeit schon erreicht
                if (show)
                {
                    Fill(_hovered.Ability);
                    Place(_hovered.Root);
                }
            }

            float target = show ? 1f : 0f;
            _group.alpha = Mathf.MoveTowards(_group.alpha, target, FadeSpeed * Time.unscaledDeltaTime);
        }

        private AbilityBarUI.Slot FindHoveredSlot()
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null) return null;
            Vector2 pos = mouse.position.ReadValue();
            Camera cam = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay ? _canvas.worldCamera : null;
            foreach (var s in Bar.Slots)
                if (s.Root != null && s.Root.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(s.Root, pos, cam))
                    return s;
            return null;
        }

        // Touch: Slot, auf dem ein Finger lange (≥ 0,4 s) liegt
        private AbilityBarUI.Slot FindHeldSlot()
        {
            Camera cam = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay ? _canvas.worldCamera : null;
            if (!MobileUi.TryGetHeldPointer(p => SlotAt(p, cam) != null, MobileUiMath.LongPressSeconds, out var pos)) return null;
            return SlotAt(pos, cam);
        }

        private AbilityBarUI.Slot SlotAt(Vector2 pos, Camera cam)
        {
            foreach (var s in Bar.Slots)
                if (s.Root != null && s.Root.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(s.Root, pos, cam))
                    return s;
            return null;
        }

        // Tooltip mittig über dem Slot, am Bildschirmrand eingeklemmt
        private void Place(RectTransform slotRoot)
        {
            if (slotRoot == null) return;
            LayoutRebuilder.ForceRebuildLayoutImmediate(Panel);

            var parent = (RectTransform)Panel.parent;
            Vector3[] corners = new Vector3[4];
            slotRoot.GetWorldCorners(corners);
            Vector3 topCenterWorld = (corners[1] + corners[2]) * 0.5f;
            Vector2 local = parent.InverseTransformPoint(topCenterWorld);

            Panel.pivot = new Vector2(0.5f, 0f);
            Vector2 size = Panel.rect.size;
            Rect bounds = parent.rect;
            float x = Mathf.Clamp(local.x, bounds.xMin + size.x * 0.5f + 8f, bounds.xMax - size.x * 0.5f - 8f);
            float y = local.y + Gap;
            Panel.anchorMin = Panel.anchorMax = new Vector2(0.5f, 0.5f);
            Panel.localPosition = new Vector3(x, y, 0f);
        }

        private void Fill(AbilityId id)
        {
            var av = PlayerAvatar.Local;
            var a = av != null && av.Abilities != null ? av.Abilities : PlayerAbilities.Instance;
            if (a == null || a.ActiveKit == null) return;
            var kit = a.ActiveKit;

            AbilitySlot slot = kit.SlotOf(id);
            if (TitleText != null) TitleText.text = kit.GetName(id);
            if (MetaText != null)
                MetaText.text = $"{AbilitySlots.KeyName(slot)}   •   {kit.GetCostLine(id)}";
            if (BodyText != null) BodyText.text = MobileUi.TouchText(Describe(id, a)); // Touch: „Zielpunkt“ statt „Mauszeiger“

            if (LockedText != null)
            {
                bool locked = !a.IsUnlocked(id);
                LockedText.gameObject.SetActive(locked);
                if (locked)
                {
                    int element = PlayerAbilities.ElementIndexOf(id);
                    LockedText.text = $"Gesperrt – nimm den {ElementInfo.ShrineName(element)} ein, um diese Fähigkeit freizuschalten.";
                }
            }
        }

        // Beschreibung mit Live-Werten (inkl. Schadens-Upgrades) – kommt vom aktiven Champion-Kit
        public static string Describe(AbilityId id, PlayerAbilities a)
        {
            if (a == null || a.ActiveKit == null) return "";
            return a.ActiveKit.Describe(id, a.GetDamageMultiplier(id));
        }
    }
}
