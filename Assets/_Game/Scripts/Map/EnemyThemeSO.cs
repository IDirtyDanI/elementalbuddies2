using System;
using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Gegner-Theme einer Map (Walddorf-Plan; Datenmodell P0, Auflösung C-Theme).
    // Ordnet jeder EnemyRole eine EnemyConfigSO zu. Die Wellen-Assets (WaveConfig, ExtraGroups, RamConfig, Schrein-/
    // Händler-Angreifer) verweisen auf die kanonischen Untoten-Configs; der WaveManager tauscht sie beim Spawnen per
    // Resolve gegen die Config dieses Themes (gleiche Rolle). Rollen ohne Eintrag bleiben unverändert – ein Theme ohne
    // Einträge ist die Identität. Die Stadt (Theme_Undead) trägt nur den Flieger (Riesen-Fledermaus), weil es dafür keine
    // kanonische Untoten-Config gibt.
    // Assets: Resources/Maps/Theme_*.asset
    [CreateAssetMenu(fileName = "Theme_", menuName = "Elemental Buddies/Map/Gegner-Theme")]
    public class EnemyThemeSO : ScriptableObject
    {
        [Serializable]
        public struct RoleEntry
        {
            public EnemyRole Role;
            public EnemyConfigSO Config;
        }

        [Tooltip("Stabile Id (z. B. \"undead\", \"forest\").")]
        public string Id = "undead";
        public string DisplayName = "Untote";
        [Tooltip("Rolle → Config dieses Themes. Leer = Identität (kanonische Configs aus den Wellen-Assets).")]
        public List<RoleEntry> Roles = new List<RoleEntry>();
        [Tooltip("Ton der Boss-Ankündigung (Untote: BossSpawn = Schlachtruf; Dorf später ein Ork-Kriegsschrei).")]
        public SfxId BossSpawnSfx = SfxId.BossSpawn;

        [Header("Bannschild (Champion-Bedrohungen)")]
        [Tooltip("Einheitliche Farbe des Bannschilds (Stadt: geisterhaftes Violett, Dorf: Bernstein) – Schild, Bann-Strahl, Minimap.")]
        public Color WardColor = new Color(0.66f, 0.42f, 1f);
        [Tooltip("Schild-Modell (Kind „Ward_Shell“, Durchmesser 1 m um den Pivot; optional „Ward_Shards“ für den Bruch). " +
                 "Leer = prozedurale Kugel in WardColor.")]
        public GameObject WardShellPrefab;

        // Ohne Einträge ändert das Theme nichts
        public bool IsIdentity => Roles == null || Roles.Count == 0;

        // Config dieses Themes für eine Rolle (null = keine Zuordnung → kanonische Config verwenden)
        public EnemyConfigSO ConfigFor(EnemyRole role)
        {
            if (Roles == null) return null;
            foreach (var e in Roles)
                if (e.Role == role) return e.Config;
            return null;
        }

        // Config dieses Themes für die Rolle der kanonischen Config; ohne Zuordnung die kanonische selbst.
        // Idempotent: Resolve(Resolve(c)) == Resolve(c).
        public EnemyConfigSO Resolve(EnemyConfigSO canonical)
        {
            if (canonical == null || IsIdentity) return canonical;
            var c = ConfigFor(canonical.Role);
            return c != null ? c : canonical;
        }

        // Null-sichere Auflösung (theme == null → Identität)
        public static EnemyConfigSO Resolve(EnemyThemeSO theme, EnemyConfigSO canonical) =>
            theme != null ? theme.Resolve(canonical) : canonical;
    }
}
