using UnityEngine;

namespace ElementalBuddies
{
    // Gemeinsame Färbung für Status-Effekte (Brand, Frost, Fluch, Nässe, Blendung) per MaterialPropertyBlock.
    // Vorrang: Frost > Brand > Fluch > Nass > Blendung; ohne aktiven Effekt wird der Block entfernt.
    public static class StatusTint
    {
        public static readonly Color BurnTint = new Color(1f, 0.45f, 0.25f);
        public static readonly Color FrozenTint = new Color(0.45f, 0.75f, 1.35f);
        public static readonly Color BlindTint = new Color(1.35f, 1.25f, 0.8f);
        public static readonly Color WetTint = new Color(0.6f, 0.8f, 1.15f);
        public static readonly Color CurseTint = new Color(0.75f, 0.45f, 1.1f);
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();

        public static void Refresh(GameObject root)
        {
            if (root == null) return;
            var burn = root.GetComponent<BurnEffect>();
            var freeze = root.GetComponent<FreezeEffect>();
            bool frozen = freeze != null && freeze.IsActive;
            bool burning = burn != null && burn.IsActive;
            var blind = root.GetComponent<BlindEffect>();
            bool blinded = blind != null && blind.IsActive;
            var curse = root.GetComponent<CurseEffect>();
            bool cursed = curse != null && curse.IsActive;
            var wet = root.GetComponent<WetEffect>();
            bool wetted = wet != null && wet.IsActive;

            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r.sharedMaterial == null || !r.sharedMaterial.HasProperty(BaseColorId)) continue;
                bool none = !frozen && !burning && !cursed && !wetted && !blinded;
                Color tint = frozen ? FrozenTint : burning ? BurnTint : cursed ? CurseTint : wetted ? WetTint : BlindTint;
                if (IsMulti(r)) { TintEach(r, none ? (Color?)null : tint, 0f); continue; }
                if (none) { r.SetPropertyBlock(null); continue; }
                r.GetPropertyBlock(Block);
                Block.SetColor(BaseColorId, r.sharedMaterial.GetColor(BaseColorId) * tint);
                r.SetPropertyBlock(Block);
            }
        }

        // Mehrere Materialien an einem Renderer (z. B. Ramme: Holz/Eisen/Haut): Block je Material-Index, sonst nähme
        // der ganze Renderer die Farbe des ersten Materials an
        public static bool IsMulti(Renderer r) => r.sharedMaterials.Length > 1;

        // tint null = Färbung entfernen; whiten > 0 = Treffer-Aufhellung (HitFlash) statt Multiplikation
        public static void TintEach(Renderer r, Color? tint, float whiten)
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                Block.Clear();
                var m = mats[i];
                if (tint.HasValue && m != null && m.HasProperty(BaseColorId))
                {
                    Color c = m.GetColor(BaseColorId);
                    Block.SetColor(BaseColorId, whiten > 0f ? Color.Lerp(c, new Color(1.6f, 1.6f, 1.6f, 1f), whiten) : c * tint.Value);
                }
                r.SetPropertyBlock(Block, i);
            }
        }
    }

    // Körpergröße von Gegnern aus dem Collider (Root-Raum, ohne Transform-Skalierung). Status-VFX sind für
    // Normalgegner (~1 m breit, 2 m hoch) gebaut; große Gegner (Belagerungsramme, 2,7 m lang im Root-Raum)
    // bekommen sie passend vergrößert. Normalgegner und Bosse liegen unter der Referenz → exakt unverändert.
    public static class BodyFootprint
    {
        // Größte horizontale Halbausdehnung eines Normalgegners im Root-Raum (Grabspinne: Kapsel 1,5 m lang)
        private const float RefLocalHalfExtent = 0.75f;

        // Größte horizontale Halbausdehnung des Colliders im Raum seines Transforms (ohne lossyScale)
        public static float LocalHalfExtent(Collider c)
        {
            switch (c)
            {
                case CapsuleCollider cap:
                    return cap.direction == 1 ? cap.radius : Mathf.Max(cap.radius, cap.height * 0.5f);
                case BoxCollider box:
                    return Mathf.Max(box.size.x, box.size.z) * 0.5f;
                case SphereCollider sph:
                    return sph.radius;
                default:
                    return c.bounds.extents.x / Mathf.Max(0.01f, Mathf.Abs(c.transform.lossyScale.x));
            }
        }

        // Zusatz-Faktor für Status-VFX (nur Gegner): ≥ 1, für alle Normalgegner genau 1
        public static float StatusVfxFactor(GameObject host)
        {
            if (host == null || host.GetComponent<EnemyBrain>() == null) return 1f;
            var col = host.GetComponent<Collider>();
            if (col == null) return 1f;
            return Mathf.Max(1f, LocalHalfExtent(col) / RefLocalHalfExtent);
        }

        // Frisch instanziierte Status-VFX (Kind von host) an große Gegner anpassen. Hierarchy-Systeme erben die
        // Root-Skalierung (Ramme ×1,7) schon und bekommen nur den Faktor; Local-Systeme ignorieren Eltern-Skalierung
        // und bekommen Faktor × Root-Skalierung, damit alle Teile gleich groß wirken. Dichte wächst linear mit.
        public static void FitStatusVfx(GameObject vfx, GameObject host)
        {
            if (vfx == null) return;
            float k = StatusVfxFactor(host);
            if (k <= 1f) return;
            float hostScale = Mathf.Abs(host.transform.lossyScale.x);
            vfx.transform.localScale *= k;
            foreach (var ps in vfx.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                if (ps.transform != vfx.transform && main.scalingMode != ParticleSystemScalingMode.Hierarchy)
                    ps.transform.localScale *= k * hostScale;
                main.maxParticles = Mathf.CeilToInt(main.maxParticles * k);
                var emission = ps.emission;
                emission.rateOverTimeMultiplier *= k;
                for (int i = 0; i < emission.burstCount; i++)
                {
                    var burst = emission.GetBurst(i);
                    if (burst.count.mode != ParticleSystemCurveMode.Constant) continue;
                    burst.count = new ParticleSystem.MinMaxCurve(burst.count.constant * k);
                    emission.SetBurst(i, burst);
                }
            }
            foreach (var l in vfx.GetComponentsInChildren<Light>(true)) l.range *= k * hostScale;
        }
    }
}
