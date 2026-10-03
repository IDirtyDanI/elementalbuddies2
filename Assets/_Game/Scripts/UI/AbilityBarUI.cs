using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;
using TMPro;

namespace ElementalBuddies
{
    // Fähigkeitenleiste unten links: LMB, RMB, R, F, C, V – Inhalt kommt vom aktiven Champion (ChampionKit).
    // Zeigt Abklingzeit (Radial + Sekunden), Mana-Kosten, Sperre, Mana-Mangel, Puls beim Freischalten,
    // Aufblitzen beim Wirken, Aufladungs-Punkte (z. B. Rolle) und einen Leuchtrahmen für gehaltene/aktive Fähigkeiten (Block).
    public class AbilityBarUI : MonoBehaviour
    {
        [System.Serializable]
        public class Slot
        {
            [FormerlySerializedAs("Ability")]
            [Tooltip("Taste dieses Slots. Welche Fähigkeit darauf liegt, bestimmt der aktive Champion.")]
            public AbilitySlot Binding;
            [Tooltip("Shown in KeyText. Empty = default key for the slot.")]
            public string KeyLabel;

            public RectTransform Root;
            public Image Icon;
            [Tooltip("Image Type Filled, Radial 360. fillAmount = remaining / duration.")]
            public Image CooldownOverlay;
            public TextMeshProUGUI CooldownText;
            public TextMeshProUGUI KeyText;
            public TextMeshProUGUI ManaCostText;
            [Tooltip("Shown while the spell is locked (padlock / dark overlay).")]
            public GameObject LockedOverlay;
            [Tooltip("Optional image flashed briefly on cast (e.g. a white/gold frame). Its alpha is animated.")]
            public Image CastHighlight;

            // Laufzeit: aktuell belegte Fähigkeit
            [System.NonSerialized] public AbilityId Ability;
            [System.NonSerialized] public Sprite DefaultIcon;
            [System.NonSerialized] public float PulseTime = -1f;
            [System.NonSerialized] public float FlashTime = -1f;
            [System.NonSerialized] public Vector3 BaseScale = Vector3.one;
            [System.NonSerialized] public RectTransform ChargesRoot;
            [System.NonSerialized] public readonly List<Image> Pips = new List<Image>();
            [System.NonSerialized] public readonly List<Image> PipFills = new List<Image>();
            [System.NonSerialized] public TextMeshProUGUI ChargeCount;
            [System.NonSerialized] public Image ActiveFrame;
        }

        [Tooltip("Optional explicit reference; falls back to PlayerAbilities.Instance.")]
        public PlayerAbilities Abilities;

        public List<Slot> Slots = new List<Slot>
        {
            new Slot { Binding = AbilitySlot.Primary },
            new Slot { Binding = AbilitySlot.Secondary },
            new Slot { Binding = AbilitySlot.Fire },
            new Slot { Binding = AbilitySlot.Ice },
            new Slot { Binding = AbilitySlot.Earth },
            new Slot { Binding = AbilitySlot.Light },
        };

        [Header("Colors")]
        public Color ReadyTint = Color.white;
        public Color NotEnoughManaTint = new Color(0.45f, 0.55f, 0.85f, 1f);
        public Color LockedTint = new Color(0.3f, 0.3f, 0.3f, 1f);
        public Color ManaCostAffordable = new Color(0.55f, 0.8f, 1f);
        public Color ManaCostTooExpensive = new Color(0.9f, 0.3f, 0.3f);

        [Header("Aufladungen / Aktiv-Zustand")]
        [Tooltip("Sprite der Aufladungs-Punkte (rund). Leer = Quadrat.")]
        public Sprite PipSprite;
        public Color PipFull = new Color(1f, 0.85f, 0.35f, 1f);
        public Color PipEmpty = new Color(0.15f, 0.1f, 0.06f, 0.85f);
        public float PipSize = 16f;
        [Tooltip("Rahmen-Farbe für gehaltene/aktive Fähigkeiten (Schildblock, Lichtschwur, Rolle).")]
        public Color ActiveFrameColor = new Color(1f, 0.8f, 0.25f, 1f);

        [Header("Animation")]
        public float UnlockPulseDuration = 0.45f;
        public float UnlockPulseScale = 1.35f;
        public float CastFlashDuration = 0.25f;
        public float CastPunchScale = 1.12f;

        private PlayerAbilities _bound;

        void Start()
        {
            foreach (var s in Slots)
            {
                if (s.Root != null) s.BaseScale = s.Root.localScale;
                if (s.Icon != null) s.DefaultIcon = s.Icon.sprite;
                if (s.KeyText != null) s.KeyText.text = string.IsNullOrEmpty(s.KeyLabel) ? DefaultKey(s.Binding) : s.KeyLabel;
                if (s.CastHighlight != null) SetAlpha(s.CastHighlight, 0f);
            }
            TryBind();
        }

        void OnDestroy()
        {
            Unbind();
        }

        private void TryBind()
        {
            var a = Abilities != null ? Abilities : PlayerAbilities.Instance;
            if (a == null || a == _bound) return;
            Unbind();
            _bound = a;
            _bound.OnAbilityUnlocked += HandleUnlocked;
            _bound.OnAbilityCast += HandleCast;
            _bound.OnChampionChanged += HandleChampionChanged;
            RefreshChampion();
        }

        private void Unbind()
        {
            if (_bound == null) return;
            _bound.OnAbilityUnlocked -= HandleUnlocked;
            _bound.OnAbilityCast -= HandleCast;
            _bound.OnChampionChanged -= HandleChampionChanged;
            _bound = null;
        }

        private void HandleChampionChanged(ChampionClass cls)
        {
            RefreshChampion();
        }

        // Icons, Belegung und Aufladungs-Punkte für den aktiven Champion setzen
        public void RefreshChampion()
        {
            if (_bound == null) return;
            foreach (var s in Slots)
            {
                s.Ability = _bound.GetAbility(s.Binding);
                if (s.Icon != null)
                {
                    Sprite icon = _bound.GetAbilityIcon(s.Ability);
                    s.Icon.sprite = icon != null ? icon : s.DefaultIcon;
                }
                BuildCharges(s, _bound.GetMaxCharges(s.Ability));
                EnsureActiveFrame(s);
            }
        }

        private void HandleUnlocked(int elementIndex)
        {
            AbilitySlot slot = AbilitySlots.OfElement(elementIndex);
            foreach (var s in Slots)
                if (s.Binding == slot) s.PulseTime = 0f;
        }

        private void HandleCast(AbilityId id)
        {
            foreach (var s in Slots)
                if (s.Ability == id) s.FlashTime = 0f;
        }

        void Update()
        {
            if (_bound == null) TryBind();
            if (_bound == null) return;

            float mana = EconomyManager.Instance != null ? EconomyManager.Instance.CurrentMana : 0f;
            float dt = Time.unscaledDeltaTime;
            var kit = _bound.ActiveKit;

            foreach (var s in Slots)
            {
                AbilityId id = _bound.GetAbility(s.Binding);
                if (id != s.Ability) RefreshChampion();

                bool unlocked = _bound.IsUnlocked(id);
                float required = kit != null ? kit.GetRequiredMana(id) : _bound.GetManaCost(id);
                float remaining = _bound.GetCooldownRemaining(id);
                float duration = _bound.GetCooldownDisplayDuration(id);
                bool enoughMana = mana >= required;
                bool active = _bound.IsAbilityActive(id);

                if (s.LockedOverlay != null && s.LockedOverlay.activeSelf == unlocked) s.LockedOverlay.SetActive(!unlocked);

                if (s.Icon != null)
                    s.Icon.color = !unlocked ? LockedTint : (enoughMana || active ? ReadyTint : NotEnoughManaTint);

                bool onCooldown = unlocked && remaining > 0f && duration > 0f;
                if (s.CooldownOverlay != null)
                {
                    s.CooldownOverlay.fillAmount = onCooldown ? Mathf.Clamp01(remaining / duration) : 0f;
                }
                if (s.CooldownText != null)
                {
                    bool show = onCooldown && remaining >= 0.15f;
                    if (s.CooldownText.gameObject.activeSelf != show) s.CooldownText.gameObject.SetActive(show);
                    if (show) s.CooldownText.text = remaining >= 1f ? Mathf.CeilToInt(remaining).ToString() : remaining.ToString("0.0");
                }
                if (s.ManaCostText != null)
                {
                    s.ManaCostText.text = kit != null ? kit.GetManaLabel(id) : Mathf.CeilToInt(_bound.GetManaCost(id)).ToString();
                    s.ManaCostText.color = enoughMana ? ManaCostAffordable : ManaCostTooExpensive;
                }

                UpdateCharges(s, id);
                UpdateActiveFrame(s, active);
                Animate(s, dt);
            }
        }

        // ---------------- Aufladungen ----------------

        private void BuildCharges(Slot s, int max)
        {
            if (s.Root == null) return;
            if (max <= 1)
            {
                if (s.ChargesRoot != null) s.ChargesRoot.gameObject.SetActive(false);
                return;
            }

            if (s.ChargesRoot == null)
            {
                var go = new GameObject("Charges", typeof(RectTransform));
                s.ChargesRoot = (RectTransform)go.transform;
                s.ChargesRoot.SetParent(s.Root, false);
                s.ChargesRoot.anchorMin = s.ChargesRoot.anchorMax = new Vector2(0.5f, 1f);
                s.ChargesRoot.pivot = new Vector2(0.5f, 0.5f);
                s.ChargesRoot.anchoredPosition = new Vector2(0f, 2f);
                var layout = go.AddComponent<HorizontalLayoutGroup>();
                layout.spacing = 5f;
                layout.childAlignment = TextAnchor.MiddleCenter;
                layout.childControlWidth = layout.childControlHeight = false;
                layout.childForceExpandWidth = layout.childForceExpandHeight = false;
                var fit = go.AddComponent<ContentSizeFitter>();
                fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                // Zähler oben rechts ("2")
                if (s.ManaCostText != null)
                {
                    var countGo = Instantiate(s.ManaCostText.gameObject, s.Root);
                    countGo.name = "ChargeCount";
                    s.ChargeCount = countGo.GetComponent<TextMeshProUGUI>();
                    var rt = (RectTransform)countGo.transform;
                    rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
                    rt.pivot = new Vector2(1f, 1f);
                    rt.anchoredPosition = new Vector2(-6f, -6f);
                    rt.sizeDelta = new Vector2(40f, 26f);
                    s.ChargeCount.alignment = TextAlignmentOptions.TopRight;
                    s.ChargeCount.fontSize = s.ManaCostText.fontSize * 1.1f;
                }
            }
            s.ChargesRoot.gameObject.SetActive(true);
            if (s.ChargeCount != null) s.ChargeCount.gameObject.SetActive(true);

            while (s.Pips.Count < max)
            {
                var pip = new GameObject("Pip" + s.Pips.Count, typeof(RectTransform), typeof(Image));
                var rt = (RectTransform)pip.transform;
                rt.SetParent(s.ChargesRoot, false);
                rt.sizeDelta = new Vector2(PipSize, PipSize);
                var bg = pip.GetComponent<Image>();
                bg.sprite = PipSprite;
                bg.color = PipEmpty;
                bg.raycastTarget = false;
                var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
                var frt = (RectTransform)fillGo.transform;
                frt.SetParent(rt, false);
                frt.anchorMin = Vector2.zero;
                frt.anchorMax = Vector2.one;
                frt.offsetMin = new Vector2(2f, 2f);
                frt.offsetMax = new Vector2(-2f, -2f);
                var fill = fillGo.GetComponent<Image>();
                fill.sprite = PipSprite;
                fill.color = PipFull;
                fill.type = Image.Type.Filled;
                fill.fillMethod = Image.FillMethod.Radial360;
                fill.fillOrigin = (int)Image.Origin360.Top;
                fill.raycastTarget = false;
                s.Pips.Add(bg);
                s.PipFills.Add(fill);
            }
            for (int i = 0; i < s.Pips.Count; i++) s.Pips[i].gameObject.SetActive(i < max);
        }

        private void UpdateCharges(Slot s, AbilityId id)
        {
            int max = _bound.GetMaxCharges(id);
            // Aufladungen können sich zur Laufzeit ändern (Händlerkarte „+1 Aufladung")
            int shown = 0;
            for (int i = 0; i < s.Pips.Count; i++) if (s.Pips[i] != null && s.Pips[i].gameObject.activeSelf) shown++;
            if (max > 1 && shown != max) BuildCharges(s, max);
            if (max <= 1 || s.ChargesRoot == null)
            {
                if (s.ChargeCount != null && s.ChargeCount.gameObject.activeSelf && max <= 1) s.ChargeCount.gameObject.SetActive(false);
                return;
            }
            int charges = _bound.GetCharges(id);
            float rechargeLeft = _bound.GetRechargeRemaining(id);
            float rechargeDur = Mathf.Max(0.01f, _bound.GetCooldownDuration(id));
            for (int i = 0; i < s.PipFills.Count && i < max; i++)
            {
                float f;
                if (i < charges) f = 1f;
                else if (i == charges && rechargeLeft > 0f) f = 1f - Mathf.Clamp01(rechargeLeft / rechargeDur); // lädt gerade
                else f = 0f;
                s.PipFills[i].fillAmount = f;
                Color c = PipFull;
                if (f < 1f) c.a = 0.55f;
                s.PipFills[i].color = c;
            }
            if (s.ChargeCount != null)
            {
                s.ChargeCount.text = charges.ToString();
                s.ChargeCount.color = charges > 0 ? PipFull : ManaCostTooExpensive;
            }
        }

        // ---------------- Aktiv-Rahmen (Block / Schwur / Rolle) ----------------

        private void EnsureActiveFrame(Slot s)
        {
            if (s.ActiveFrame != null || s.Root == null) return;
            var go = new GameObject("ActiveFrame", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(s.Root, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-5f, -5f);
            rt.offsetMax = new Vector2(5f, 5f);
            var img = go.GetComponent<Image>();
            var frameSrc = s.Root.GetComponent<Image>();
            if (frameSrc != null)
            {
                img.sprite = frameSrc.sprite;
                img.type = frameSrc.type;
                img.pixelsPerUnitMultiplier = frameSrc.pixelsPerUnitMultiplier;
            }
            img.raycastTarget = false;
            img.color = new Color(ActiveFrameColor.r, ActiveFrameColor.g, ActiveFrameColor.b, 0f);
            // Hinter Taste/Zähler, aber über dem Icon
            if (s.KeyText != null && s.KeyText.transform.parent != null && s.KeyText.transform.parent.parent == s.Root)
                rt.SetSiblingIndex(s.KeyText.transform.parent.GetSiblingIndex());
            s.ActiveFrame = img;
        }

        private void UpdateActiveFrame(Slot s, bool active)
        {
            if (s.ActiveFrame == null) return;
            float a = active ? 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 9f) : 0f;
            SetAlpha(s.ActiveFrame, a);
        }

        private void Animate(Slot s, float dt)
        {
            float scale = 1f;

            // Unlock pulse: big scale punch, decaying
            if (s.PulseTime >= 0f)
            {
                s.PulseTime += dt;
                float t = s.PulseTime / Mathf.Max(0.01f, UnlockPulseDuration);
                if (t >= 1f) s.PulseTime = -1f;
                else scale = Mathf.Max(scale, 1f + (UnlockPulseScale - 1f) * Mathf.Sin(t * Mathf.PI));
            }

            // Cast flash: small punch + highlight alpha
            float flashAlpha = 0f;
            if (s.FlashTime >= 0f)
            {
                s.FlashTime += dt;
                float t = s.FlashTime / Mathf.Max(0.01f, CastFlashDuration);
                if (t >= 1f) s.FlashTime = -1f;
                else
                {
                    flashAlpha = 1f - t;
                    scale = Mathf.Max(scale, 1f + (CastPunchScale - 1f) * Mathf.Sin(t * Mathf.PI));
                }
            }

            if (s.Root != null) s.Root.localScale = s.BaseScale * scale;
            if (s.CastHighlight != null) SetAlpha(s.CastHighlight, flashAlpha);
        }

        private static void SetAlpha(Image img, float a)
        {
            Color c = img.color;
            if (Mathf.Approximately(c.a, a)) return;
            c.a = a;
            img.color = c;
        }

        public static string DefaultKey(AbilitySlot slot)
        {
            return AbilitySlots.ShortKey(slot);
        }
    }
}
