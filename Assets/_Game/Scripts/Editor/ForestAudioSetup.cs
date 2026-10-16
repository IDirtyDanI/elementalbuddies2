using System.Text;
using UnityEditor;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // Walddorf Etappe 3 (A-Ramme/Audio, 2026-10-16): eigene Gegner-Klänge des Dorf-Themes zuweisen.
    // Menü: BuddyTD → Walddorf → Dorf-Klänge zuweisen. Idempotent.
    //  1. GameAudio-Einträge (SfxTable von GameFeelSetup) ins Kern-Prefab Managers.prefab (Stadt + Walddorf)
    //  2. Nur Ton-Felder der Dorf-/Flieger-Configs (MeleeHitSfx, AttackSfx, DeathSfx, SpawnSfx – Balance bleibt; die Felder
    //     stehen in ThemeParityTests.OptikFields)
    //  3. Theme_Forest.BossSpawnSfx = OrkWarcry (Boss-Ankündigung im Dorf)
    // Hinweis: ThemeSetup.SetupVariant kopiert die Untoten-Config per CopySerialized und setzt dabei die Ton-Felder zurück –
    // nach „Dorf-Theme einrichten“ dieses Menü erneut ausführen.
    public static class ForestAudioSetup
    {
        private const string ForestDir = "Assets/ScriptableObjects/Configs/Forest/";
        private const string ConfigDir = "Assets/ScriptableObjects/Configs/";

        private struct Row
        {
            public string Path;
            public SfxId? Hit, Attack, Death, Spawn;
        }

        private static readonly Row[] Rows =
        {
            new Row { Path = ForestDir + "Goblin_Pluenderer.asset", Hit = SfxId.GoblinStab, Death = SfxId.GoblinScreech },
            new Row { Path = ForestDir + "Goblin_Bogenschuetze.asset", Attack = SfxId.GoblinBowShot, Death = SfxId.GoblinScreech },
            new Row { Path = ForestDir + "Ork_Krieger.asset", Hit = SfxId.OrkGrunt, Death = SfxId.OrkDeath },
            new Row { Path = ForestDir + "Wolf.asset", Hit = SfxId.WolfBite, Death = SfxId.WolfYelp, Spawn = SfxId.WolfHowl },
            new Row { Path = ForestDir + "Waldspinne.asset", Hit = SfxId.SpiderBite, Death = SfxId.SpiderDeath },
            new Row { Path = ConfigDir + "Flyer_Wyvern.asset", Attack = SfxId.WingFlap, Death = SfxId.WyvernScreech, Spawn = SfxId.WyvernScreech },
            new Row { Path = ConfigDir + "Flyer_Bat.asset", Attack = SfxId.BatFlap, Death = SfxId.BatScreech, Spawn = SfxId.BatScreech },
        };

        [MenuItem("BuddyTD/Walddorf/Dorf-Klänge zuweisen (Configs, Theme, Managers-Prefab)")]
        private static void Menu() => Debug.Log(Setup());

        public static string Setup()
        {
            var sb = new StringBuilder("ForestAudioSetup:");
            if (EditorApplication.isPlayingOrWillChangePlaymode) { sb.Append(" nur im Edit-Modus."); return sb.ToString(); }
            sb.Append("\n  " + GameFeelSetup.SetupSoundsInManagersPrefab());
            foreach (var r in Rows)
            {
                var c = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>(r.Path);
                if (c == null) { sb.Append("\n  fehlt: " + r.Path); continue; }
                if (r.Hit.HasValue) { c.MeleeHitSound = true; c.MeleeHitSfx = r.Hit.Value; }
                c.AttackSound = r.Attack.HasValue; if (r.Attack.HasValue) c.AttackSfx = r.Attack.Value;
                c.DeathSound = r.Death.HasValue; if (r.Death.HasValue) c.DeathSfx = r.Death.Value;
                c.SpawnSound = r.Spawn.HasValue; if (r.Spawn.HasValue) c.SpawnSfx = r.Spawn.Value;
                EditorUtility.SetDirty(c);
                sb.Append($"\n  {c.name}: Treffer {(c.MeleeHitSound ? c.MeleeHitSfx.ToString() : "-")}, Angriff {(c.AttackSound ? c.AttackSfx.ToString() : "-")}, " +
                          $"Tod {(c.DeathSound ? c.DeathSfx.ToString() : "Knochenbruch")}, Spawn {(c.SpawnSound ? c.SpawnSfx.ToString() : "-")}");
            }
            var theme = AssetDatabase.LoadAssetAtPath<EnemyThemeSO>(ThemeSetup.ForestThemePath);
            if (theme != null)
            {
                theme.BossSpawnSfx = SfxId.OrkWarcry;
                EditorUtility.SetDirty(theme);
                sb.Append("\n  Theme_Forest.BossSpawnSfx = OrkWarcry");
            }
            else sb.Append("\n  fehlt: " + ThemeSetup.ForestThemePath);
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }
    }
}
