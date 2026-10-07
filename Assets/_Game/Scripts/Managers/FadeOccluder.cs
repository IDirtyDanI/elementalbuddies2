using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Auf Gebäude-Roots: Renderer dieses Objekts dürfen ausgeblendet werden, wenn sie die Sicht auf den Spieler verdecken.
    // Erfasst auch inaktive Modell-Varianten (Beschädigt/Ruine aus BuildingDamage); Partikel-Renderer werden ignoriert.
    public class FadeOccluder : MonoBehaviour
    {
        [Range(0f, 1f)] public float FadedAlpha = 0.28f;
        [Tooltip("Trigger-Collider (z. B. Baumkronen auf Layer Ignore Raycast) zählen als Sichtblocker für den OcclusionFader.")]
        public bool TriggerVolumes;

        private Renderer[] _renderers;
        private Material[][] _opaque;
        private Material[][] _faded;
        private float _alpha = 1f;
        private float _target = 1f;
        private Color _tint = Color.white;
        private bool _dirty;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly Dictionary<Material, Material> FadeCache = new Dictionary<Material, Material>();

        public void SetOccluding(bool occluding) => _target = occluding ? FadedAlpha : 1f;

        // Farb-Multiplikator für alle Modelle (z. B. verkohlt, wenn es kein eigenes Ruinen-Modell gibt)
        public void SetTint(Color tint)
        {
            if (!Application.isPlaying) _renderers = null; // Edit-Modus (Vorschau): nichts cachen
            Init();
            if (_tint == tint) return;
            _tint = tint;
            ApplyBlocks();
        }

        // Tauscht ein Material in allen erfassten Renderern aus (z. B. Laufzeit-Klon des Fensterglühens).
        // Ist das Gebäude gerade ausgeblendet, wird nur der Cache angepasst und beim Einblenden übernommen.
        public void ReplaceMaterial(Material from, Material to)
        {
            if (!Application.isPlaying) _renderers = null;
            Init();
            bool isOpaque = _alpha >= 0.999f;
            for (int i = 0; i < _renderers.Length; i++)
            {
                bool changed = false;
                for (int k = 0; k < _opaque[i].Length; k++)
                {
                    if (_opaque[i][k] != from) continue;
                    _opaque[i][k] = to;
                    if (_faded[i] != null) _faded[i][k] = FadeVariant(to);
                    changed = true;
                }
                if (changed && _renderers[i] != null) _renderers[i].sharedMaterials = isOpaque ? _opaque[i] : Faded(i);
            }
            if (!isOpaque) _dirty = true;
        }

        // Spätestens in Start (Awake anderer Komponenten darf vorher Materialien tauschen).
        // Nach einem Hot-Reload im Play-Mode stellt Unity _renderers wieder her, die Material-Arrays (Material[][],
        // nicht serialisierbar) aber nicht → dann neu erfassen (sonst NullReference in Update/ReplaceMaterial).
        private void Init()
        {
            if (_renderers != null && _opaque != null && _faded != null && _opaque.Length == _renderers.Length) return;
            var all = GetComponentsInChildren<Renderer>(true);
            var list = new List<Renderer>();
            foreach (var r in all) if (r is MeshRenderer || r is SkinnedMeshRenderer) list.Add(r);
            _renderers = list.ToArray();
            _opaque = new Material[_renderers.Length][];
            _faded = new Material[_renderers.Length][];
            for (int i = 0; i < _renderers.Length; i++) _opaque[i] = _renderers[i].sharedMaterials;
        }

        // Transparente Varianten erst bei Bedarf anlegen (nicht im Edit-Modus beim Tönen)
        private Material[] Faded(int i)
        {
            if (_faded[i] != null) return _faded[i];
            _faded[i] = new Material[_opaque[i].Length];
            for (int k = 0; k < _opaque[i].Length; k++) _faded[i][k] = FadeVariant(_opaque[i][k]);
            return _faded[i];
        }

        void Start() => Init();

        void Update()
        {
            Init();
            if (Mathf.Approximately(_alpha, _target) && !_dirty) return;
            bool wasOpaque = _alpha >= 0.999f;
            _alpha = Mathf.MoveTowards(_alpha, _target, Time.unscaledDeltaTime * 4f);
            bool isOpaque = _alpha >= 0.999f;
            if (wasOpaque != isOpaque || _dirty)
                for (int i = 0; i < _renderers.Length; i++)
                    if (_renderers[i] != null) _renderers[i].sharedMaterials = isOpaque ? _opaque[i] : Faded(i);
            _dirty = false;
            ApplyBlocks();
        }

        // Alpha (beim Ausblenden) und Tönung per MaterialPropertyBlock
        private void ApplyBlocks()
        {
            bool isOpaque = _alpha >= 0.999f;
            bool tinted = _tint != Color.white;
            for (int i = 0; i < _renderers.Length; i++)
            {
                var r = _renderers[i];
                if (r == null) continue;
                if (isOpaque && !tinted) { r.SetPropertyBlock(null); continue; }
                var mats = isOpaque ? _opaque[i] : Faded(i);
                var block = new MaterialPropertyBlock();
                for (int k = 0; k < mats.Length; k++)
                {
                    var m = mats[k];
                    if (m == null || !m.HasProperty(BaseColorId)) continue;
                    Color c = m.GetColor(BaseColorId) * _tint;
                    c.a = isOpaque ? 1f : _alpha;
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
