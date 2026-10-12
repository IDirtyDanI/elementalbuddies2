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
}
