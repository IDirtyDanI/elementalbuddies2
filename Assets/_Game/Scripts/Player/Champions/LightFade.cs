using UnityEngine;

namespace ElementalBuddies
{
    // Lässt ein Punktlicht über Duration ausklingen (Einmal-Effekte der Champions)
    [RequireComponent(typeof(Light))]
    public class LightFade : MonoBehaviour
    {
        public float Duration = 0.5f;

        private Light _light;
        private float _start;
        private float _t;

        void Awake()
        {
            _light = GetComponent<Light>();
            _start = _light.intensity;
        }

        void Update()
        {
            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / Mathf.Max(0.01f, Duration));
            _light.intensity = _start * (1f - k) * (1f - k);
            if (k >= 1f) _light.enabled = false;
        }
    }
}
