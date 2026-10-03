using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Auf Gebäude-Roots: Renderer dieses Objekts dürfen ausgeblendet werden, wenn sie die Sicht auf den Spieler verdecken.
    public class FadeOccluder : MonoBehaviour
    {
        [Range(0f, 1f)] public float FadedAlpha = 0.28f;

        private Renderer[] _renderers;
        private Material[][] _opaque;
        private Material[][] _faded;
        private float _alpha = 1f;
        private float _target = 1f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly Dictionary<Material, Material> FadeCache = new Dictionary<Material, Material>();

        public void SetOccluding(bool occluding) => _target = occluding ? FadedAlpha : 1f;

        void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>();
            _opaque = new Material[_renderers.Length][];
            _faded = new Material[_renderers.Length][];
            for (int i = 0; i < _renderers.Length; i++)
            {
                _opaque[i] = _renderers[i].sharedMaterials;
                _faded[i] = new Material[_opaque[i].Length];
                for (int k = 0; k < _opaque[i].Length; k++) _faded[i][k] = FadeVariant(_opaque[i][k]);
            }
        }

        void Update()
        {
            if (Mathf.Approximately(_alpha, _target)) return;
            bool wasOpaque = _alpha >= 0.999f;
            _alpha = Mathf.MoveTowards(_alpha, _target, Time.unscaledDeltaTime * 4f);
            bool isOpaque = _alpha >= 0.999f;
            for (int i = 0; i < _renderers.Length; i++)
            {
                var r = _renderers[i];
                if (r == null) continue;
                if (wasOpaque != isOpaque) r.sharedMaterials = isOpaque ? _opaque[i] : _faded[i];
                if (isOpaque) { r.SetPropertyBlock(null); continue; }
                var block = new MaterialPropertyBlock();
                for (int k = 0; k < _faded[i].Length; k++)
                {
                    var m = _faded[i][k];
                    if (m == null || !m.HasProperty(BaseColorId)) continue;
                    Color c = m.GetColor(BaseColorId); c.a = _alpha;
                    block.SetColor(BaseColorId, c);
                    r.SetPropertyBlock(block, k);
                }
            }
        }

        // Transparente Kopie eines URP-Lit-Materials (einmal pro Ausgangsmaterial)
        private static Material FadeVariant(Material src)
        {
            if (src == null) return null;
            if (FadeCache.TryGetValue(src, out var cached) && cached != null) return cached;
            var m = new Material(src) { name = src.name + " (Fade)" };
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            FadeCache[src] = m;
            return m;
        }
    }
}
