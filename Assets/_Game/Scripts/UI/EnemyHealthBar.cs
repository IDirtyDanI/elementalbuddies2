using UnityEngine;
using UnityEngine.UI;

namespace ElementalBuddies
{
    // Ziel einer HP-Bar (Gegner, Buddy)
    public interface IHealthBarTarget
    {
        Transform transform { get; }
        float CurrentHP { get; }
        float MaxHP { get; }
        bool HealthBarVisible { get; } // false = Bar ausblenden (z. B. deaktivierter Buddy)
    }

    // Optional: Ziel mit Schild (Buddy-Segen) – Schild wird als Segment an der HP-Bar gezeigt
    public interface IShieldedTarget
    {
        float ShieldAmount { get; }
    }

    // World-Space-HP-Bar über einem Gegner oder Buddy (Prefab aus dem FunProject: HPBar).
    // Unsichtbar, bis das Ziel Schaden genommen hat; schaut immer zur Kamera.
    public class EnemyHealthBar : MonoBehaviour
    {
        public Slider Slider;
        [Tooltip("Optional: Füllung, wird je nach HP von Gelb nach Rot gefärbt.")]
        public Image Fill;
        [Tooltip("Optional: verzögerte helle Leiste hinter der Füllung (zeigt den gerade verlorenen Schaden).")]
        public RectTransform DamageChip;
        [Tooltip("Optional: Schild-Segment, hinter der Füllung angehängt (bzw. am rechten Ende überlagert bei vollen HP).")]
        public RectTransform Shield;
        public Color FullColor = new Color(0.85f, 0.2f, 0.15f);
        public Color LowColor = new Color(0.55f, 0.05f, 0.05f);
        public Color BuddyFullColor = new Color(0.3f, 0.85f, 0.3f);
        public Color BuddyLowColor = new Color(0.9f, 0.75f, 0.15f);
        [Tooltip("Abstand über der Oberkante des Gegner-Modells.")]
        public float HeightOffset = 0.35f;
        public float ChipDelay = 0.35f;
        public float ChipSpeed = 1.5f;

        private IHealthBarTarget _target;
        private IShieldedTarget _shielded;
        private Object _targetObject; // für Unitys Null-Check (zerstörtes Ziel)
        private Renderer[] _renderers;
        private Color _full, _low;
        private bool _measured;
        private Canvas _canvas;
        private float _height = 2f;
        private float _chip = 1f;
        private float _chipHoldUntil;
        private float _lastValue = 1f;

        public void Bind(EnemyBrain enemy) => Bind(enemy, enemy, FullColor, LowColor);

        public void Bind(ElementalBuddy buddy) => Bind(buddy, buddy, BuddyFullColor, BuddyLowColor);

        private void Bind(IHealthBarTarget target, Object targetObject, Color full, Color low)
        {
            _target = target;
            _shielded = target as IShieldedTarget;
            _targetObject = targetObject;
            _full = full;
            _low = low;
            _renderers = target.transform.GetComponentsInChildren<Renderer>(true);
            _canvas = GetComponent<Canvas>();
            if (Slider != null)
            {
                Slider.interactable = false;
                Slider.wholeNumbers = false;
                Slider.direction = Slider.Direction.LeftToRight;
                Slider.minValue = 0f;
                Slider.maxValue = 1f;
                Slider.value = 1f;
            }
            MeasureHeight();
            SetVisible(false);
        }

        // Höhe über aktive Renderer (inaktive Entwicklungsstufen zählen nicht); MarkDirty misst beim nächsten Anzeigen neu
        private void MeasureHeight()
        {
            bool any = false;
            Bounds b = default;
            foreach (var r in _renderers)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer || r.transform.IsChildOf(transform)) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            if (any) _height = b.max.y - _target.transform.position.y;
            _measured = any;
        }

        // Visual hat sich geändert (z. B. Buddy-Entwicklung)
        public void MarkDirty()
        {
            if (_target != null && _targetObject != null) _renderers = _target.transform.GetComponentsInChildren<Renderer>(true);
            _measured = false;
        }

        private void SetVisible(bool visible)
        {
            if (_canvas != null) _canvas.enabled = visible;
        }

        void LateUpdate()
        {
            if (_targetObject == null) { Destroy(gameObject); return; }

            float hp01 = _target.MaxHP > 0f ? Mathf.Clamp01(_target.CurrentHP / _target.MaxHP) : 1f;
            float shield01 = _shielded != null && _target.MaxHP > 0f ? Mathf.Clamp01(_shielded.ShieldAmount / _target.MaxHP) : 0f;
            bool damaged = hp01 < 0.999f;
            bool shielded = shield01 > 0.001f;
            // Mit aktivem Schild auch bei vollen HP sichtbar
            SetVisible((damaged || shielded) && _target.HealthBarVisible);
            UpdateShield(hp01, shield01);
            if (!damaged && !shielded) { _chip = 1f; _lastValue = 1f; return; }
            if (!_measured) MeasureHeight();

            if (hp01 < _lastValue) _chipHoldUntil = Time.time + ChipDelay;
            _lastValue = hp01;
            if (Time.time >= _chipHoldUntil) _chip = Mathf.MoveTowards(_chip, hp01, ChipSpeed * Time.deltaTime);
            if (_chip < hp01) _chip = hp01;

            if (Slider != null) Slider.value = hp01;
            if (Fill != null) Fill.color = Color.Lerp(_low, _full, hp01);
            if (DamageChip != null) DamageChip.anchorMax = new Vector2(_chip, DamageChip.anchorMax.y);

            transform.position = _target.transform.position + Vector3.up * (_height + HeightOffset);
            Camera cam = Camera.main;
            if (cam != null) transform.rotation = cam.transform.rotation;
        }

        // Schild-Segment direkt hinter der Füllung; passt es nicht mehr hinein, überlagert es das rechte Ende
        private void UpdateShield(float hp01, float shield01)
        {
            if (Shield == null) return;
            bool on = shield01 > 0.001f;
            if (Shield.gameObject.activeSelf != on) Shield.gameObject.SetActive(on);
            if (!on) return;
            float end = Mathf.Min(1f, hp01 + shield01);
            float start = Mathf.Max(0f, end - shield01);
            Shield.anchorMin = new Vector2(start, Shield.anchorMin.y);
            Shield.anchorMax = new Vector2(end, Shield.anchorMax.y);
        }
    }
}
