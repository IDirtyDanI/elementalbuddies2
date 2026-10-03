using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ElementalBuddies
{
    // Hover-Tooltip (Pergament) für die Slots der AbilityBar: Name, Taste, Mana, Abklingzeit und Beschreibung.
    // Werte kommen live vom aktiven ChampionKit (inkl. Schadens-Upgrades). Hover-Erkennung per Rect-Test,
    // daher brauchen die Slots keine Raycast-Targets. Script auf ein immer aktives Objekt legen.
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

        void Update()
        {
            if (Panel == null || Bar == null) return;

            AbilityBarUI.Slot slot = FindHoveredSlot();
            if (slot != _hovered)
            {
                _hovered = slot;
                _hoverTime = 0f;
            }

            bool show = false;
            if (_hovered != null)
            {
                _hoverTime += Time.unscaledDeltaTime;
                show = _hoverTime >= ShowDelay;
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
            var a = PlayerAbilities.Instance;
            if (a == null || a.ActiveKit == null) return;
            var kit = a.ActiveKit;

            AbilitySlot slot = kit.SlotOf(id);
            if (TitleText != null) TitleText.text = kit.GetName(id);
            if (MetaText != null)
                MetaText.text = $"{AbilitySlots.KeyName(slot)}   •   {kit.GetCostLine(id)}";
            if (BodyText != null) BodyText.text = Describe(id, a);

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
            return a.ActiveKit.Describe(id, a.DamageMultiplier);
        }
    }
}
