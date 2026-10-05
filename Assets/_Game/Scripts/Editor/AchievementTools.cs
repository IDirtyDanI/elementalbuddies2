using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ElementalBuddies.EditorTools
{
    // Menü BuddyTD → Erfolge: Datenbank (Resources/AchievementDatabase.asset) anlegen/ergänzen, Stand zurücksetzen,
    // alles freischalten, Stand ins Log schreiben. Bestehende Einträge (z. B. im Inspector geänderte Schwellen) bleiben erhalten;
    // Icons werden nur gesetzt, wenn sie noch leer sind. Außerdem: Popup-Prefab (Resources/AchievementPopup) für die Spielszene.
    public static class AchievementTools
    {
        private const string AssetPath = "Assets/Resources/AchievementDatabase.asset";
        private const string PopupPath = "Assets/Resources/" + AchievementPopupUI.ResourcePath + ".prefab";
        private const string IconDir = "Assets/_Game/UI/Icons/";
        private const string SpriteDir = "Assets/_Game/UI/Sprites/";
        private const string FontDir = "Assets/_Game/UI/Fonts/";

        // Icons der Erfolge (Id → Datei in UI/Icons)
        private static readonly string[,] AchievementIcons =
        {
            { "defender", "ach_survivor" },
            { "bulwark", "ach_survivor" },
            { "unshakable", "ach_survivor" },
            { "twin_power", "portrait_fusion_blitz" },
            { "master_fire", "portrait4_fire" },
            { "master_ice", "portrait4_ice" },
            { "master_earth", "portrait4_earth" },
            { "master_light", "portrait4_light" },
            { "fusionist", "portrait_fusion_magma" },
            { "primal_force", "portrait_super_vulkan" },
            { "bonebreaker", "ach_boss" },
            { "boss_slayer", "ach_bosses_all" },
            { "soul_collector", "ach_soulcollector" },
        };

        // Neue PNGs als Sprite (2D and UI) importieren, falls noch nicht geschehen
        private static readonly string[] SpriteIcons =
        {
            "achievement_frame", "achievement_frame_locked", "icon_lock", "icon_trophy",
            "ach_boss", "ach_bosses_all", "ach_soulcollector", "ach_survivor",
        };

        [MenuItem("BuddyTD/Erfolge/Datenbank anlegen-aktualisieren")]
        public static void CreateOrUpdateDatabase()
        {
            var db = AssetDatabase.LoadAssetAtPath<AchievementDatabaseSO>(AssetPath);
            if (db == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
                db = ScriptableObject.CreateInstance<AchievementDatabaseSO>();
                AssetDatabase.CreateAsset(db, AssetPath);
            }

            int added = 0;
            added += Add(db, "defender", "Verteidiger", AchievementCondition.ReachWave, 6, 0, -1, UnlockId.ChampionKnight);
            added += Add(db, "bulwark", "Bollwerk", AchievementCondition.ReachWave, 10, 0, -1, UnlockId.ChampionArcher);
            added += Add(db, "twin_power", "Zwillingskraft", AchievementCondition.BaseBuddiesAtLevel, 2, 2, -1, UnlockId.Fusion2);
            added += Add(db, "master_fire", "Feuermeister", AchievementCondition.BaseBuddiesAtLevel, 2, 3, 0, UnlockId.Stage4Fire);
            added += Add(db, "master_ice", "Eismeister", AchievementCondition.BaseBuddiesAtLevel, 2, 3, 1, UnlockId.Stage4Ice);
            added += Add(db, "master_earth", "Erdmeister", AchievementCondition.BaseBuddiesAtLevel, 2, 3, 2, UnlockId.Stage4Earth);
            added += Add(db, "master_light", "Lichtmeister", AchievementCondition.BaseBuddiesAtLevel, 2, 3, 3, UnlockId.Stage4Light);
            added += Add(db, "fusionist", "Fusionist", AchievementCondition.FusionBuddiesOnField, 2, 0, -1, UnlockId.TriFusion);
            added += Add(db, "unshakable", "Unerschütterlich", AchievementCondition.ReachWave, 25, 0, -1, UnlockId.TriFusion);
            added += Add(db, "bonebreaker", "Knochenbrecher", AchievementCondition.DefeatBoss, 1, 0, -1, UnlockId.None);
            added += Add(db, "boss_slayer", "Bosstöter", AchievementCondition.DefeatBossKinds, 3, 0, -1, UnlockId.None);
            added += Add(db, "soul_collector", "Seelensammler", AchievementCondition.ShardsCollected, 1000, 0, -1, UnlockId.None);
            added += Add(db, "primal_force", "Urgewalt", AchievementCondition.BuildSuper, 1, 0, -1, UnlockId.None);

            AddUnlock(db, UnlockId.ChampionKnight, "Schwertkämpfer");
            AddUnlock(db, UnlockId.ChampionArcher, "Bogenschütze");
            AddUnlock(db, UnlockId.Fusion2, "Zweier-Fusionen");
            AddUnlock(db, UnlockId.Stage4Fire, "Feuer Stufe 4 – Flammenkaiser");
            AddUnlock(db, UnlockId.Stage4Ice, "Eis Stufe 4 – Frostkönig");
            AddUnlock(db, UnlockId.Stage4Earth, "Erde Stufe 4 – Bergkönig");
            AddUnlock(db, UnlockId.Stage4Light, "Licht Stufe 4 – Sonnenerzengel");
            AddUnlock(db, UnlockId.TriFusion, "Super-Elementare");

            AssignIcons(db);

            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
            Debug.Log($"AchievementTools: {AssetPath} – {db.Achievements.Count} Erfolge ({added} neu), {db.Unlocks.Count} Freischaltungen.");
            Selection.activeObject = db;
        }

        private static int Add(AchievementDatabaseSO db, string id, string title, AchievementCondition condition, int threshold, int level, int element, UnlockId unlock)
        {
            if (db.Find(id) != null) return 0;
            db.Achievements.Add(new AchievementDefinition
            {
                Id = id,
                Title = title,
                Condition = condition,
                Threshold = threshold,
                Level = level,
                Element = element,
                Unlock = unlock,
            });
            return 1;
        }

        private static void AddUnlock(AchievementDatabaseSO db, UnlockId id, string name)
        {
            foreach (var u in db.Unlocks)
                if (u != null && u.Id == id) return;
            db.Unlocks.Add(new UnlockInfo { Id = id, Name = name });
        }

        // ---------------- Icons ----------------

        private static void EnsureSpriteImport()
        {
            foreach (var n in SpriteIcons)
            {
                var imp = AssetImporter.GetAtPath(IconDir + n + ".png") as TextureImporter;
                if (imp == null || imp.textureType == TextureImporterType.Sprite) continue;
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.mipmapEnabled = false;
                imp.alphaIsTransparency = true;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.maxTextureSize = 512;
                imp.SaveAndReimport();
            }
        }

        private static Sprite Icon(string name)
        {
            var s = AssetDatabase.LoadAssetAtPath<Sprite>(IconDir + name + ".png");
            if (s == null) Debug.LogWarning("AchievementTools: Icon fehlt: " + IconDir + name + ".png");
            return s;
        }

        private static Sprite UISprite(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(SpriteDir + name + ".png");

        // Nur leere Felder füllen – im Inspector gesetzte Icons bleiben
        private static void AssignIcons(AchievementDatabaseSO db)
        {
            EnsureSpriteImport();
            for (int i = 0; i < AchievementIcons.GetLength(0); i++)
            {
                var a = db.Find(AchievementIcons[i, 0]);
                if (a != null && a.Icon == null) a.Icon = Icon(AchievementIcons[i, 1]);
            }
            SetUnlockIcon(db, UnlockId.ChampionKnight, "portrait_champion_knight");
            SetUnlockIcon(db, UnlockId.ChampionArcher, "portrait_champion_archer");
            SetUnlockIcon(db, UnlockId.Fusion2, "portrait_fusion_blitz");
            SetUnlockIcon(db, UnlockId.Stage4Fire, "portrait4_fire");
            SetUnlockIcon(db, UnlockId.Stage4Ice, "portrait4_ice");
            SetUnlockIcon(db, UnlockId.Stage4Earth, "portrait4_earth");
            SetUnlockIcon(db, UnlockId.Stage4Light, "portrait4_light");
            SetUnlockIcon(db, UnlockId.TriFusion, "portrait_super_vulkan");
            if (db.MedalFrame == null) db.MedalFrame = Icon("achievement_frame");
            if (db.MedalFrameLocked == null) db.MedalFrameLocked = Icon("achievement_frame_locked");
            if (db.LockIcon == null) db.LockIcon = Icon("icon_lock");
            if (db.TrophyIcon == null) db.TrophyIcon = Icon("icon_trophy");
        }

        private static void SetUnlockIcon(AchievementDatabaseSO db, UnlockId id, string icon)
        {
            foreach (var u in db.Unlocks)
                if (u != null && u.Id == id && u.Icon == null) u.Icon = Icon(icon);
        }

        // ---------------- Popup-Prefab ----------------

        // Baut Resources/AchievementPopup.prefab (eigenes Overlay-Canvas, Pergament-&-Holz-Stil); ersetzt ein vorhandenes.
        [MenuItem("BuddyTD/Erfolge/Popup-Prefab bauen")]
        public static void BuildPopupPrefab()
        {
            EnsureSpriteImport();
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            var fHead = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontDir + "MedievalSharp SDF.asset");
            var fBold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontDir + "AlegreyaSans-Bold SDF.asset");
            var fReg = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontDir + "AlegreyaSans-Regular SDF.asset");
            var ink = new Color(0.239f, 0.149f, 0.078f);
            var inkLight = new Color(0.4f, 0.26f, 0.13f);

            var root = new GameObject("AchievementPopup", typeof(RectTransform));
            try
            {
                var canvas = root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 80; // über HUD, Pause- und Upgrade-Overlays
                var scaler = root.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(2400f, 1350f);
                scaler.matchWidthOrHeight = 0.5f;
                var ui = root.AddComponent<AchievementPopupUI>();

                var panel = Rect("Panel", root.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -222f), new Vector2(960f, 196f));
                ui.Panel = panel;
                ui.Group = panel.gameObject.AddComponent<CanvasGroup>();
                ui.Group.alpha = 0f;
                ui.Group.blocksRaycasts = false;
                ui.Group.interactable = false;
                Img(panel, UISprite("frame_wood_parchment"), Color.white, true);

                var medal = Rect("Medal", panel, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(110f, 0f), new Vector2(156f, 156f));
                var disc = Rect("Disc", medal, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(106f, 106f));
                Img(disc, AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Game/Menu/menu_disc.png"), new Color(0.2f, 0.13f, 0.08f), false);
                var icon = Rect("Icon", medal, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(108f, 108f));
                ui.MedalIcon = Img(icon, null, Color.white, false);
                ui.MedalIcon.preserveAspect = true;
                var frame = Rect("Frame", medal, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                ui.MedalFrame = Img(frame, Icon("achievement_frame"), Color.white, false);
                ui.MedalFrame.preserveAspect = true;

                ui.Header = Text(panel, "Header", "Erfolg freigeschaltet!", fBold, 27f, new Color(0.6f, 0.36f, 0.04f), TextAlignmentOptions.TopLeft);
                Place(ui.Header.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(208f, -30f), new Vector2(580f, 34f));
                ui.Header.characterSpacing = 4f;
                ui.Title = Text(panel, "Title", "Erfolg", fHead, 52f, ink, TextAlignmentOptions.TopLeft);
                Place(ui.Title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(206f, -62f), new Vector2(590f, 62f));
                ui.Title.enableAutoSizing = true;
                ui.Title.fontSizeMin = 28f;
                ui.Title.fontSizeMax = 52f;
                ui.Title.textWrappingMode = TextWrappingModes.NoWrap;
                ui.Reward = Text(panel, "Reward", "Ab dem nächsten Spiel: …", fReg, 28f, inkLight, TextAlignmentOptions.TopLeft);
                Place(ui.Reward.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(208f, -128f), new Vector2(590f, 38f));
                ui.Reward.enableAutoSizing = true;
                ui.Reward.fontSizeMin = 16f;
                ui.Reward.fontSizeMax = 28f;
                ui.Reward.textWrappingMode = TextWrappingModes.NoWrap;

                var rIcon = Rect("RewardIcon", panel, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-94f, 0f), new Vector2(112f, 112f));
                ui.RewardIcon = Img(rIcon, null, Color.white, false);
                ui.RewardIcon.preserveAspect = true;

                PrefabUtility.SaveAsPrefabAsset(root, PopupPath);
                Debug.Log("AchievementTools: " + PopupPath + " gebaut.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            Place(rt, aMin, aMax, pivot, pos, size);
            return rt;
        }

        private static void Place(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        private static Image Img(RectTransform rt, Sprite sprite, Color color, bool sliced)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            img.raycastTarget = false; // Popup ist nicht klickbar
            return img;
        }

        private static TextMeshProUGUI Text(Transform parent, string name, string text, TMP_FontAsset font, float size, Color color, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            return t;
        }

        [MenuItem("BuddyTD/Erfolge/Alle zurücksetzen")]
        public static void ResetAll()
        {
            Progression.ResetAll();
            Debug.Log("AchievementTools: Alle Erfolge zurückgesetzt (Bestwelle bleibt).\n" + Progression.DescribeState());
        }

        [MenuItem("BuddyTD/Erfolge/Alle freischalten")]
        public static void UnlockAll()
        {
            Progression.UnlockAllPersistent();
            Debug.Log("AchievementTools: Alle Erfolge freigeschaltet (wirkt ab dem nächsten Spiel).\n" + Progression.DescribeState());
        }

        [MenuItem("BuddyTD/Erfolge/Stand ausgeben")]
        public static void LogState()
        {
            Debug.Log(Progression.DescribeState());
        }
    }
}
