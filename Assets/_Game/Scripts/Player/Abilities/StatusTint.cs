using UnityEngine;

namespace ElementalBuddies
{
    // Gemeinsame Färbung für Status-Effekte (Brand, Frost) per MaterialPropertyBlock.
    // Vorrang: Frost > Brand > Blendung; ohne aktiven Effekt wird der Block entfernt.
    public static class StatusTint
    {
        public static readonly Color BurnTint = new Color(1f, 0.45f, 0.25f);
        public static readonly Color FrozenTint = new Color(0.45f, 0.75f, 1.35f);
        public static readonly Color BlindTint = new Color(1.35f, 1.25f, 0.8f);
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

            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r.sharedMaterial == null || !r.sharedMaterial.HasProperty(BaseColorId)) continue;
                if (!frozen && !burning && !blinded) { r.SetPropertyBlock(null); continue; }
                Color tint = frozen ? FrozenTint : (burning ? BurnTint : BlindTint);
                r.GetPropertyBlock(Block);
                Block.SetColor(BaseColorId, r.sharedMaterial.GetColor(BaseColorId) * tint);
                r.SetPropertyBlock(Block);
            }
        }
    }
}
