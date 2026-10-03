using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ElementalBuddies
{
    // Bottom-left ability bar: Q Arcane Ball, E Blink, R Flammenwelle, F Frostnova, C Steinwall, V Heiliger Kreis.
    // Shows cooldown (radial fill + seconds), mana cost, locked state, not-enough-mana tint,
    // a scale pulse on unlock and a short highlight flash on cast.
    public class AbilityBarUI : MonoBehaviour
    {
        [System.Serializable]
        public class Slot
        {
            public AbilityId Ability;
            [Tooltip("Shown in KeyText. Empty = default key for the ability.")]
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

            [System.NonSerialized] public float PulseTime = -1f;
            [System.NonSerialized] public float FlashTime = -1f;
            [System.NonSerialized] public Vector3 BaseScale = Vector3.one;
        }

        [Tooltip("Optional explicit reference; falls back to PlayerAbilities.Instance.")]
        public PlayerAbilities Abilities;

        public List<Slot> Slots = new List<Slot>
        {
            new Slot { Ability = AbilityId.ArcaneBall },
            new Slot { Ability = AbilityId.Blink },
            new Slot { Ability = AbilityId.FireWave },
            new Slot { Ability = AbilityId.FrostNova },
            new Slot { Ability = AbilityId.StoneWall },
            new Slot { Ability = AbilityId.HolyCircle },
        };

        [Header("Colors")]
        public Color ReadyTint = Color.white;
        public Color NotEnoughManaTint = new Color(0.45f, 0.55f, 0.85f, 1f);
        public Color LockedTint = new Color(0.3f, 0.3f, 0.3f, 1f);
        public Color ManaCostAffordable = new Color(0.55f, 0.8f, 1f);
        public Color ManaCostTooExpensive = new Color(0.9f, 0.3f, 0.3f);

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
                if (s.KeyText != null) s.KeyText.text = string.IsNullOrEmpty(s.KeyLabel) ? DefaultKey(s.Ability) : s.KeyLabel;
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
        }

        private void Unbind()
        {
            if (_bound == null) return;
            _bound.OnAbilityUnlocked -= HandleUnlocked;
            _bound.OnAbilityCast -= HandleCast;
            _bound = null;
        }

        private void HandleUnlocked(int elementIndex)
        {
            AbilityId id = PlayerAbilities.AbilityOfElement(elementIndex);
            foreach (var s in Slots)
                if (s.Ability == id) s.PulseTime = 0f;
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

            foreach (var s in Slots)
            {
                bool unlocked = _bound.IsUnlocked(s.Ability);
                float cost = _bound.GetManaCost(s.Ability);
                float remaining = _bound.GetCooldownRemaining(s.Ability);
                float duration = _bound.GetCooldownDuration(s.Ability);
                bool enoughMana = mana >= cost;

                if (s.LockedOverlay != null && s.LockedOverlay.activeSelf == unlocked) s.LockedOverlay.SetActive(!unlocked);

                if (s.Icon != null)
                    s.Icon.color = !unlocked ? LockedTint : (enoughMana ? ReadyTint : NotEnoughManaTint);

                bool onCooldown = unlocked && remaining > 0f && duration > 0f;
                if (s.CooldownOverlay != null)
                {
                    s.CooldownOverlay.fillAmount = onCooldown ? Mathf.Clamp01(remaining / duration) : 0f;
                }
                if (s.CooldownText != null)
                {
                    bool show = onCooldown;
                    if (s.CooldownText.gameObject.activeSelf != show) s.CooldownText.gameObject.SetActive(show);
                    if (show) s.CooldownText.text = remaining >= 1f ? Mathf.CeilToInt(remaining).ToString() : remaining.ToString("0.0");
                }
                if (s.ManaCostText != null)
                {
                    s.ManaCostText.text = Mathf.CeilToInt(cost).ToString();
                    s.ManaCostText.color = enoughMana ? ManaCostAffordable : ManaCostTooExpensive;
                }

                Animate(s, dt);
            }
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

        public static string DefaultKey(AbilityId id)
        {
            switch (id)
            {
                case AbilityId.ArcaneBall: return "Q";
                case AbilityId.Blink: return "E";
                case AbilityId.FireWave: return "R";
                case AbilityId.FrostNova: return "F";
                case AbilityId.StoneWall: return "C";
                case AbilityId.HolyCircle: return "V";
                default: return "";
            }
        }
    }
}
