using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // Plan „Fesselung“, Sprint 3 (Builds): richtet reproduzierbar ein
    //  1. Kartenpool 7 → 32: neue Wellenkarten mit Seltenheit, Affinität, Schlüsselwörtern und Sonderwirkung
    //     (Assets/ScriptableObjects/Upgrades/Draft/), bestehende Karten bekommen Seltenheit/Affinität,
    //     „Arkane Wucht“ heißt jetzt „Kampfeswut“ (wirkt auf alle Champions)
    //  2. Icons (Blender: art-src/icons/sprint3_icons.py) als Single-Sprites
    //  3. Szene: UpgradeManager.AllUpgrades (nur anhängen – Indizes gehen übers Netz), Elite-Icons + Aura am WaveManager,
    //     Sounds/VFX über GameFeelSetup
    // Menü: BuddyTD → Game Feel → Sprint 3 einrichten (Karten, Eliten).
    public static class DraftSetup
    {
        private const string UpgradeDir = "Assets/ScriptableObjects/Upgrades/";
        private const string DraftDir = UpgradeDir + "Draft/";
        private const string IconDir = "Assets/_Game/UI/Icons/";
        private const string GameScene = "Assets/test.unity";

        private class Def
        {
            public string Asset, Title, Desc, Icon, Keywords;
            public CardRarity Rarity;
            public CardAffinity Affinity;
            public UpgradeTarget Target = UpgradeTarget.Global;
            public StatType Stat = StatType.None;
            public float Value, Value2;
            public bool Percent = true;
            public CardEffect Effect = CardEffect.None;
            public int MaxPicks;
        }

        private static IEnumerable<Def> NewCards()
        {
            // ---------- Gewöhnlich ----------
            yield return new Def { Asset = "IceStat", Title = "Frostbiss", Desc = "Eis-Buddies verursachen 10 % mehr Schaden.", Icon = "card_frostbiss",
                Rarity = CardRarity.Common, Affinity = CardAffinity.Ice, Target = UpgradeTarget.IceUnit, Stat = StatType.Damage, Value = 10, Keywords = "Eis" };
            yield return new Def { Asset = "EarthStat", Title = "Felsenfaust", Desc = "Die Aura der Erd-Buddies verursacht 15 % mehr Schaden.", Icon = "card_felsenfaust",
                Rarity = CardRarity.Common, Affinity = CardAffinity.Earth, Target = UpgradeTarget.EarthUnit, Stat = StatType.Damage, Value = 15, Keywords = "Erde" };
            yield return new Def { Asset = "LightStat", Title = "Sonnenstrahl", Desc = "Licht-Buddies verursachen 10 % mehr Schaden.", Icon = "card_sonnenstrahl",
                Rarity = CardRarity.Common, Affinity = CardAffinity.Light, Target = UpgradeTarget.LightUnit, Stat = StatType.Damage, Value = 10, Keywords = "Licht" };
            yield return new Def { Asset = "AllRange", Title = "Weitblick", Desc = "Alle Buddies haben 8 % mehr Reichweite.", Icon = "card_weitblick",
                Rarity = CardRarity.Common, Affinity = CardAffinity.Neutral, Target = UpgradeTarget.AllUnits, Stat = StatType.Range, Value = 8 };
            yield return new Def { Asset = "AllFireRate", Title = "Schnellfeuer", Desc = "Alle Buddies greifen 6 % schneller an.", Icon = "card_schnellfeuer",
                Rarity = CardRarity.Common, Affinity = CardAffinity.Neutral, Target = UpgradeTarget.AllUnits, Stat = StatType.FireRate, Value = 6 };
            yield return new Def { Asset = "PlayerCooldown", Title = "Kampfrhythmus", Desc = "Deine Fähigkeiten laden 10 % schneller auf.", Icon = "card_kampfrhythmus",
                Rarity = CardRarity.Common, Affinity = CardAffinity.Champion, Target = UpgradeTarget.Player, Stat = StatType.Cooldown, Value = 10, MaxPicks = 5 };
            yield return new Def { Asset = "PlayerMobility", Title = "Ausweichkunst", Desc = "Rolle und Blink reichen 20 % weiter.", Icon = "card_ausweichkunst",
                Rarity = CardRarity.Common, Affinity = CardAffinity.Champion, Target = UpgradeTarget.Player, Stat = StatType.Mobility, Value = 20, MaxPicks = 3 };
            yield return new Def { Asset = "PlayerHealth", Title = "Zähigkeit", Desc = "Dein Champion hat 20 % mehr Leben.", Icon = "card_zaehigkeit",
                Rarity = CardRarity.Common, Affinity = CardAffinity.Champion, Target = UpgradeTarget.Player, Stat = StatType.Health, Value = 20 };
            yield return new Def { Asset = "BuddyHealth", Title = "Steinhaut", Desc = "Alle Buddies haben 15 % mehr Leben.", Icon = "card_steinhaut",
                Rarity = CardRarity.Common, Affinity = CardAffinity.Neutral, Effect = CardEffect.BuddyHealth, Value = 15 };
            yield return new Def { Asset = "WaveHeal", Title = "Feldscher", Desc = "Buddies heilen am Wellenende zusätzlich 25 % ihres Lebens.", Icon = "card_feldscher",
                Rarity = CardRarity.Common, Affinity = CardAffinity.Neutral, Effect = CardEffect.WaveEndHeal, Value = 25, MaxPicks = 2 };

            // ---------- Selten: Schlüsselwort-Synergien ----------
            yield return new Def { Asset = "FireIgnite", Title = "Glutgeschosse", Desc = "Treffer von Feuer-Buddies setzen Gegner 3 s in Brand (pro Sekunde 15 % des Treffers).", Icon = "card_glutgeschosse",
                Rarity = CardRarity.Rare, Affinity = CardAffinity.Fire, Effect = CardEffect.FireIgnite, Value = 15, Value2 = 3, Keywords = "Brand", MaxPicks = 3 };
            yield return new Def { Asset = "SteamShock", Title = "Dampfschock", Desc = "Brennende Gegner erleiden 30 % mehr Schaden von Eis-Buddies.", Icon = "card_dampfschock",
                Rarity = CardRarity.Rare, Affinity = CardAffinity.Ice, Effect = CardEffect.SteamShock, Value = 30, Keywords = "Brand, Eis" };
            yield return new Def { Asset = "IceFreeze", Title = "Raureif", Desc = "Jeder 4. Schuss eines Eis-Buddys friert das Ziel 1 s ein.", Icon = "card_raureif",
                Rarity = CardRarity.Rare, Affinity = CardAffinity.Ice, Effect = CardEffect.IceFreeze, Value = 4, Value2 = 1f, Keywords = "Frost", MaxPicks = 3 };
            yield return new Def { Asset = "FrostShatter", Title = "Splitterfrost", Desc = "Eingefrorene Gegner zerspringen beim Tod: 35 % ihres Lebens als Schaden an Gegnern im Umkreis.", Icon = "card_splitterfrost",
                Rarity = CardRarity.Rare, Affinity = CardAffinity.Ice, Effect = CardEffect.FrostShatter, Value = 35, Value2 = 3.5f, Keywords = "Frost", MaxPicks = 2 };
            yield return new Def { Asset = "TauntMark", Title = "Spottmal", Desc = "Vom Erd-Buddy verspottete Gegner erleiden 20 % mehr Schaden.", Icon = "card_spottmal",
                Rarity = CardRarity.Rare, Affinity = CardAffinity.Earth, Effect = CardEffect.TauntVulnerable, Value = 20, Keywords = "Spott" };
            yield return new Def { Asset = "EarthSlow", Title = "Schwere Erde", Desc = "Die Aura der Erd-Buddies verlangsamt Gegner um 25 %.", Icon = "card_schwere_erde",
                Rarity = CardRarity.Rare, Affinity = CardAffinity.Earth, Effect = CardEffect.EarthSlow, Value = 25, Keywords = "Langsam", MaxPicks = 2 };
            yield return new Def { Asset = "SlowVulnerable", Title = "Eiseskälte", Desc = "Verlangsamte oder eingefrorene Gegner erleiden 15 % mehr Schaden von Buddies.", Icon = "card_eiseskaelte",
                Rarity = CardRarity.Rare, Affinity = CardAffinity.Ice, Effect = CardEffect.SlowVulnerable, Value = 15, Keywords = "Langsam, Frost" };
            yield return new Def { Asset = "LightCurse", Title = "Läuterung", Desc = "Der Strahl der Licht-Buddies verflucht 3 s: +15 % erlittener Schaden, Rüstung zählt nicht.", Icon = "card_laeuterung",
                Rarity = CardRarity.Rare, Affinity = CardAffinity.Light, Effect = CardEffect.LightCurse, Value = 15, Value2 = 3, Keywords = "Fluch", MaxPicks = 2 };
            yield return new Def { Asset = "WetVulnerable", Title = "Leitfähig", Desc = "Nasse Gegner erleiden 25 % mehr Schaden.", Icon = "card_leitfaehig",
                Rarity = CardRarity.Rare, Affinity = CardAffinity.Neutral, Effect = CardEffect.WetVulnerable, Value = 25, Keywords = "Nass" };
            yield return new Def { Asset = "EliteBounty", Title = "Kopfgeld", Desc = "Elite-Gegner und Bosse lassen 60 % mehr Seelensplitter fallen.", Icon = "card_kopfgeld",
                Rarity = CardRarity.Rare, Affinity = CardAffinity.Neutral, Effect = CardEffect.EliteBounty, Value = 60, Keywords = "Elite" };
            yield return new Def { Asset = "Interest", Title = "Zinseszins", Desc = "Am Wellenende bekommst du 8 % deiner Seelensplitter als Zinsen (höchstens 40).", Icon = "card_zinseszins",
                Rarity = CardRarity.Rare, Affinity = CardAffinity.Neutral, Effect = CardEffect.Interest, Value = 8, Value2 = 40, MaxPicks = 2 };

            // ---------- Episch: Build-Karten mit Zielkonflikt ----------
            // Build-Karten: höchstens EINE pro Run (CardEffects.IsBuildCard)
            yield return new Def { Asset = "GlassCannon", Title = "Glaskanone", Desc = "Build-Karte: Alle Buddies verursachen 30 % mehr Schaden, haben aber 30 % weniger Leben.", Icon = "card_glaskanone",
                Rarity = CardRarity.Epic, Affinity = CardAffinity.Neutral, Effect = CardEffect.GlassCannon, Value = 30, Value2 = 30, MaxPicks = 1 };
            yield return new Def { Asset = "Loner", Title = "Einzelgänger", Desc = "Build-Karte: Du verlierst 2 Buddy-Slots. Alle Buddies verursachen 35 % mehr Schaden.", Icon = "card_einzelgaenger",
                Rarity = CardRarity.Epic, Affinity = CardAffinity.Neutral, Effect = CardEffect.Loner, Value = 35, Value2 = 2, MaxPicks = 1 };
            yield return new Def { Asset = "Purity", Title = "Elementar-Harmonie", Desc = "Build-Karte: Buddies verursachen 8 % mehr Schaden je Element, das du gebaut hast (bis +32 %).", Icon = "card_reinheit",
                Rarity = CardRarity.Epic, Affinity = CardAffinity.Neutral, Effect = CardEffect.Harmony, Value = 8, Value2 = 0, MaxPicks = 1 };
            yield return new Def { Asset = "BloodPact", Title = "Blutpakt", Desc = "Dein Champion verursacht 60 % mehr Schaden, hat aber 25 % weniger Leben.", Icon = "card_blutpakt",
                Rarity = CardRarity.Epic, Affinity = CardAffinity.Champion, Target = UpgradeTarget.Player, Stat = StatType.Damage, Value = 60, Effect = CardEffect.BloodPact, Value2 = 25, MaxPicks = 1 };
        }

        // Bestehende Karten: Seltenheit, Affinität, Schlüsselwörter (Titel/Icon nur bei Arkane Wucht)
        private static void UpdateExisting()
        {
            Existing("FireStat", CardRarity.Common, CardAffinity.Fire, "Feuer");
            Existing("PlayerStat", CardRarity.Common, CardAffinity.Champion, null, "Kampfeswut", "card_kampfeswut");
            Existing("Player speed", CardRarity.Common, CardAffinity.Champion, null);
            Existing("PlayerManaboost", CardRarity.Common, CardAffinity.Champion, null);
            Existing("PlayerManaRegen", CardRarity.Common, CardAffinity.Champion, null);
            Existing("BuddySlot", CardRarity.Common, CardAffinity.Neutral, null); // Selten machte Erstspieler-Runs kürzer (Modell)
            Existing("ShardGain", CardRarity.Common, CardAffinity.Neutral, null);
        }

        private static void Existing(string asset, CardRarity r, CardAffinity a, string keywords, string title = null, string icon = null)
        {
            var up = AssetDatabase.LoadAssetAtPath<UpgradeDefinitionSO>(UpgradeDir + asset + ".asset");
            if (up == null) { Debug.LogWarning("DraftSetup: fehlt " + asset); return; }
            up.Rarity = r;
            up.Affinity = a;
            if (keywords != null) up.Keywords = keywords;
            if (title != null) up.Title = title;
            if (icon != null) up.Icon = LoadIcon(icon);
            EditorUtility.SetDirty(up);
        }

        [MenuItem("BuddyTD/Game Feel/Sprint 3 einrichten (Karten, Eliten)")]
        public static void SetupAll()
        {
            ImportIcons();
            UpdateExisting();
            var created = new List<UpgradeDefinitionSO>();
            Directory.CreateDirectory(DraftDir);
            foreach (var d in NewCards())
            {
                string path = DraftDir + d.Asset + ".asset";
                var up = AssetDatabase.LoadAssetAtPath<UpgradeDefinitionSO>(path);
                if (up == null)
                {
                    up = ScriptableObject.CreateInstance<UpgradeDefinitionSO>();
                    AssetDatabase.CreateAsset(up, path);
                }
                up.Title = d.Title;
                up.Description = d.Desc;
                up.Icon = LoadIcon(d.Icon);
                up.Type = UpgradeType.StatIncrease;
                up.Target = d.Target;
                up.StatToBuff = d.Stat;
                up.Value = d.Value;
                up.IsPercentage = d.Percent;
                up.Effect = d.Effect;
                up.Value2 = d.Value2;
                up.Keywords = d.Keywords;
                up.Rarity = d.Rarity;
                up.Affinity = d.Affinity;
                up.MaxPicks = d.MaxPicks;
                EditorUtility.SetDirty(up);
                created.Add(up);
            }
            AssetDatabase.SaveAssets();

            // Sounds + VFX (Kartenaufdecken, Splitterfrost, Elite-Aura) und FeedbackDirector
            GameFeelSetup.SetupAll();
            SetupScene(created);
            Debug.Log($"DraftSetup: fertig ({created.Count} neue Karten).");
        }

        private static void ImportIcons()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { IconDir.TrimEnd('/') }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var file = Path.GetFileName(path);
                if (!file.StartsWith("card_") && !file.StartsWith("elite_")) continue;
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp == null) continue;
                if (imp.textureType == TextureImporterType.Sprite && imp.spriteImportMode == SpriteImportMode.Single) continue;
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single; // Projekt-Vorgabe ist Multiple
                imp.alphaIsTransparency = true;
                imp.mipmapEnabled = false;
                imp.SaveAndReimport();
            }
        }

        private static Sprite LoadIcon(string name)
        {
            var s = AssetDatabase.LoadAssetAtPath<Sprite>(IconDir + name + ".png");
            if (s == null) Debug.LogWarning("DraftSetup: Icon fehlt: " + name);
            return s;
        }

        private static void SetupScene(List<UpgradeDefinitionSO> cards)
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != GameScene)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                scene = EditorSceneManager.OpenScene(GameScene);
            }
            var um = Object.FindFirstObjectByType<UpgradeManager>(FindObjectsInactive.Include);
            if (um != null)
            {
                if (um.AllUpgrades == null) um.AllUpgrades = new List<UpgradeDefinitionSO>();
                foreach (var c in cards)
                    if (!um.AllUpgrades.Contains(c)) um.AllUpgrades.Add(c); // nur anhängen (Indizes im Netz)
                EditorUtility.SetDirty(um);
            }
            var wm = Object.FindFirstObjectByType<WaveManager>(FindObjectsInactive.Include);
            if (wm != null)
            {
                wm.EliteAuraPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameFeelSetup.EliteAuraPath);
                wm.EliteIcons = new[]
                {
                    LoadIcon("elite_fireproof"), LoadIcon("elite_frostguard"), LoadIcon("elite_swift"),
                    LoadIcon("elite_shieldbearer"), LoadIcon("elite_shardthief"),
                };
                EditorUtility.SetDirty(wm);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }
}
