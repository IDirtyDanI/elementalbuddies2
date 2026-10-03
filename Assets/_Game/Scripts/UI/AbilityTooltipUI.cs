using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ElementalBuddies
{
    // Hover-Tooltip (Pergament) für die Slots der AbilityBar: Name, Taste, Mana, Abklingzeit und Beschreibung.
    // Werte kommen live aus PlayerAbilities (inkl. Schadens-Upgrades). Hover-Erkennung per Rect-Test,
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
            if (a == null) return;

            string key = KeyName(id);
            if (TitleText != null) TitleText.text = Title(id);
            if (MetaText != null)
                MetaText.text = $"{key}   •   <color=#2f5f9e>{Mathf.CeilToInt(a.GetManaCost(id))} Mana</color>   •   {Fmt(a.GetCooldownDuration(id))} s Abklingzeit";
            if (BodyText != null) BodyText.text = Describe(id, a);

            if (LockedText != null)
            {
                bool locked = !a.IsUnlocked(id);
                LockedText.gameObject.SetActive(locked);
                if (locked)
                {
                    int element = PlayerAbilities.ElementIndexOf(id);
                    LockedText.text = $"Gesperrt – nimm den {ElementInfo.ShrineName(element)} ein, um diesen Zauber freizuschalten.";
                }
            }
        }

        private static string KeyName(AbilityId id)
        {
            switch (id)
            {
                case AbilityId.ArcaneBall: return "Linke Maustaste";
                case AbilityId.Blink: return "Rechte Maustaste";
                default: return "Taste " + AbilityBarUI.DefaultKey(id);
            }
        }

        private static string Title(AbilityId id)
        {
            switch (id)
            {
                case AbilityId.ArcaneBall: return "Arkanball";
                case AbilityId.Blink: return "Blinzeln";
                default: return ElementInfo.AbilityName(PlayerAbilities.ElementIndexOf(id));
            }
        }

        private const string Hi = "<color=#8a2a12><b>";
        private const string HiEnd = "</b></color>";

        private static string V(float value) => Hi + Fmt(value) + HiEnd;

        private static string Fmt(float v)
        {
            return Mathf.Approximately(v, Mathf.Round(v)) ? Mathf.RoundToInt(v).ToString() : v.ToString("0.#");
        }

        public static string Describe(AbilityId id, PlayerAbilities a)
        {
            float dm = a.DamageMultiplier;
            switch (id)
            {
                case AbilityId.ArcaneBall:
                {
                    float dmg = 35f;
                    var ball = a.ArcaneBallPrefab != null ? a.ArcaneBallPrefab.GetComponent<ArcaneBall>() : null;
                    if (ball != null) dmg = ball.Damage;
                    return $"Schleudert eine arkane Kugel in Blickrichtung. Sie verursacht {V(dmg * dm)} Schaden am ersten getroffenen Gegner.";
                }
                case AbilityId.Blink:
                    return $"Teleportiert dich bis zu {V(a.BlinkRange)} m in Richtung Mauszeiger. Kurz nach dem Sprung bist du {V(a.InvulnerabilityDuration)} s unverwundbar. Wände halten den Sprung auf.";
                case AbilityId.FireWave:
                {
                    var s = a.FireWave;
                    return $"Eine Flammenwelle im Kegel vor dir ({V(s.ConeAngle)}°, {V(s.Range)} m). Sie verursacht {V(s.Damage * dm)} Schaden und setzt Gegner in Brand: {V(s.BurnDps * dm)} Schaden pro Sekunde für {V(s.BurnDuration)} s.";
                }
                case AbilityId.FrostNova:
                {
                    var s = a.FrostNova;
                    return $"Eisige Druckwelle um dich herum ({V(s.Radius)} m). Sie verursacht {V(s.Damage * dm)} Schaden und friert Gegner {V(s.FreezeDuration)} s komplett ein: Sie können sich weder bewegen noch angreifen.";
                }
                case AbilityId.StoneWall:
                {
                    var s = a.StoneWall;
                    return $"Lässt {V(s.Distance)} m vor dir eine {V(s.Length)} m breite Steinmauer quer zur Blickrichtung aufsteigen. Gegner müssen {V(s.Lifetime)} s lang außen herum laufen – ideal, um Engstellen zu sperren.";
                }
                case AbilityId.HolyCircle:
                {
                    var s = a.HolyCircle;
                    return $"Heiliges Licht im Umkreis von {V(s.Radius)} m. Es heilt dich um {V(s.PlayerHeal)}, Buddies um {V(s.BuddyHeal)} und den Nexus um {V(s.NexusHeal)} LP. Gegner werden geblendet (Sterne über dem Kopf) und sind {V(s.BlindDuration)} s lang um {V(s.BlindSlow * 100f)} % verlangsamt.";
                }
                default:
                    return "";
            }
        }
    }
}
