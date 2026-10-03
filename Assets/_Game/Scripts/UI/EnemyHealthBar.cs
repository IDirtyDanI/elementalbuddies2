using UnityEngine;
using UnityEngine.UI;

namespace ElementalBuddies
{
    // World-Space-HP-Bar über einem Gegner (Prefab aus dem FunProject: HPBar).
    // Unsichtbar, bis der Gegner Schaden genommen hat; schaut immer zur Kamera.
    public class EnemyHealthBar : MonoBehaviour
    {
        public Slider Slider;
        [Tooltip("Optional: Füllung, wird je nach HP von Gelb nach Rot gefärbt.")]
        public Image Fill;
        [Tooltip("Optional: verzögerte helle Leiste hinter der Füllung (zeigt den gerade verlorenen Schaden).")]
        public RectTransform DamageChip;
        public Color FullColor = new Color(0.85f, 0.2f, 0.15f);
        public Color LowColor = new Color(0.55f, 0.05f, 0.05f);
        [Tooltip("Abstand über der Oberkante des Gegner-Modells.")]
        public float HeightOffset = 0.35f;
        public float ChipDelay = 0.35f;
        public float ChipSpeed = 1.5f;

        private EnemyBrain _enemy;
        private Renderer[] _renderers;
        private Canvas _canvas;
        private float _height = 2f;
        private float _chip = 1f;
        private float _chipHoldUntil;
        private float _lastValue = 1f;

        public void Bind(EnemyBrain enemy)
        {
            _enemy = enemy;
            _renderers = enemy.GetComponentsInChildren<Renderer>();
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

        private void MeasureHeight()
        {
            bool any = false;
            Bounds b = default;
            foreach (var r in _renderers)
            {
                if (r == null || r is ParticleSystemRenderer || r.transform.IsChildOf(transform)) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            if (any) _height = b.max.y - _enemy.transform.position.y;
        }

        private void SetVisible(bool visible)
        {
            if (_canvas != null) _canvas.enabled = visible;
        }

        void LateUpdate()
        {
            if (_enemy == null) { Destroy(gameObject); return; }

            float hp01 = _enemy.MaxHP > 0f ? Mathf.Clamp01(_enemy.CurrentHP / _enemy.MaxHP) : 1f;
            bool damaged = hp01 < 0.999f;
            SetVisible(damaged);
            if (!damaged) { _chip = 1f; _lastValue = 1f; return; }

            if (hp01 < _lastValue) _chipHoldUntil = Time.time + ChipDelay;
            _lastValue = hp01;
            if (Time.time >= _chipHoldUntil) _chip = Mathf.MoveTowards(_chip, hp01, ChipSpeed * Time.deltaTime);
            if (_chip < hp01) _chip = hp01;

            if (Slider != null) Slider.value = hp01;
            if (Fill != null) Fill.color = Color.Lerp(LowColor, FullColor, hp01);
            if (DamageChip != null) DamageChip.anchorMax = new Vector2(_chip, DamageChip.anchorMax.y);

            transform.position = _enemy.transform.position + Vector3.up * (_height + HeightOffset);
            Camera cam = Camera.main;
            if (cam != null) transform.rotation = cam.transform.rotation;
        }
    }
}
