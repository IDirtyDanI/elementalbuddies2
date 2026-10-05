using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Blitz (Feuer + Licht): Kettenblitz. Erstes Ziel = nächster Gegner in Reichweite, dann Sprünge zum jeweils
    // nächsten noch nicht getroffenen Gegner. Schaden fällt pro Sprung ab; nasse Gegner nehmen mehr Schaden (+1 Sprung, wenn das erste Ziel nass ist).
    public class LightningBuddy : FusionBuddy
    {
        [Header("Kettenblitz")]
        public int Jumps = 3;
        public float JumpRange = 4.5f;
        [Range(0f, 1f)] public float Falloff = 0.8f;
        public float WetMultiplier = 2f;

        [Header("Optik")]
        public Material BoltMaterial;        // optional; leer = Laufzeit-Material
        public GameObject HitVfxPrefab;      // optional, an jedem Treffer
        public Color BoltColor = new Color(1f, 0.92f, 0.5f);
        public float BoltLifetime = 0.12f;
        public float BoltJitter = 0.25f;

        private const int BoltPoints = 8;
        private readonly List<EnemyBrain> _hit = new List<EnemyBrain>();

        protected override FusionElement DefaultElement => FusionElement.Lightning;

        public int GetJumps(bool firstTargetWet) => Mathf.Max(0, Jumps) + (firstTargetWet ? 1 : 0);

        protected override bool TryPerformAction()
        {
            var current = FindNearestEnemy(transform.position, EffectiveRange);
            if (current == null) return false;

            CurrentTarget = current.transform;
            int jumps = GetJumps(current.IsWet);
            float damage = EffectiveDamage;
            Vector3 from = FirePosition;
            _hit.Clear();

            for (int i = 0; current != null && i <= jumps; i++)
            {
                _hit.Add(current);
                Vector3 to = BodyPoint(current);
                SpawnBolt(from, to);
                SpawnVfx(HitVfxPrefab, to, 1f);

                float dmg = damage * Mathf.Pow(Falloff, i) * (current.IsWet ? WetMultiplier : 1f);
                Vector3 jumpOrigin = current.transform.position; // vor dem Schaden merken (Tod zerstört am Frame-Ende)
                current.TakeDamage(DamageAgainst(current, dmg));

                from = to;
                current = i < jumps ? FindNearestEnemy(jumpOrigin, JumpRange, _hit) : null;
            }
            return true;
        }

        // Zackiger Blitz-Abschnitt: 8 Punkte mit zufälligem Versatz quer zur Linie
        private void SpawnBolt(Vector3 from, Vector3 to)
        {
            var points = new Vector3[BoltPoints];
            for (int p = 0; p < BoltPoints; p++)
            {
                float t = p / (float)(BoltPoints - 1);
                Vector3 pos = Vector3.Lerp(from, to, t);
                if (p > 0 && p < BoltPoints - 1) pos += Random.insideUnitSphere * BoltJitter;
                points[p] = pos;
            }
            FusionLineFx.Spawn(points, BoltMaterial, BoltColor, Color.white, 0.07f, 0.03f, BoltLifetime, "LightningBolt");
        }
    }
}
