using UnityEngine;

namespace ElementalBuddies
{
    // Kurzes Aufhellen eines getroffenen Gegners (MaterialPropertyBlock auf _BaseColor, wie StatusTint).
    // Danach stellt StatusTint.Refresh die Status-Färbung (Brand, Frost …) wieder her.
    public class HitFlash : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();

        private Renderer[] _renderers;
        private float _until;
        private bool _on;

        // strength 0..1: Anteil Weiß (Champion-Treffer stark, Turm-Treffer schwach)
        public static void Flash(GameObject root, float strength = 0.75f, float duration = 0.08f)
        {
            if (root == null || !GameFeel.Flash) return;
            var hf = root.GetComponent<HitFlash>();
            if (hf == null) hf = root.AddComponent<HitFlash>();
            hf.Do(strength, duration);
        }

        private void Do(float strength, float duration)
        {
            if (_renderers == null) _renderers = GetComponentsInChildren<Renderer>(true);
            Color white = new Color(1.6f, 1.6f, 1.6f, 1f);
            foreach (var r in _renderers)
            {
                if (r == null || r is ParticleSystemRenderer || r.sharedMaterial == null || !r.sharedMaterial.HasProperty(BaseColorId)) continue;
                Color baseCol = r.sharedMaterial.GetColor(BaseColorId);
                r.GetPropertyBlock(Block);
                Block.SetColor(BaseColorId, Color.Lerp(baseCol, white, strength));
                r.SetPropertyBlock(Block);
            }
            _until = Mathf.Max(_until, Time.unscaledTime + duration);
            _on = true;
        }

        void Update()
        {
            if (!_on || Time.unscaledTime < _until) return;
            _on = false;
            StatusTint.Refresh(gameObject);
        }
    }
}
