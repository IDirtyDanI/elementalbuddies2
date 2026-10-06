using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using TMPro;

namespace ElementalBuddies.EditorTools
{
    // Händler: Kartenpool (ScriptableObjects), Händlerstände in der Szene, Manager + UI.
    // Menü „BuddyTD → Händler → …“. Alles idempotent: bestehende Assets/Objekte werden aktualisiert, nicht dupliziert.
    public static class MerchantSetup
    {
        public const string CardFolder = "Assets/ScriptableObjects/MerchantCards";
        public const string IconFolder = "Assets/_Game/UI/Icons";
        public const string GroupPath = "Environment/Stadt/Haendler";
        public const string StallFolder = "Assets/_Game/Prefabs/Town";
        public const string MaterialFolder = "Assets/_Game/Models/Town/Materials";

        // ---------------- Kartenpool ----------------

        // (Fähigkeit, Stat, Wert) – Werte: Prozent bei Prozent/Reduktion, sonst flach (siehe AbilityMods.KindOf)
        private struct CardDef
        {
            public AbilityId Ability;
            public AbilityStat Stat;
            public float Value;
            public CardDef(AbilityId a, AbilityStat s, float v) { Ability = a; Stat = s; Value = v; }
        }

        private static readonly CardDef[] Pool =
        {
            // ---- Magier ----
            new CardDef(AbilityId.ArcaneBall, AbilityStat.Damage, 20),
            new CardDef(AbilityId.ArcaneBall, AbilityStat.Area, 25),
            new CardDef(AbilityId.ArcaneBall, AbilityStat.Cooldown, 15),
            new CardDef(AbilityId.ArcaneBall, AbilityStat.Speed, 25),
            new CardDef(AbilityId.Blink, AbilityStat.Range, 20),
            new CardDef(AbilityId.Blink, AbilityStat.Cooldown, 20),
            new CardDef(AbilityId.Blink, AbilityStat.Duration, 50),
            new CardDef(AbilityId.FireWave, AbilityStat.Damage, 20),
            new CardDef(AbilityId.FireWave, AbilityStat.Range, 15),
            new CardDef(AbilityId.FireWave, AbilityStat.Cooldown, 12),
            new CardDef(AbilityId.FireWave, AbilityStat.Duration, 25),
            new CardDef(AbilityId.FrostNova, AbilityStat.Damage, 20),
            new CardDef(AbilityId.FrostNova, AbilityStat.Area, 15),
            new CardDef(AbilityId.FrostNova, AbilityStat.Cooldown, 12),
            new CardDef(AbilityId.FrostNova, AbilityStat.Duration, 25),
            new CardDef(AbilityId.StoneWall, AbilityStat.Length, 2),
            new CardDef(AbilityId.StoneWall, AbilityStat.Duration, 25),
            new CardDef(AbilityId.StoneWall, AbilityStat.Cooldown, 12),
            new CardDef(AbilityId.HolyCircle, AbilityStat.Area, 15),
            new CardDef(AbilityId.HolyCircle, AbilityStat.Heal, 25),
            new CardDef(AbilityId.HolyCircle, AbilityStat.Duration, 25),
            new CardDef(AbilityId.HolyCircle, AbilityStat.Cooldown, 12),

            // ---- Schwertkämpfer ----
            new CardDef(AbilityId.SwordSlash, AbilityStat.Damage, 20),
            new CardDef(AbilityId.SwordSlash, AbilityStat.Range, 20),
            new CardDef(AbilityId.SwordSlash, AbilityStat.Angle, 20),
            new CardDef(AbilityId.SwordSlash, AbilityStat.Speed, 15),
            new CardDef(AbilityId.ShieldBlock, AbilityStat.ManaCost, 20),
            new CardDef(AbilityId.ShieldBlock, AbilityStat.Angle, 30),
            new CardDef(AbilityId.ShieldBlock, AbilityStat.Speed, 20),
            new CardDef(AbilityId.ShieldBlock, AbilityStat.Cooldown, 30),
            new CardDef(AbilityId.FlameWhirl, AbilityStat.Damage, 20),
            new CardDef(AbilityId.FlameWhirl, AbilityStat.Area, 15),
            new CardDef(AbilityId.FlameWhirl, AbilityStat.Cooldown, 12),
            new CardDef(AbilityId.FlameWhirl, AbilityStat.Duration, 25),
            new CardDef(AbilityId.FrostStrike, AbilityStat.Damage, 20),
            new CardDef(AbilityId.FrostStrike, AbilityStat.Range, 15),
            new CardDef(AbilityId.FrostStrike, AbilityStat.Cooldown, 12),
            new CardDef(AbilityId.FrostStrike, AbilityStat.Duration, 25),
            new CardDef(AbilityId.Earthquake, AbilityStat.Damage, 20),
            new CardDef(AbilityId.Earthquake, AbilityStat.Area, 15),
            new CardDef(AbilityId.Earthquake, AbilityStat.Cooldown, 12),
            new CardDef(AbilityId.Earthquake, AbilityStat.Duration, 25),
            new CardDef(AbilityId.LightOath, AbilityStat.Area, 15),
            new CardDef(AbilityId.LightOath, AbilityStat.Duration, 25),
            new CardDef(AbilityId.LightOath, AbilityStat.Heal, 25),
            new CardDef(AbilityId.LightOath, AbilityStat.Cooldown, 12),

            // ---- Bogenschütze ----
            new CardDef(AbilityId.ArrowShot, AbilityStat.Damage, 20),
            new CardDef(AbilityId.ArrowShot, AbilityStat.Pierce, 1),
            new CardDef(AbilityId.ArrowShot, AbilityStat.Speed, 15),
            new CardDef(AbilityId.ArrowShot, AbilityStat.Range, 20),
            new CardDef(AbilityId.Roll, AbilityStat.Charges, 1),
            new CardDef(AbilityId.Roll, AbilityStat.Range, 20),
            new CardDef(AbilityId.Roll, AbilityStat.Cooldown, 20),
            new CardDef(AbilityId.FireArrowRain, AbilityStat.Damage, 20),
            new CardDef(AbilityId.FireArrowRain, AbilityStat.Area, 15),
            new CardDef(AbilityId.FireArrowRain, AbilityStat.Count, 1),
            new CardDef(AbilityId.FireArrowRain, AbilityStat.Cooldown, 12),
            new CardDef(AbilityId.FrostArrow, AbilityStat.Damage, 20),
            new CardDef(AbilityId.FrostArrow, AbilityStat.Duration, 25),
            new CardDef(AbilityId.FrostArrow, AbilityStat.Range, 20),
            new CardDef(AbilityId.FrostArrow, AbilityStat.Cooldown, 12),
            new CardDef(AbilityId.ThornTrap, AbilityStat.Count, 2),
            new CardDef(AbilityId.ThornTrap, AbilityStat.Area, 15),
            new CardDef(AbilityId.ThornTrap, AbilityStat.Duration, 25),
            new CardDef(AbilityId.ThornTrap, AbilityStat.Damage, 20),
            new CardDef(AbilityId.LightArrow, AbilityStat.Damage, 20),
            new CardDef(AbilityId.LightArrow, AbilityStat.Range, 15),
            new CardDef(AbilityId.LightArrow, AbilityStat.Duration, 25),
            new CardDef(AbilityId.LightArrow, AbilityStat.Cooldown, 12),
        };

        [MenuItem("BuddyTD/Händler/Kartenpool neu erzeugen")]
        public static void GenerateCardsMenu()
        {
            Debug.Log(GenerateCards());
        }

        // Erzeugt/aktualisiert alle Karten-Assets. Texte und Icons kommen aus den Kits des Player-Prefabs.
        public static string GenerateCards()
        {
            EnsureFolder(CardFolder);
            var kits = LoadKits();
            var sb = new StringBuilder();
            int created = 0, updated = 0;
            foreach (var def in Pool)
            {
                ChampionClass cls = MerchantCardSO.ClassOf(def.Ability);
                ChampionKit kit;
                kits.TryGetValue(cls, out kit);
                string path = $"{CardFolder}/{cls}_{def.Ability}_{def.Stat}.asset";
                var card = AssetDatabase.LoadAssetAtPath<MerchantCardSO>(path);
                bool isNew = card == null;
                if (isNew) card = ScriptableObject.CreateInstance<MerchantCardSO>();

                card.Class = cls;
                card.Ability = def.Ability;
                card.Stat = def.Stat;
                card.Value = def.Value;

                string abilityName = kit != null ? kit.GetName(def.Ability) : def.Ability.ToString();
                string label = AbilityMods.StatName(def.Stat);
                if (kit != null && kit.TryGetStatValue(def.Ability, def.Stat, out float _, out string _, out string kitLabel) && !string.IsNullOrEmpty(kitLabel))
                    label = kitLabel;
                // Tempo-Karten zeigen in der Vorschau die (kürzere) Zeit – im Text das Tempo nennen
                if (def.Stat == AbilityStat.Speed && def.Ability == AbilityId.SwordSlash) label = "Kombo-Tempo";
                if (def.Stat == AbilityStat.Speed && def.Ability == AbilityId.ArrowShot) label = "Feuerrate";
                card.Title = $"{abilityName}: {AbilityMods.StatName(def.Stat)}";
                card.Description = $"{label} {AbilityMods.FormatValue(def.Stat, def.Value)}";
                card.Icon = FindAbilityIcon(kit, def.Ability);
                card.Badge = LoadBadge(def.Stat);

                if (isNew)
                {
                    AssetDatabase.CreateAsset(card, path);
                    created++;
                }
                else
                {
                    EditorUtility.SetDirty(card);
                    updated++;
                }
                sb.AppendLine($"{cls}\t{abilityName}\t{def.Stat}\t{AbilityMods.FormatValue(def.Stat, def.Value)}\t{card.Description}\ticon={(card.Icon != null ? card.Icon.name : "-")}");
            }
            AssetDatabase.SaveAssets();
            return $"MerchantSetup: {created} Karten neu, {updated} aktualisiert ({Pool.Length} gesamt).\n" + sb;
        }

        public static List<MerchantCardSO> LoadAllCards()
        {
            var list = new List<MerchantCardSO>();
            foreach (var guid in AssetDatabase.FindAssets("t:MerchantCardSO", new[] { CardFolder }))
            {
                var c = AssetDatabase.LoadAssetAtPath<MerchantCardSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (c != null) list.Add(c);
            }
            list.Sort((a, b) => a.Ability != b.Ability ? a.Ability.CompareTo(b.Ability) : a.Stat.CompareTo(b.Stat));
            return list;
        }

        private static Dictionary<ChampionClass, ChampionKit> LoadKits()
        {
            var dict = new Dictionary<ChampionClass, ChampionKit>();
            var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Player.prefab");
            if (player != null)
                foreach (var k in player.GetComponents<ChampionKit>()) dict[k.Class] = k;
            return dict;
        }

        private static Sprite FindAbilityIcon(ChampionKit kit, AbilityId id)
        {
            Sprite s = kit != null ? kit.GetIcon(id) : null;
            if (s != null) return s;
            int e = PlayerAbilities.ElementIndexOf(id);
            string[] emblems = { "emblem_fire", "emblem_ice", "emblem_earth", "emblem_light" };
            return e >= 0 ? LoadSprite($"{IconFolder}/{emblems[e]}.png") : null;
        }

        // badge_<name>.png (vom Hauptagenten, optional); Fallbacks für Mana/Heilung aus den HUD-Icons
        public static Sprite LoadBadge(AbilityStat stat)
        {
            string name = AbilityMods.BadgeName(stat);
            if (string.IsNullOrEmpty(name)) return null;
            Sprite s = LoadSprite($"{IconFolder}/badge_{name}.png");
            if (s != null) return s;
            if (stat == AbilityStat.ManaCost) return LoadSprite($"{IconFolder}/icon_mana.png");
            if (stat == AbilityStat.Heal) return LoadSprite($"{IconFolder}/icon_heart.png");
            return null;
        }

        public static Sprite LoadSprite(string path)
        {
            if (!File.Exists(path)) return null;
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null && imp.textureType != TextureImporterType.Sprite)
            {
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.alphaIsTransparency = true;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // ---------------- Platzierung ----------------

        // Händlerstand: Position (x, z) des Stand-Mittelpunkts, Blickrichtung (Grad, 0 = +Z) = Front zum Kreis
        public struct StallDef
        {
            public MerchantKind Kind;
            public float X, Z, Yaw;
            public float CircleX; // seitlicher Versatz des Kreises (lokal, + = rechts vom Stand aus gesehen)
        }

        public const string StandModelFolder = "Assets/_Game/Models/Town/Haendler";
        public const string FigureModelFolder = "Assets/_Game/Models/Characters/Merchants";
        public const string FigureControllerPath = "Assets/_Game/Animations/MerchantFigure.controller";
        // Grundfläche des Stand-Modells inkl. Fass/Kiste an den Seiten (Stand_*.fbx: x −2,51..2,61, z −1,41..1,37)
        public static readonly Vector3 StandColliderCenter = new Vector3(0.05f, 1.3f, -0.02f);
        public static readonly Vector3 StandColliderSize = new Vector3(5.15f, 2.6f, 2.8f);
        // Laterne an der vorderen Ecke des Modells (X im Export gespiegelt → lokal −1,85)
        public static readonly Vector3 LampLocal = new Vector3(-1.85f, 2.3f, 1.0f);

        // Abstand Stand-Mitte → Kreis-Mitte (entlang der Front)
        public const float CircleOffset = 4.2f;
        public const float CircleRadius = 3.5f;

        public static readonly StallDef[] Stalls =
        {
            new StallDef { Kind = MerchantKind.Waffen, X = -4.6f, Z = 4.3f, Yaw = 180f },  // Marktplatz, Nordost-Ecke
            new StallDef { Kind = MerchantKind.Feuer, X = 2.45f, Z = 21f, Yaw = 270f },  // Nordstraße, Ostseite
            new StallDef { Kind = MerchantKind.Eis, X = 13f, Z = 10.6f, Yaw = 180f },    // Nexusplatz, Nordwest-Einmündung
            new StallDef { Kind = MerchantKind.Erde, X = 2.45f, Z = -22f, Yaw = 270f },  // Südstraße, Ostseite
            new StallDef { Kind = MerchantKind.Licht, X = 13f, Z = -10.6f, Yaw = 0f, CircleX = 2.8f }, // Nexusplatz, Südwest-Einmündung
        };

        // Prüft Stand-Grundflächen (Überschneidung mit Collidern, ohne Terrain und ohne eigene Händler) und den Kreis
        // (Anteil begehbarer Punkte: NavMesh + kein Collider in Spielerhöhe). Optional Draufsicht-Render als PNG.
        public static string Evaluate(float[][] cands, string pngPath, float x0, float x1, float z0, float z1)
        {
            float FW = StandColliderSize.x, FD = StandColliderSize.z;
            var sb = new StringBuilder();
            var terr = Terrain.activeTerrain;
            for (int i = 0; i < cands.Length; i++)
            {
                var c = cands[i];
                var rot = Quaternion.Euler(0f, c[2], 0f);
                var fwd = rot * Vector3.forward;
                Vector3 p = new Vector3(c[0], 0f, c[1]);
                p.y = terr != null ? terr.SampleHeight(p) + terr.transform.position.y : 0f;
                sb.Append($"#{i} ({c[0]},{c[1]}) yaw {c[2]}: Stand überschneidet ");
                int hitsCount = 0;
                foreach (var h in Physics.OverlapBox(p + Vector3.up * 1.4f, new Vector3(FW / 2f, 1.3f, FD / 2f), rot, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (h is TerrainCollider || h.GetComponentInParent<Merchant>() != null) continue;
                    sb.Append(h.name).Append("[").Append(h.transform.parent != null ? h.transform.parent.name : "").Append("] ");
                    hitsCount++;
                }
                if (hitsCount == 0) sb.Append("nichts");
                Vector3 cc = p + fwd * CircleOffset + (rot * Vector3.right) * (c.Length > 3 ? c[3] : 0f);
                int tot = 0, ok = 0;
                for (float dx = -CircleRadius; dx <= CircleRadius; dx += 0.5f)
                    for (float dz = -CircleRadius; dz <= CircleRadius; dz += 0.5f)
                    {
                        if (dx * dx + dz * dz > CircleRadius * CircleRadius) continue;
                        tot++;
                        if (IsWalkable(cc + new Vector3(dx, 0f, dz))) ok++;
                    }
                sb.AppendLine($" | Kreis ({cc.x:0.0},{cc.z:0.0}) begehbar {ok}/{tot}");
            }
            if (!string.IsNullOrEmpty(pngPath)) RenderTop(cands, pngPath, x0, x1, z0, z1, FW, FD);
            return sb.ToString();
        }

        private static bool IsWalkable(Vector3 q)
        {
            if (!NavMesh.SamplePosition(q, out NavMeshHit nh, 0.6f, NavMesh.AllAreas)) return false;
            foreach (var h in Physics.OverlapCapsule(nh.position + Vector3.up * 0.4f, nh.position + Vector3.up * 1.6f, 0.3f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h is TerrainCollider || h is CharacterController) continue;
                if (h.GetComponentInParent<PlayerStats>() != null || h.GetComponentInParent<EnemyBrain>() != null) continue;
                return false;
            }
            return true;
        }

        public static void RenderTop(float[][] cands, string pngPath, float x0, float x1, float z0, float z1, float fw, float fd)
        {
            const float s = 24f;
            int W = Mathf.RoundToInt((x1 - x0) * s), H = Mathf.RoundToInt((z1 - z0) * s);
            var go = new GameObject("TmpTopCam");
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = (z1 - z0) / 2f;
            cam.transform.position = new Vector3((x0 + x1) / 2f, 80f, (z0 + z1) / 2f);
            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            cam.farClipPlane = 200f;
            var rt = new RenderTexture(W, H, 24);
            cam.targetTexture = rt;
            cam.aspect = (float)W / H;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);

            var tri = NavMesh.CalculateTriangulation();
            for (int i = 0; i < tri.indices.Length; i += 3)
                for (int e = 0; e < 3; e++)
                {
                    Vector3 a = tri.vertices[tri.indices[i + e]], b = tri.vertices[tri.indices[i + (e + 1) % 3]];
                    float len = Vector3.Distance(a, b);
                    int steps = Mathf.Max(2, Mathf.CeilToInt(len * s));
                    for (int k = 0; k <= steps; k++) Plot(tex, Vector3.Lerp(a, b, k / (float)steps), x0, z0, s, new Color(0f, 0.8f, 0.8f));
                }
            for (int g = -40; g <= 40; g += 5)
                for (float k = -40f; k <= 40f; k += 0.08f)
                {
                    Plot(tex, new Vector3(g, 0f, k), x0, z0, s, Color.white);
                    Plot(tex, new Vector3(k, 0f, g), x0, z0, s, Color.white);
                }
            if (cands != null)
                foreach (var c in cands)
                {
                    var rot = Quaternion.Euler(0f, c[2], 0f);
                    Vector3 fwd = rot * Vector3.forward, right = rot * Vector3.right, p = new Vector3(c[0], 0f, c[1]);
                    for (float u = -fw / 2f; u <= fw / 2f; u += 0.02f)
                    {
                        Plot(tex, p + right * u + fwd * (fd / 2f), x0, z0, s, Color.yellow); // Front
                        Plot(tex, p + right * u - fwd * (fd / 2f), x0, z0, s, Color.red);
                    }
                    for (float u = -fd / 2f; u <= fd / 2f; u += 0.02f)
                    {
                        Plot(tex, p + fwd * u + right * (fw / 2f), x0, z0, s, Color.red);
                        Plot(tex, p + fwd * u - right * (fw / 2f), x0, z0, s, Color.red);
                    }
                    Vector3 cc = p + fwd * CircleOffset + right * (c.Length > 3 ? c[3] : 0f);
                    for (float a = 0f; a < Mathf.PI * 2f; a += 0.004f)
                        Plot(tex, cc + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * CircleRadius, x0, z0, s, Color.magenta);
                }
            tex.Apply();
            File.WriteAllBytes(pngPath, tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(go);
            rt.Release();
            Object.DestroyImmediate(tex);
        }

        private static void Plot(Texture2D tex, Vector3 p, float x0, float z0, float s, Color c)
        {
            int x = (int)((p.x - x0) * s), y = (int)((p.z - z0) * s);
            if (x >= 0 && y >= 0 && x < tex.width && y < tex.height) tex.SetPixel(x, y, c);
        }

        public static float[][] StallCandidates()
        {
            var list = new float[Stalls.Length][];
            for (int i = 0; i < Stalls.Length; i++) list[i] = new[] { Stalls[i].X, Stalls[i].Z, Stalls[i].Yaw, Stalls[i].CircleX };
            return list;
        }

        // ---------------- Szene ----------------

        [MenuItem("BuddyTD/Händler/Szene einrichten (Stände, Manager, UI)")]
        public static void SetupSceneMenu()
        {
            Debug.Log(SetupScene());
        }

        // Legt Environment/Stadt/Haendler mit 5 Ständen an (bzw. aktualisiert sie), richtet MerchantManager und
        // Kartenauswahl-UI ein und meldet die Laternen bei der Belagerungs-Stimmung an. Speichert die Szene NICHT.
        public static string SetupScene()
        {
            var sb = new StringBuilder();
            var stadt = GameObject.Find("Environment/Stadt");
            if (stadt == null) return "MerchantSetup: Environment/Stadt nicht gefunden.";

            Transform group = stadt.transform.Find("Haendler");
            if (group == null)
            {
                group = new GameObject("Haendler").transform;
                group.SetParent(stadt.transform, false);
            }

            var merchants = new List<Merchant>();
            foreach (var def in Stalls) merchants.Add(BuildStall(group, def, sb));

            // Manager
            var managers = GameObject.Find("Managers");
            var mm = managers != null ? GetOrAdd<MerchantManager>(managers) : null;
            if (mm != null)
            {
                mm.Cards = LoadAllCards();
                mm.Portraits = new Sprite[MerchantInfo.Count];
                mm.Emblems = new Sprite[MerchantInfo.Count];
                string[] emblems = { "ability_knight_slash", "emblem_fire", "emblem_ice", "emblem_earth", "emblem_light" };
                for (int i = 0; i < MerchantInfo.Count; i++)
                {
                    mm.Portraits[i] = LoadSprite($"{IconFolder}/portrait_merchant_{MerchantInfo.Keys[i]}.png");
                    mm.Emblems[i] = LoadSprite($"{IconFolder}/{emblems[i]}.png");
                }
                int stats = System.Enum.GetValues(typeof(AbilityStat)).Length;
                mm.Badges = new Sprite[stats];
                for (int i = 0; i < stats; i++) mm.Badges[i] = LoadBadge((AbilityStat)i);
                EditorUtility.SetDirty(mm);
                sb.AppendLine($"MerchantManager: {mm.Cards.Count} Karten");
            }

            sb.AppendLine(SetupUI());

            // Laternen der Stände in die Belagerungs-Stimmung (nachts heller)
            var atmo = Object.FindFirstObjectByType<SiegeAtmosphere>();
            if (atmo != null)
            {
                for (int i = atmo.Lanterns.Count - 1; i >= 0; i--)
                    if (atmo.Lanterns[i] == null)
                    {
                        atmo.Lanterns.RemoveAt(i);
                        if (i < atmo.LanternBase.Count) atmo.LanternBase.RemoveAt(i);
                    }
                foreach (var m in merchants)
                {
                    if (m == null || m.Lampe == null) continue;
                    int idx = atmo.Lanterns.IndexOf(m.Lampe);
                    if (idx < 0)
                    {
                        atmo.Lanterns.Add(m.Lampe);
                        atmo.LanternBase.Add(m.Lampe.intensity);
                    }
                    else if (idx < atmo.LanternBase.Count) atmo.LanternBase[idx] = LampIntensity;
                }
                EditorUtility.SetDirty(atmo);
            }

            EditorSceneManager.MarkSceneDirty(stadt.scene);
            return sb.ToString();
        }

        private const float LampIntensity = 2.2f;

        private static Merchant BuildStall(Transform group, StallDef def, StringBuilder log)
        {
            string name = "Haendler_" + def.Kind;
            Transform root = group.Find(name);
            if (root == null)
            {
                root = new GameObject(name).transform;
                root.SetParent(group, false);
            }
            var terr = Terrain.activeTerrain;
            Vector3 pos = new Vector3(def.X, 0f, def.Z);
            pos.y = terr != null ? terr.SampleHeight(pos) + terr.transform.position.y : 0f;
            root.SetPositionAndRotation(pos, Quaternion.Euler(0f, def.Yaw, 0f));
            root.localScale = Vector3.one;

            var m = GetOrAdd<Merchant>(root.gameObject);
            m.Kind = def.Kind;
            m.DisplayName = MerchantInfo.Name(def.Kind);
            m.CaptureRadius = CircleRadius;

            string key = def.Kind.ToString();

            // Stand (Stand_<Typ>.fbx, Pivot Boden-Mitte, Front = lokales +Z) + Collider auf dem Stand-Objekt
            Transform stand = Child(root, "Stand");
            ReplaceModel(stand, $"{StandModelFolder}/Stand_{key}.fbx", "Modell", Vector3.zero, 1f);
            var box = GetOrAdd<BoxCollider>(stand.gameObject);
            box.center = StandColliderCenter;
            box.size = StandColliderSize;

            // Abdeckung (Stand_Abdeckung.fbx, gleiche Pivot-Konvention, nur geschlossen sichtbar)
            Transform cover = Child(root, "Abdeckung");
            ReplaceModel(cover, $"{StandModelFolder}/Stand_Abdeckung.fbx", "Modell", Vector3.zero, 1f);

            // Figur hinter der Theke, Blick +Z: Heroes2-Modell Merchant2_<Typ>.fbx (1,80 m, Skalierung 1) mit Requisite in der Hand,
            // sonst das alte Merchant_<Typ>.fbx (Skalierung 0,76) – siehe MerchantFigures
            Transform fig = Child(root, "Figur");
            fig.localPosition = new Vector3(0f, 0f, -0.4f);
            fig.localRotation = Quaternion.identity;
            Animator anim = MerchantFigures.Build(fig, def.Kind, log);
            IgnoreInNavMesh(fig.gameObject);

            // Kreis (Einnahme-Kreis + Leuchtsäule über dem Stand), Mitte = Merchant.Center
            Transform circle = Child(root, "Kreis");
            circle.localPosition = new Vector3(def.CircleX, 0.02f, CircleOffset);
            circle.localRotation = Quaternion.identity;
            if (circle.childCount == 0) BuildCircleFx(circle);
            // Leuchtsäule/Leuchte stehen über dem Stand, nicht über dem Kreis
            var pillar = circle.Find("Leuchtsaeule");
            if (pillar != null) pillar.localPosition = new Vector3(-def.CircleX, 0f, -CircleOffset);
            var beacon = circle.Find("Leuchte");
            if (beacon != null) beacon.localPosition = new Vector3(-def.CircleX * 0.5f, 3f, -CircleOffset * 0.5f);
            IgnoreInNavMesh(circle.gameObject);

            // Lampe (Punktlicht für die Nacht)
            Transform lampT = Child(root, "Lampe");
            lampT.localPosition = LampLocal;
            var lamp = lampT.GetComponent<Light>();
            if (lamp == null)
            {
                lamp = lampT.gameObject.AddComponent<Light>();
                lamp.type = LightType.Point;
                lamp.range = 7f;
                lamp.intensity = LampIntensity;
                lamp.color = new Color(1f, 0.72f, 0.4f);
                lamp.shadows = LightShadows.None;
            }

            m.Center = circle;
            m.Stand = stand.gameObject;
            m.Abdeckung = cover.gameObject;
            m.Figur = fig.gameObject;
            m.Kreis = circle.gameObject;
            m.Lampe = lamp;
            m.FigureAnimator = anim;

            // Editor-Zustand = geschlossen
            cover.gameObject.SetActive(true);
            fig.gameObject.SetActive(false);
            circle.gameObject.SetActive(false);
            EditorUtility.SetDirty(m);
            log.AppendLine($"{name}: ({def.X}, {def.Z}) Blick {def.Yaw}°, Kreis {circle.position.x:0.0}/{circle.position.z:0.0}");
            return m;
        }

        // Kopie der Schrein-Effekte (Säule, Ring, Funken), Kreis auf CircleRadius skaliert. Getönt wird zur Laufzeit.
        private static void BuildCircleFx(Transform circle)
        {
            Shrine src = null;
            foreach (var sh in Object.FindObjectsByType<Shrine>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (sh.AwakenedVisuals != null) { src = sh; break; }
            float scale = src != null && src.CaptureRadius > 0f ? CircleRadius / src.CaptureRadius : 1f;
            if (src != null)
            {
                foreach (Transform fx in src.AwakenedVisuals.transform)
                {
                    var copy = Object.Instantiate(fx.gameObject, circle);
                    copy.name = fx.name == "Pillar" ? "Leuchtsaeule" : (fx.name == "CaptureRing" ? "Ring" : (fx.name == "Motes" ? "Funken" : fx.name));
                    copy.transform.localRotation = fx.localRotation;
                    if (fx.name == "Pillar")
                    {
                        // Säule über dem Stand (hinter dem Kreis)
                        copy.transform.localPosition = new Vector3(0f, 0f, -CircleOffset);
                        copy.transform.localScale = fx.localScale;
                    }
                    else
                    {
                        copy.transform.localPosition = fx.localPosition;
                        copy.transform.localScale = fx.localScale * scale;
                    }
                }
            }
            // Bodenring (flache Scheibe, dezent) + Leuchte in Händlerfarbe
            var lightGo = new GameObject("Leuchte");
            lightGo.transform.SetParent(circle, false);
            lightGo.transform.localPosition = new Vector3(0f, 3f, -CircleOffset * 0.5f);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 9f;
            l.intensity = 3f;
            l.shadows = LightShadows.None;
        }

        // Ersetzt den Inhalt eines Hierarchie-Knotens durch eine Modell-Instanz (idempotent: gleiches Modell bleibt)
        private static GameObject ReplaceModel(Transform holder, string assetPath, string name, Vector3 localPos, float scale)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (asset == null)
            {
                Debug.LogWarning("MerchantSetup: Modell fehlt: " + assetPath);
                return holder.childCount > 0 ? holder.GetChild(0).gameObject : null;
            }
            GameObject existing = null;
            for (int i = holder.childCount - 1; i >= 0; i--)
            {
                var c = holder.GetChild(i).gameObject;
                if (existing == null && PrefabUtility.GetCorrespondingObjectFromSource(c) == asset) existing = c;
                else Object.DestroyImmediate(c);
            }
            var inst = existing != null ? existing : (GameObject)PrefabUtility.InstantiatePrefab(asset, holder);
            inst.name = name;
            inst.transform.localPosition = localPos;
            inst.transform.localRotation = Quaternion.identity;
            inst.transform.localScale = Vector3.one * scale;
            return inst;
        }

        // Kleiner Controller für die Händler-Figur: Idle (StarterAssets) ↔ Winken (Trigger „Winken") / Jubel (Trigger „Jubel")
        public static RuntimeAnimatorController EnsureFigureController()
        {
            var ctrl = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(FigureControllerPath);
            if (ctrl != null) return ctrl;
            string clipSrc = $"{FigureModelFolder}/Merchant_Waffen.fbx";
            AnimationClip beckon = null, cheer = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(clipSrc))
            {
                var clip = o as AnimationClip;
                if (clip == null || clip.name.StartsWith("__")) continue;
                if (clip.name == "Merchant_Beckon") beckon = clip;
                if (clip.name == "Merchant_Cheer") cheer = clip;
            }
            AnimationClip idle = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath("Assets/StarterAssets/ThirdPersonController/Character/Animations/Stand--Idle.anim.fbx"))
                if (o is AnimationClip && !o.name.StartsWith("__")) idle = (AnimationClip)o;

            ctrl = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(FigureControllerPath);
            ctrl.AddParameter("Winken", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Jubel", AnimatorControllerParameterType.Trigger);
            var sm = ctrl.layers[0].stateMachine;
            var sIdle = sm.AddState("Idle");
            sIdle.motion = idle;
            var sBeckon = sm.AddState("Winken");
            sBeckon.motion = beckon;
            var sCheer = sm.AddState("Jubel");
            sCheer.motion = cheer;
            sm.defaultState = sIdle;

            var t1 = sm.AddAnyStateTransition(sBeckon);
            t1.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0f, "Winken");
            t1.duration = 0.2f;
            t1.canTransitionToSelf = false;
            var t2 = sm.AddAnyStateTransition(sCheer);
            t2.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0f, "Jubel");
            t2.duration = 0.15f;
            t2.canTransitionToSelf = false;
            // Winken zweimal, Jubel einmal, dann zurück ins Idle
            var b = sBeckon.AddTransition(sIdle);
            b.hasExitTime = true;
            b.exitTime = 2f;
            b.duration = 0.3f;
            var c2 = sCheer.AddTransition(sIdle);
            c2.hasExitTime = true;
            c2.exitTime = 0.95f;
            c2.duration = 0.3f;
            AssetDatabase.SaveAssets();
            return ctrl;
        }

        private static void IgnoreInNavMesh(GameObject go)
        {
            var mod = go.GetComponent<Unity.AI.Navigation.NavMeshModifier>();
            if (mod == null) mod = go.AddComponent<Unity.AI.Navigation.NavMeshModifier>();
            mod.ignoreFromBuild = true;
        }

        private static void Box(Transform parent, string name, Vector3 pos, Vector3 size, Vector3 euler, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = size;
            if (mat != null) go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private static Transform Child(Transform parent, string name)
        {
            Transform t = parent.Find(name);
            if (t == null)
            {
                t = new GameObject(name).transform;
                t.SetParent(parent, false);
            }
            return t;
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        // ---------------- UI ----------------

        // Kopie von Canvas/UpgradePanel als Canvas/MerchantPanel (Overlay, Banderole, Container) + Untertitel + Porträt
        public static string SetupUI()
        {
            var canvas = GameObject.Find("Canvas");
            if (canvas == null) return "UI: Canvas fehlt";
            Transform panel = canvas.transform.Find("MerchantPanel");
            Transform up = canvas.transform.Find("UpgradePanel");
            if (panel == null)
            {
                if (up == null) return "UI: UpgradePanel fehlt";
                panel = Object.Instantiate(up.gameObject, canvas.transform).transform;
                panel.name = "MerchantPanel";
                panel.SetSiblingIndex(up.GetSiblingIndex() + 1);
                var old = panel.GetComponent<UpgradeScreenUI>();
                if (old != null) Object.DestroyImmediate(old);
            }
            var container = panel.Find("Container");
            if (container != null)
                for (int i = container.childCount - 1; i >= 0; i--) Object.DestroyImmediate(container.GetChild(i).gameObject);

            var ui = GetOrAdd<MerchantScreenUI>(panel.gameObject);
            ui.Panel = panel.gameObject;
            ui.CardsContainer = container;
            ui.CardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/BuffCard.prefab");
            var ribbon = panel.Find("Ribbon");
            ui.Ribbon = ribbon != null ? ribbon.GetComponent<Image>() : null;
            ui.TitleText = ribbon != null ? ribbon.Find("Text")?.GetComponent<TextMeshProUGUI>() : null;
            if (ui.TitleText != null) ui.TitleText.text = "Händler";

            // Untertitel unter der Banderole
            Transform sub = panel.Find("Subtitle");
            if (sub == null)
            {
                var go = new GameObject("Subtitle", typeof(RectTransform), typeof(TextMeshProUGUI));
                sub = go.transform;
                sub.SetParent(panel, false);
            }
            var subRt = (RectTransform)sub;
            subRt.anchorMin = subRt.anchorMax = new Vector2(0.5f, 0.5f);
            subRt.sizeDelta = new Vector2(1200f, 50f);
            subRt.anchoredPosition = new Vector2(0f, 222f);
            var subText = sub.GetComponent<TextMeshProUGUI>();
            subText.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Game/UI/Fonts/AlegreyaSans-Bold SDF.asset");
            subText.fontSize = 30f;
            subText.alignment = TextAlignmentOptions.Center;
            subText.color = new Color(1f, 0.93f, 0.78f);
            subText.raycastTarget = false;
            subText.text = "Wähle eine Verbesserung";
            ui.SubtitleText = subText;

            // Porträt links an der Banderole (portrait_merchant_*, optional)
            Transform portrait = ribbon != null ? ribbon.Find("Portrait") : null;
            if (portrait == null && ribbon != null)
            {
                var go = new GameObject("Portrait", typeof(RectTransform), typeof(Image));
                portrait = go.transform;
                portrait.SetParent(ribbon, false);
            }
            if (portrait != null)
            {
                var prt = (RectTransform)portrait;
                prt.anchorMin = prt.anchorMax = new Vector2(0f, 0.5f);
                prt.sizeDelta = new Vector2(150f, 150f);
                prt.anchoredPosition = new Vector2(-40f, 8f);
                var img = portrait.GetComponent<Image>();
                img.preserveAspect = true;
                img.raycastTarget = false;
                ui.Portrait = img;
                portrait.gameObject.SetActive(false);
            }
            panel.gameObject.SetActive(true); // blendet sich in Start aus (wie UpgradePanel)
            EditorUtility.SetDirty(ui);
            return "UI: Canvas/MerchantPanel eingerichtet";
        }

        // ---------------- NavMesh ----------------

        // NavMeshSurface „Environment" neu backen und in Assets/NavMesh-Environment.asset kopieren (CopySerialized)
        public static string RebakeNavMesh()
        {
            Unity.AI.Navigation.NavMeshSurface surface = null;
            foreach (var srf in Object.FindObjectsByType<Unity.AI.Navigation.NavMeshSurface>(FindObjectsSortMode.None))
                if (srf.name == "Environment") surface = srf;
            if (surface == null) return "NavMeshSurface 'Environment' fehlt";
            const string assetPath = "Assets/NavMesh-Environment.asset";
            var asset = AssetDatabase.LoadAssetAtPath<NavMeshData>(assetPath);
            surface.BuildNavMesh();
            var built = surface.navMeshData;
            if (asset != null && built != null && built != asset)
            {
                EditorUtility.CopySerialized(built, asset);
                surface.RemoveData();
                surface.navMeshData = asset;
                surface.AddData();
                EditorUtility.SetDirty(asset);
                EditorUtility.SetDirty(surface);
                AssetDatabase.SaveAssets();
            }
            return "NavMesh neu gebacken → " + AssetDatabase.GetAssetPath(surface.navMeshData);
        }

        // Weglängen Portal → Nexus (NavMesh)
        public static string PortalPaths()
        {
            var sb = new StringBuilder();
            Vector3 nexus = Nexus.Instance != null ? Nexus.Instance.transform.position : new Vector3(26f, 0f, 0f);
            var nx = Object.FindFirstObjectByType<Nexus>();
            if (nx != null) nexus = nx.transform.position;
            var portals = new Dictionary<string, Vector3>
            {
                { "West", new Vector3(-44f, 0f, 0f) }, { "Nord", new Vector3(0f, 0f, 44f) }, { "Sued", new Vector3(0f, 0f, -44f) }
            };
            foreach (var kv in portals)
            {
                var path = new NavMeshPath();
                NavMesh.SamplePosition(kv.Value, out NavMeshHit a, 5f, NavMesh.AllAreas);
                NavMesh.SamplePosition(nexus, out NavMeshHit b, 6f, NavMesh.AllAreas);
                NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path);
                float len = 0f;
                for (int i = 1; i < path.corners.Length; i++) len += Vector3.Distance(path.corners[i - 1], path.corners[i]);
                sb.Append($"{kv.Key}: {path.status} {len:0.0} m   ");
            }
            return sb.ToString();
        }
    }
}
