using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Ergebnis-Elemente der Buddy-Fusion (Reihenfolge = Index in FusionInfo)
    public enum FusionElement
    {
        Lightning,
        Water,
        Air,
        Shadow,
        Magma,
        Crystal,
        // Super-Elementare (Tri-Fusion aus drei Elementen, angehängt)
        VolcanoTitan,
        StormLord,
        Phoenix,
        WorldTree
    }

    // Anzeige-Daten pro Fusions-Element
    public static class FusionInfo
    {
        public static readonly string[] Names =
            { "Blitz", "Wasser", "Luft", "Schatten", "Magma", "Kristall", "Vulkan-Titan", "Sturmfürst", "Phönix", "Weltenbaum" };
        public static readonly string[] DisplayNames =
            { "Blitz-Elementar", "Wasser-Elementar", "Luft-Elementar", "Schatten-Elementar", "Magma-Elementar", "Kristall-Elementar",
              "Vulkan-Titan", "Sturmfürst", "Phönix", "Weltenbaum" };
        public static readonly Color[] Colors =
        {
            new Color(1f, 0.88f, 0.35f),   // Blitz
            new Color(0.2f, 0.55f, 1f),    // Wasser
            new Color(0.75f, 0.95f, 0.85f),// Luft
            new Color(0.55f, 0.25f, 0.85f),// Schatten
            new Color(1f, 0.35f, 0.1f),    // Magma
            new Color(0.6f, 0.9f, 1f),     // Kristall
            new Color(1f, 0.45f, 0.15f),   // Vulkan-Titan
            new Color(0.55f, 0.6f, 1f),    // Sturmfürst
            new Color(1f, 0.6f, 0.2f),     // Phönix
            new Color(0.45f, 0.85f, 0.4f), // Weltenbaum
        };
        // Standard-Eltern (Basis-Element-Index), falls ein Fusions-Buddy ohne Fusion platziert wurde
        public static readonly Vector2Int[] DefaultParents =
        {
            new Vector2Int(0, 3), // Blitz = Feuer + Licht
            new Vector2Int(1, 3), // Wasser = Eis + Licht
            new Vector2Int(0, 1), // Luft = Feuer + Eis
            new Vector2Int(2, 3), // Schatten = Erde + Licht
            new Vector2Int(0, 2), // Magma = Feuer + Erde
            new Vector2Int(1, 2), // Kristall = Eis + Erde
        };
        // Super-Elementare: drei Eltern-Elemente
        public static readonly Vector3Int[] DefaultTriParents =
        {
            new Vector3Int(0, 1, 2), // Vulkan-Titan = Feuer + Eis + Erde
            new Vector3Int(0, 1, 3), // Sturmfürst = Feuer + Eis + Licht
            new Vector3Int(0, 2, 3), // Phönix = Feuer + Erde + Licht
            new Vector3Int(1, 2, 3), // Weltenbaum = Eis + Erde + Licht
        };

        public static bool IsSuper(FusionElement e) => (int)e >= (int)FusionElement.VolcanoTitan;

        // Eltern-Elemente (z = -1 bei 2er-Fusionen)
        public static Vector3Int GetDefaultParents(FusionElement e)
        {
            int i = (int)e;
            if (IsSuper(e))
                return DefaultTriParents[Mathf.Clamp(i - (int)FusionElement.VolcanoTitan, 0, DefaultTriParents.Length - 1)];
            var p = DefaultParents[Mathf.Clamp(i, 0, DefaultParents.Length - 1)];
            return new Vector3Int(p.x, p.y, -1);
        }

        private static int Clamp(FusionElement e) => Mathf.Clamp((int)e, 0, Names.Length - 1);
        public static string Name(FusionElement e) => Names[Clamp(e)];
        public static string DisplayName(FusionElement e) => DisplayNames[Clamp(e)];
        public static Color GetColor(FusionElement e) => Colors[Clamp(e)];
    }

    // Basis für Fusions-Buddies: nur eine Stufe (keine Aufwertung), Werte direkt aus der Config,
    // Schrein-Bonus = Produkt der Boni beider Eltern-Elemente. Alle Visuals funktionieren ohne Prefabs (Laufzeit-LineRenderer).
    public abstract class FusionBuddy : ElementalBuddy
    {
        [Header("Fusion")]
        public FusionElement Element;
        public Transform FirePoint;
        [HideInInspector] public int ParentElementA = -1, ParentElementB = -1;

        public override bool IsFusion => true;
        public override int MaxLevel => 1;

        public int ParentA => ParentElementA >= 0 ? ParentElementA : FusionInfo.GetDefaultParents(Element).x;
        public int ParentB => ParentElementB >= 0 ? ParentElementB : FusionInfo.GetDefaultParents(Element).y;

        // Bitmaske der Eltern-Elemente (1 << Index)
        public virtual int ElementMask => (1 << ParentA) | (1 << ParentB);

        public override int ElementIndex => Mathf.Clamp(ParentA, 0, ShrineBonuses.ElementCount - 1);
        public override string DisplayName => FusionInfo.Name(Element);
        public override string StageName => FusionInfo.DisplayName(Element);
        public Color ElementColor => FusionInfo.GetColor(Element);

        protected override float ShrineDamageMultiplier =>
            ShrineBonuses.GetDamageMultiplier(ParentA) * ShrineBonuses.GetDamageMultiplier(ParentB);

        // Boss-Bonus aus der Config (UnitConfigSO.BossDamageMultiplier, Default 1)
        public float BossDamageMultiplier => Config != null && Config.BossDamageMultiplier > 0f ? Config.BossDamageMultiplier : 1f;

        // Schaden gegen diesen Gegner: × BossDamageMultiplier, wenn er ein Boss ist (Direkttreffer, Fluch, Brand, Pfütze)
        protected float DamageAgainst(EnemyBrain e, float damage) => e != null && e.IsBoss ? damage * BossDamageMultiplier : damage;

        // Element der Subklasse (Default fürs Inspector-Feld)
        protected abstract FusionElement DefaultElement { get; }

        // Defaults beim Hinzufügen der Komponente im Editor
        protected virtual void Reset()
        {
            BaseMaxHP = 120f;
            Element = DefaultElement;
        }

        // ---------------- Helfer für Subklassen ----------------

        protected Vector3 FirePosition => FirePoint != null ? FirePoint.position : transform.position + Vector3.up;

        protected static Vector3 BodyPoint(EnemyBrain e) => e.transform.position + Vector3.up * 0.9f;

        private static readonly List<EnemyBrain> _enemyBuffer = new List<EnemyBrain>();

        // Alle Gegner (Tag "Enemy", EnemyBrain) im Radius; Ergebnis-Liste wird wiederverwendet -> sofort verarbeiten/kopieren
        protected static List<EnemyBrain> FindEnemies(Vector3 center, float radius)
        {
            _enemyBuffer.Clear();
            if (radius <= 0f) return _enemyBuffer;
            foreach (var hit in Physics.OverlapSphere(center, radius))
            {
                if (!hit.CompareTag("Enemy")) continue;
                var e = hit.GetComponentInParent<EnemyBrain>();
                if (e != null && e.isActiveAndEnabled && !_enemyBuffer.Contains(e)) _enemyBuffer.Add(e);
            }
            return _enemyBuffer;
        }

        // Nächster Gegner im Radius, optional ohne bereits getroffene
        protected static EnemyBrain FindNearestEnemy(Vector3 center, float radius, ICollection<EnemyBrain> exclude = null)
        {
            EnemyBrain best = null;
            float bestDist = float.MaxValue;
            foreach (var e in FindEnemies(center, radius))
            {
                if (exclude != null && exclude.Contains(e)) continue;
                float d = (e.transform.position - center).sqrMagnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = e;
                }
            }
            return best;
        }

        protected static void SpawnVfx(GameObject prefab, Vector3 position, float lifetime)
        {
            if (prefab == null) return;
            Destroy(Instantiate(prefab, position, Quaternion.identity), lifetime);
        }
    }
}
