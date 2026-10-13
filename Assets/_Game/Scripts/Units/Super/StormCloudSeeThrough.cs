using UnityEngine;

namespace ElementalBuddies
{
    // Reine Optik (lokal auf jedem Peer): macht die Sturmwolke des Sturmfürsten durchsichtig, wenn die eigene Spielfigur
    // in der Nähe ist (horizontal unter/neben der Wolke) oder die Sichtlinie Kamera → Spielfigur durch die Wolke läuft.
    // Nutzt den FadeOccluder (transparente Material-Varianten + Alpha per MaterialPropertyBlock, ~0,2 s Überblendung)
    // für das Wolken-Mesh; die Rauchschwaden-Partikel (Wisps) werden zusätzlich per _BaseColor-Alpha mit ausgeblendet.
    [RequireComponent(typeof(FadeOccluder))]
    public class StormCloudSeeThrough : MonoBehaviour
    {
        [Range(0f, 1f)] public float FadedAlpha = 0.25f;
        [Tooltip("Zusätzlicher Rand (m) um die horizontale Wolkenfläche, ab dem schon ausgeblendet wird.")]
        public float NearMargin = 1.5f;
        [Tooltip("Rand (m) um die Wolken-Bounds für den Sichtlinien-Test (Spielfigur ~1 m breit).")]
        public float RayMargin = 0.75f;
        [Tooltip("Partikel-Renderer (z. B. Wisps), deren Alpha mitgeblendet wird. Leer = Wisps automatisch suchen.")]
        public ParticleSystemRenderer[] FadeParticles;

        private FadeOccluder _occ;
        private Renderer _cloudRenderer;
        private Color[] _particleBase;
        private float _particleAlpha = 1f;
        private Transform _soloPlayer;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // Hinzufügen zur Laufzeit (StormCloud.Spawn) – der FadeOccluder kommt per RequireComponent mit
        public static void Attach(GameObject cloud)
        {
            if (cloud.GetComponent<StormCloudSeeThrough>() == null) cloud.AddComponent<StormCloudSeeThrough>();
        }

        void Start()
        {
            _occ = GetComponent<FadeOccluder>();
            _occ.FadedAlpha = FadedAlpha;

            // Größtes Mesh = Wolkenkörper (Bounds für Nähe- und Sichtlinien-Test)
            float best = -1f;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                float s = r.bounds.size.sqrMagnitude;
                if (s > best) { best = s; _cloudRenderer = r; }
            }

            if (FadeParticles == null || FadeParticles.Length == 0)
            {
                var list = new System.Collections.Generic.List<ParticleSystemRenderer>();
                foreach (var p in GetComponentsInChildren<ParticleSystemRenderer>(true))
                    if (p.name == "Wisps") list.Add(p);
                FadeParticles = list.ToArray();
            }
            _particleBase = new Color[FadeParticles.Length];
            for (int i = 0; i < FadeParticles.Length; i++)
            {
                var m = FadeParticles[i] != null ? FadeParticles[i].sharedMaterial : null;
                _particleBase[i] = m != null && m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : Color.white;
            }
        }

        void LateUpdate()
        {
            if (_occ == null) return;
            bool fade = ShouldFade();
            _occ.SetOccluding(fade);

            // Partikel im gleichen Tempo wie der FadeOccluder (4/s) überblenden
            float target = fade ? FadedAlpha : 1f;
            if (Mathf.Approximately(_particleAlpha, target)) return;
            _particleAlpha = Mathf.MoveTowards(_particleAlpha, target, Time.unscaledDeltaTime * 4f);
            bool opaque = _particleAlpha >= 0.999f;
            for (int i = 0; i < FadeParticles.Length; i++)
            {
                var p = FadeParticles[i];
                if (p == null) continue;
                if (opaque) { p.SetPropertyBlock(null); continue; }
                var block = new MaterialPropertyBlock();
                Color c = _particleBase[i];
                c.a *= _particleAlpha;
                block.SetColor(BaseColorId, c);
                p.SetPropertyBlock(block);
            }
        }

        private bool ShouldFade()
        {
            Transform player = LocalPlayer();
            if (player == null || _cloudRenderer == null) return false;
            Bounds b = _cloudRenderer.bounds;
            Vector3 pp = player.position;

            // 1) Nähe: Spielfigur horizontal unter der Wolke (+ Rand)
            float rx = b.extents.x + NearMargin, rz = b.extents.z + NearMargin;
            float dx = (pp.x - b.center.x) / rx, dz = (pp.z - b.center.z) / rz;
            if (dx * dx + dz * dz <= 1f) return true;

            // 2) Sichtlinie: Strahl Kamera → Spielfigur (Brust- und Fußhöhe) schneidet die Wolken-Bounds
            var cam = Camera.main;
            if (cam == null) return false;
            Vector3 from = cam.transform.position;
            b.Expand(RayMargin * 2f);
            return Blocks(b, from, pp + Vector3.up * 1f) || Blocks(b, from, pp + Vector3.up * 0.1f);
        }

        private static bool Blocks(Bounds b, Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 0.01f) return false;
            return b.IntersectRay(new Ray(from, d / len), out float hit) && hit < len;
        }

        // Eigene Figur (Mehrspieler: lokaler Avatar), sonst Einzelspieler-Figur per Tag
        private Transform LocalPlayer()
        {
            var local = PlayerAvatar.Local;
            if (local != null) return local.transform;
            if (PlayerAvatar.All.Count > 0) return null;
            if (_soloPlayer == null)
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                if (p != null) _soloPlayer = p.transform;
            }
            return _soloPlayer;
        }
    }
}
