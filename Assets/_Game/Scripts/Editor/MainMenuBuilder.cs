using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

namespace ElementalBuddies.EditorTools
{
    // Baut das Hauptmenü (Szene Assets/Scenes/MainMenu.unity) inkl. Champion-Assets, Vorschau-Prefabs und Build Settings.
    // Idempotent: die Szene wird jedes Mal komplett neu aufgebaut. Gearbeitet wird additiv, die aktive Szene bleibt unverändert.
    // Champion-Assets und Vorschau-Prefabs werden nur angelegt, wenn sie fehlen (spätere Anpassungen bleiben erhalten).
    public static class MainMenuBuilder
    {
        public const string ScenePath = "Assets/Scenes/MainMenu.unity";
        private const string GameScenePath = "Assets/test.unity";
        private const string ChampionDir = "Assets/ScriptableObjects/Champions/";
        private const string PreviewDir = "Assets/_Game/Prefabs/Champions/";
        private const string MenuDir = "Assets/_Game/Menu/";
        private const string SpriteDir = "Assets/_Game/UI/Sprites/";
        private const string IconDir = "Assets/_Game/UI/Icons/";
        private const string FontDir = "Assets/_Game/UI/Fonts/";
        private const string PlaceholderModel = "Assets/3D Models/player/Character_output (4).fbx";
        private const string PlaceholderController = "Assets/_Game/Animations/PlayerVisual.controller";

        // Farben des Pergament-&-Holz-Stils
        private static readonly Color Ink = new Color(0.239f, 0.149f, 0.078f);
        private static readonly Color InkLight = new Color(0.478f, 0.31f, 0.165f);
        private static readonly Color Cream = new Color(0.937f, 0.878f, 0.741f);
        private static readonly Color LineCol = new Color(0.43f, 0.29f, 0.16f, 0.55f);
        private static readonly Color BarFill = new Color(0.85f, 0.62f, 0.28f);

        private static TMP_FontAsset _fHead, _fBold, _fReg;
        private static Material _mHeadOutline, _mBoldOutline;

        // ---------------- Menü ----------------

        [MenuItem("BuddyTD/Hauptmenü/Alles einrichten (Assets + Szene + Build Settings)")]
        public static void SetupAll()
        {
            BuildScene();
            SetupBuildSettings();
        }

        [MenuItem("BuddyTD/Hauptmenü/Szene neu bauen")]
        public static void BuildSceneMenu() { BuildScene(); }

        [MenuItem("BuddyTD/Hauptmenü/Build Settings setzen")]
        public static void BuildSettingsMenu() { SetupBuildSettings(); }

        [MenuItem("BuddyTD/Hauptmenü/Erfolge-UI in bestehende Szene einbauen")]
        public static void AddAchievementUIMenu() { AddAchievementUIToScene(); }

        [MenuItem("BuddyTD/Hauptmenü/Schwierigkeitsauswahl in bestehende Szene einbauen")]
        public static void AddDifficultyUIMenu() { AddDifficultyUIToScene(); }

        [MenuItem("BuddyTD/Hauptmenü/Erfolge: Stufen-Marker in bestehende Szene einbauen")]
        public static void AddAchievementTierUIMenu() { AddAchievementTierUIToScene(); }

        // ---------------- Build Settings ----------------

        // MainMenu = Index 0, Spielszene danach. SampleScene (Unity-Vorlage, nicht referenziert) fliegt raus.
        public static void SetupBuildSettings()
        {
            var list = new List<EditorBuildSettingsScene>();
            list.Add(new EditorBuildSettingsScene(ScenePath, true));
            list.Add(new EditorBuildSettingsScene(GameScenePath, true));
            foreach (var s in EditorBuildSettings.scenes)
            {
                if (s.path == ScenePath || s.path == GameScenePath || s.path.EndsWith("SampleScene.unity")) continue;
                list.Add(s);
            }
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log("MainMenuBuilder: Build Settings = " + string.Join(", ", list.ConvertAll(s => s.path).ToArray()));
        }

        // ---------------- Champion-Assets ----------------

        public static void CreateChampionAssets(bool overwrite)
        {
            EnsureFolder(ChampionDir.TrimEnd('/'));
            EnsureFolder(PreviewDir.TrimEnd('/'));

            var mage = EnsurePreviewPrefab(ChampionClass.Mage);
            var knight = EnsurePreviewPrefab(ChampionClass.Knight);
            var archer = EnsurePreviewPrefab(ChampionClass.Archer);

            Sprite arcane = Icon("ability_arcane"), blink = Icon("ability_blink");
            Sprite fire = Icon("emblem_fire"), ice = Icon("emblem_ice"), earth = Icon("emblem_earth"), light = Icon("emblem_light");

            WriteChampion(ChampionClass.Mage, overwrite, "Champion_Mage", d =>
            {
                d.DisplayName = "Magier";
                d.Tagline = "Meister der arkanen Künste";
                d.Description = "Ein Gelehrter, der Gegner aus sicherer Entfernung mit arkanen Geschossen zermürbt. " +
                                "Mit Blink entkommt er jeder Umzingelung – und an den Schreinen erweckt er die Macht der vier Elemente.";
                d.PreviewPrefab = mage;
                d.AccentColor = new Color(0.62f, 0.4f, 0.9f);
                d.Abilities = new List<ChampionDefinitionSO.AbilityInfo>
                {
                    Ab("LMB", "Arkanball", "Schleudert eine arkane Kugel in Zielrichtung, die beim Aufprall Schaden verursacht.", arcane, -1),
                    Ab("RMB", "Blink", "Teleportiert dich bis zu 8 m Richtung Mauszeiger, kurz unverwundbar.", blink, -1),
                    Ab("R", "Flammenwelle", "Feuerkegel nach vorne (7 m): hoher Schaden und Brand.", fire, 0),
                    Ab("F", "Frostnova", "Eisexplosion um dich (6 m): Schaden, Gegner frieren 2,5 s ein.", ice, 1),
                    Ab("C", "Steinwall", "Errichtet eine 6 m breite Steinmauer, die Gegner 7 s lang aufhält.", earth, 2),
                    Ab("V", "Heiliger Kreis", "Heilt dich, Buddies und Nexus in der Nähe und blendet Gegner.", light, 3),
                };
            });

            WriteChampion(ChampionClass.Knight, overwrite, "Champion_Knight", d =>
            {
                d.DisplayName = "Schwertkämpfer";
                d.Tagline = "Unerschütterlicher Schild der Stadt";
                d.Description = "Ein gepanzerter Ritter, der sich mitten ins Getümmel wirft. Mit Schwert und Schild hält er " +
                                "die Gegnerwellen auf, zieht ihren Zorn auf sich und schützt so Nexus und Buddies.";
                d.PreviewPrefab = knight;
                d.AccentColor = new Color(0.36f, 0.55f, 0.78f);
                d.Abilities = new List<ChampionDefinitionSO.AbilityInfo>
                {
                    Ab("LMB", "Schwerthieb", "Weiter Hieb vor dir, 3er-Kombo – der dritte Schlag trifft härter und stößt zurück.", arcane, -1),
                    Ab("RMB", "Schildblock", "Halten: blockt frontalen Schaden gegen Mana. Langsamer, kein Angriff.", blink, -1),
                    Ab("R", "Flammenwirbel", "Drehschlag um dich herum, setzt alle Gegner in der Nähe in Brand.", fire, 0),
                    Ab("F", "Frostschlag", "Schockwelle nach vorne, die getroffene Gegner einfriert.", ice, 1),
                    Ab("C", "Erdbeben", "Sprung mit Stampfer: betäubt Gegner und wirft sie zurück.", earth, 2),
                    Ab("V", "Lichtschwur", "Dein Schild leuchtet: verspottet Gegner, heilt dich und Buddies in der Nähe.", light, 3),
                };
            });

            WriteChampion(ChampionClass.Archer, overwrite, "Champion_Archer", d =>
            {
                d.DisplayName = "Bogenschütze";
                d.Tagline = "Präziser Schütze aus den Wäldern";
                d.Description = "Ein Waldläufer, der aus großer Entfernung präzise Pfeile verschießt. Mit seiner Rolle " +
                                "bleibt er stets in Bewegung, und tückische Fallen halten die Gegner auf Abstand.";
                d.PreviewPrefab = archer;
                d.AccentColor = new Color(0.3f, 0.62f, 0.32f);
                d.Abilities = new List<ChampionDefinitionSO.AbilityInfo>
                {
                    Ab("LMB", "Pfeilschuss", "Schneller Pfeil mit großer Reichweite.", arcane, -1),
                    Ab("RMB", "Rolle", "Hechtrolle (~4 m), kurz unverwundbar. 2 Aufladungen.", blink, -1),
                    Ab("R", "Feuerpfeil-Regen", "Brennende Pfeile regnen auf das Zielgebiet und setzen Gegner in Brand.", fire, 0),
                    Ab("F", "Frostpfeil", "Durchschlagender Pfeil, der alle getroffenen Gegner einfriert.", ice, 1),
                    Ab("C", "Dornenfalle", "Legt eine Falle, die Gegner festhält und verletzt.", earth, 2),
                    Ab("V", "Lichtpfeil", "Lichtstrahl, der alle Gegner in einer Linie durchbohrt und blendet.", light, 3),
                };
            });

            AssetDatabase.SaveAssets();
        }

        private static ChampionDefinitionSO.AbilityInfo Ab(string key, string name, string desc, Sprite icon, int element)
        {
            return new ChampionDefinitionSO.AbilityInfo { Key = key, Name = name, Description = desc, Icon = icon, Element = element };
        }

        private static void WriteChampion(ChampionClass c, bool overwrite, string file, System.Action<ChampionDefinitionSO> fill)
        {
            string path = ChampionDir + file + ".asset";
            var def = AssetDatabase.LoadAssetAtPath<ChampionDefinitionSO>(path);
            if (def != null && !overwrite) return;
            bool created = def == null;
            if (created) def = ScriptableObject.CreateInstance<ChampionDefinitionSO>();
            def.Class = c;
            fill(def);
            if (created) AssetDatabase.CreateAsset(def, path);
            else EditorUtility.SetDirty(def);
        }

        public static ChampionDefinitionSO[] LoadChampions()
        {
            return new[]
            {
                AssetDatabase.LoadAssetAtPath<ChampionDefinitionSO>(ChampionDir + "Champion_Mage.asset"),
                AssetDatabase.LoadAssetAtPath<ChampionDefinitionSO>(ChampionDir + "Champion_Knight.asset"),
                AssetDatabase.LoadAssetAtPath<ChampionDefinitionSO>(ChampionDir + "Champion_Archer.asset"),
            };
        }

        // Vorschau-Prefab: leere Wurzel (Füße = Pivot) mit Kind "Model" – später wird nur "Model" gegen das echte Modell getauscht.
        private static GameObject EnsurePreviewPrefab(ChampionClass c)
        {
            string path = PreviewDir + "Preview_" + c + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var root = new GameObject("Preview_" + c);
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(PlaceholderModel);
            if (fbx != null)
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
                model.name = "Model";
                model.transform.SetParent(root.transform, false);
                model.transform.localScale = Vector3.one * 0.64f; // wie Player/Visual
                var anim = model.GetComponent<Animator>();
                if (anim == null) anim = model.AddComponent<Animator>();
                anim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PlaceholderController);
                anim.applyRootMotion = false;
            }
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ---------------- Szene ----------------

        public static void BuildScene()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("MainMenuBuilder: nur im Edit-Modus."); return; }
            EnsureFolder("Assets/Scenes");
            EnsureFolder(MenuDir.TrimEnd('/'));
            ImportMenuSprites();
            LoadFonts();

            Scene prevActive = SceneManager.GetActiveScene();
            Scene menu = SceneManager.GetSceneByPath(ScenePath);
            bool wasLoaded = menu.IsValid() && menu.isLoaded;
            if (!wasLoaded)
            {
                if (System.IO.File.Exists(ScenePath)) menu = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                else
                {
                    menu = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                    EditorSceneManager.SaveScene(menu, ScenePath);
                }
            }

            SceneManager.SetActiveScene(menu);
            try
            {
                foreach (var go in menu.GetRootGameObjects()) Object.DestroyImmediate(go);
                // Temporäre Objekte der Prefab-Erzeugung landen so in der Menü-Szene, nicht in der Spielszene
                CreateChampionAssets(false);
                BuildContents();
                EditorSceneManager.MarkSceneDirty(menu);
                EditorSceneManager.SaveScene(menu, ScenePath);
            }
            finally
            {
                if (prevActive.IsValid() && prevActive.isLoaded && prevActive != menu) SceneManager.SetActiveScene(prevActive);
                if (!wasLoaded && prevActive != menu) EditorSceneManager.CloseScene(menu, true);
            }
            Debug.Log("MainMenuBuilder: " + ScenePath + " gebaut.");
        }

        private static void BuildContents()
        {
            var defs = LoadChampions();

            // --- Licht & Atmosphäre (Dämmerung, Werte angelehnt an SiegeAtmosphere "Dämmerung") ---
            var sky = EnsureSkybox();
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.42f, 0.34f, 0.52f);
            RenderSettings.ambientEquatorColor = new Color(0.5f, 0.35f, 0.36f);
            RenderSettings.ambientGroundColor = new Color(0.16f, 0.12f, 0.15f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.46f, 0.31f, 0.38f);
            RenderSettings.fogDensity = 0.03f;

            var world = new GameObject("World").transform;

            var sun = NewLight("Sun_Rim", world, LightType.Directional, new Color(1f, 0.56f, 0.36f), 1.6f);
            sun.transform.rotation = Quaternion.Euler(7f, 235f, 0f); // tief von hinten-rechts → Randlicht
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            RenderSettings.sun = sun;
            var fill = NewLight("Fill_Front", world, LightType.Directional, new Color(0.55f, 0.52f, 0.85f), 0.25f);
            fill.transform.rotation = Quaternion.Euler(28f, -25f, 0f);
            fill.shadows = LightShadows.None;

            // Boden: Wiese + gepflasterter Platz
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(world, false);
            ground.transform.localScale = new Vector3(12f, 1f, 12f);
            Object.DestroyImmediate(ground.GetComponent<Collider>());
            ground.GetComponent<Renderer>().sharedMaterial = EnsureLitMaterial("MenuGround",
                "Assets/Painterly Terrain Textures/Painterly Terrain Textures - Impressionist Grass - Rich.BMP",
                new Color(0.55f, 0.58f, 0.5f), new Vector2(40f, 40f));

            var plaza = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            plaza.name = "Plaza";
            plaza.transform.SetParent(world, false);
            plaza.transform.localPosition = new Vector3(0f, -0.02f, 1.5f);
            plaza.transform.localScale = new Vector3(11f, 0.03f, 9f);
            Object.DestroyImmediate(plaza.GetComponent<Collider>());
            plaza.GetComponent<Renderer>().sharedMaterial = EnsureLitMaterial("MenuPlaza",
                "Assets/_Game/Environment/Town/plaza_stone.png", new Color(0.8f, 0.76f, 0.72f), new Vector2(4f, 4f));

            // Nexus-Kristall hinter den Champions
            var nexus = Place("Assets/_Game/Models/Nexus.fbx", world, new Vector3(0.4f, 0f, 8.5f), 35f, 1f);
            if (nexus != null)
            {
                nexus.name = "Nexus";
                var nl = NewLight("NexusGlow", nexus.transform, LightType.Point, new Color(0.62f, 0.5f, 1f), 6f);
                nl.transform.localPosition = new Vector3(0f, 3.6f, 0f);
                nl.range = 9f;
                nl.shadows = LightShadows.None;
            }

            // Stadtkulisse (Halbkreis hinter der Bühne)
            string town = "Assets/_Game/Prefabs/Town/";
            Place(town + "Kapelle.prefab", world, new Vector3(-10.5f, 0f, 17f), 160f, 1f);
            Place(town + "Taverne.prefab", world, new Vector3(11.5f, 0f, 15f), 205f, 1f);
            Place(town + "Haus_Turm.prefab", world, new Vector3(-17f, 0f, 8.5f), 115f, 1f);
            Place(town + "Wachturm.prefab", world, new Vector3(17.5f, 0f, 6f), 0f, 1f);
            Place(town + "Haus_Mittel.prefab", world, new Vector3(-4f, 0f, 24f), 165f, 1f);
            Place(town + "Haus_Lang.prefab", world, new Vector3(-20f, 0f, 22f), 150f, 1f);
            Place(town + "Schmiede.prefab", world, new Vector3(20f, 0f, 21f), 215f, 1f);
            Place(town + "Banner.prefab", world, new Vector3(-4.6f, 0f, 3.6f), 180f, 0.9f);
            Place(town + "Banner.prefab", world, new Vector3(4.6f, 0f, 3.6f), 180f, 0.9f);
            Place(town + "Laterne.prefab", world, new Vector3(-5.6f, 0f, 0.2f), 20f, 1f);
            Place(town + "Laterne.prefab", world, new Vector3(5.6f, 0f, 0.2f), -20f, 1f);
            Place(town + "Obelisk.prefab", world, new Vector3(-7.5f, 0f, 6.5f), 10f, 1f);
            Place(town + "Obelisk.prefab", world, new Vector3(7.5f, 0f, 6.5f), -10f, 1f);
            Place(town + "Fass.prefab", world, new Vector3(-6.4f, 0f, 2.4f), 30f, 1f);
            Place(town + "Kisten.prefab", world, new Vector3(6.6f, 0f, 2.6f), -40f, 1f);
            Place(town + "Heu.prefab", world, new Vector3(-8.5f, 0f, 1.2f), 70f, 1f);
            Place(town + "Karren.prefab", world, new Vector3(8.8f, 0f, 1.5f), -60f, 1f);
            Place(town + "Blumen.prefab", world, new Vector3(-3f, 0f, 4.8f), 0f, 1f);
            Place(town + "Blumen.prefab", world, new Vector3(3.2f, 0f, 4.8f), 90f, 1f);

            // Schwebende Lichtfunken über dem Platz
            var motes = NewMotes("AmbientMotes", world, new Vector3(0f, 2.2f, 2f), new Vector3(16f, 4f, 10f),
                new Color(1f, 0.78f, 0.45f, 0.9f), 26f, 0.05f, 0.14f, true);
            motes.transform.localPosition = new Vector3(0f, 2.2f, 2f);

            // --- Bühne: drei Sockel auf einer Drehscheibe (Karussell), der gewählte dreht nach vorne ---
            var stageGo = new GameObject("Stage");
            var stage = stageGo.AddComponent<MenuChampionStage>();
            var carousel = new GameObject("Carousel").transform;
            carousel.SetParent(stageGo.transform, false);
            carousel.localPosition = new Vector3(0f, 0f, 1.1f);
            stage.Carousel = carousel;

            const float radius = 1.45f;
            const float pedScale = 0.8f;
            var pedestals = new List<MenuChampionStage.Pedestal>();
            ChampionClass[] order = { ChampionClass.Mage, ChampionClass.Knight, ChampionClass.Archer };
            for (int i = 0; i < 3; i++)
            {
                // Winkel 180° = vorne (zur Kamera), Reihenfolge im Uhrzeigersinn
                float a = 180f + i * 120f;
                var ped = new GameObject("Pedestal_" + order[i]).transform;
                ped.SetParent(carousel, false);
                ped.localPosition = new Vector3(Mathf.Sin(a * Mathf.Deg2Rad), 0f, Mathf.Cos(a * Mathf.Deg2Rad)) * radius;
                ped.localRotation = Quaternion.Euler(0f, a + 180f, 0f); // lokales -Z zeigt nach außen
                var sockel = Place("Assets/_Game/Models/BuddySockel.fbx", ped, Vector3.zero, 0f, pedScale);
                if (sockel != null) sockel.transform.localPosition = new Vector3(0.05f * pedScale, 0f, 0.03f * pedScale);

                var anchor = new GameObject("Anchor").transform;
                anchor.SetParent(ped, false);
                anchor.localPosition = new Vector3(0f, 0.26f * pedScale, 0f);

                var spot = NewLight("Spot", ped, LightType.Spot, new Color(1f, 0.9f, 0.75f), 4f);
                spot.transform.localPosition = new Vector3(0f, 4.6f, -2.2f);
                spot.transform.LookAt(ped.position + Vector3.up * 0.7f);
                spot.range = 8f;
                spot.spotAngle = 30f;
                spot.innerSpotAngle = 14f;
                spot.shadows = LightShadows.Soft;

                var pm = NewMotes("Motes", ped, new Vector3(0f, 0.3f, 0f), new Vector3(1f, 0.1f, 1f),
                    Color.white, 14f, 0.035f, 0.09f, false);
                var shape = pm.shape;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = 0.55f;
                shape.rotation = new Vector3(-90f, 0f, 0f);
                var vel = pm.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.Local;
                // alle Achsen im selben Kurvenmodus (TwoConstants), sonst meckert Unity
                vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
                vel.y = new ParticleSystem.MinMaxCurve(0.3f, 0.7f);
                vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
                vel.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
                vel.orbitalY = new ParticleSystem.MinMaxCurve(0.6f, 1f);
                vel.orbitalZ = new ParticleSystem.MinMaxCurve(0f, 0f);

                pedestals.Add(new MenuChampionStage.Pedestal { Class = order[i], Anchor = anchor, Spot = spot, Motes = pm });
            }
            stage.Pedestals = pedestals.ToArray();

            // Blickpunkt etwas rechts der Bühne → Champions erscheinen in der freien Mitte zwischen Karten und Detail-Panel
            var look = new GameObject("CameraLookTarget").transform;
            look.SetParent(stageGo.transform, false);
            look.localPosition = new Vector3(0.3f, 1.05f, 0.4f);
            stage.LookTarget = look;

            // --- Kamera ---
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.Skybox;
            camGo.AddComponent<AudioListener>();
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camGo.transform.position = new Vector3(0.3f, 1.9f, -7.6f);
            camGo.transform.LookAt(look.position);
            stage.Cam = cam;

            var volGo = new GameObject("PostFX");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = EnsureVolumeProfile();

            // --- Musik (GameAudio der Menü-Szene; die Spielszene hat ihr eigenes, beim Laden wird dieses zerstört) ---
            var audioGo = new GameObject("MenuAudio");
            var ga = audioGo.AddComponent<GameAudio>();
            ga.MusicClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Game/Audio/Game Music Intro.mp3");
            ga.MusicVolume = 0.25f;
            ga.MusicFadeIn = 2.5f;
            ga.Voices = 2;

            // --- EventSystem ---
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();

            // --- UI ---
            BuildUI(defs, stage);
        }

        // ---------------- UI ----------------

        private static void BuildUI(ChampionDefinitionSO[] defs, MenuChampionStage stage)
        {
            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2400f, 1350f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            var ui = canvasGo.AddComponent<MainMenuUI>();
            ui.Champions = defs;
            ui.Stage = stage;
            var root = canvasGo.transform;

            // Abdunklung links (Lesbarkeit von Titel, Karten und Buttons)
            var shade = Rect("ShadeLeft", root, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(1250f, 0f));
            var shadeImg = Img(shade, MenuSprite("menu_shade"), new Color(1f, 1f, 1f, 0.92f), false);
            shadeImg.raycastTarget = false;

            // Titel
            var title = Text(root, "Title", "Elemental Buddies", _fHead, 124f, Cream, TextAlignmentOptions.TopLeft, _mHeadOutline);
            Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(80f, -50f), new Vector2(1200f, 150f));
            title.textWrappingMode = TextWrappingModes.NoWrap;
            var sub = Text(root, "Subtitle", "Verteidige die Stadt – mit Zauberkraft, Stahl und deinen Elementar-Buddies",
                _fBold, 30f, new Color(0.95f, 0.82f, 0.6f), TextAlignmentOptions.TopLeft, _mBoldOutline);
            Place(sub.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(88f, -190f), new Vector2(1100f, 44f));

            // Champion-Karten (linke Spalte)
            var chooseHdr = Text(root, "ChooseHeader", "Wähle deinen Champion", _fHead, 40f, Cream, TextAlignmentOptions.BottomLeft, _mHeadOutline);
            Place(chooseHdr.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(84f, -262f), new Vector2(700f, 56f));

            var cards = new List<MainMenuUI.Card>();
            for (int i = 0; i < defs.Length; i++)
            {
                var def = defs[i];
                if (def == null) continue;
                cards.Add(BuildCard(root, def, i, new Vector2(80f, -330f - i * 168f)));
            }
            ui.Cards = cards.ToArray();
            ui.CardNormalSprite = UISprite("slot_card");
            ui.CardSelectedSprite = UISprite("slot_card_selected");

            // Hauptbuttons (unten links)
            ui.PlayButton = MakeButton(root, "PlayButton", "Spielen", true, 50f, new Vector2(80f, 290f), new Vector2(540f, 110f));
            ui.SettingsButton = MakeButton(root, "SettingsButton", "Einstellungen", false, 32f, new Vector2(80f, 186f), new Vector2(540f, 82f));
            ui.QuitButton = MakeButton(root, "QuitButton", "Beenden", true, 34f, new Vector2(80f, 86f), new Vector2(540f, 82f));
            SetVerticalNav(ui.PlayButton, ui.SettingsButton, ui.QuitButton);

            var hint = Text(root, "KeyHint", KeyHintText,
                _fBold, 22f, new Color(0.95f, 0.85f, 0.7f, 0.85f), TextAlignmentOptions.Bottom, _mBoldOutline);
            Place(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-120f, 26f), new Vector2(1100f, 34f));

            BuildDetailPanel(root, ui);
            BuildSettings(root, ui);
            BuildAchievementExtras(root, ui);
            BuildDifficultyExtras(root, ui);

            // Überblender (zuletzt = oben)
            var fader = Rect("Fader", root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var fImg = Img(fader, null, Color.black, false);
            fImg.raycastTarget = true;
            var cg = fader.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
            cg.blocksRaycasts = false;
            cg.interactable = false;
            ui.Fader = cg;
        }

        private static MainMenuUI.Card BuildCard(Transform parent, ChampionDefinitionSO def, int index, Vector2 pos)
        {
            var card = new MainMenuUI.Card { Class = def.Class };
            var rt = Rect("Card_" + def.Class, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), pos, new Vector2(540f, 150f));
            card.Frame = Img(rt, UISprite("slot_card"), Color.white, true);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = card.Frame;
            card.Frame.raycastTarget = true; // Img() schaltet Raycasts ab – Klickfläche der Karte
            var cb = btn.colors;
            cb.highlightedColor = new Color(1f, 0.95f, 0.82f);
            cb.pressedColor = new Color(0.85f, 0.78f, 0.65f);
            cb.selectedColor = Color.white;
            btn.colors = cb;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            card.Button = btn;

            var accent = Rect("Accent", rt, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(8f, -40f));
            card.Accent = Img(accent, null, def.AccentColor, false);

            var med = Rect("Medallion", rt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(98f, 0f), new Vector2(112f, 112f));
            card.PortraitBg = Img(med, MenuSprite("menu_disc"), def.AccentColor, false);
            var por = Rect("Portrait", med, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-14f, -14f));
            card.Portrait = Img(por, null, Color.white, false);
            card.Portrait.preserveAspect = true;
            card.Initial = Text(med, "Initial", def.DisplayName.Substring(0, 1), _fHead, 62f, Cream, TextAlignmentOptions.Center, _mHeadOutline);
            Stretch(card.Initial.rectTransform, new Vector2(0f, 4f));
            var ring = Rect("Ring", med, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(8f, 8f));
            Img(ring, MenuSprite("menu_ring"), Color.white, false);

            card.Name = Text(rt, "Name", def.DisplayName, _fHead, 42f, Ink, TextAlignmentOptions.BottomLeft, null);
            Place(card.Name.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0f), new Vector2(172f, -2f), new Vector2(-236f, 54f));
            card.Name.enableAutoSizing = true;
            card.Name.fontSizeMin = 30f;
            card.Name.fontSizeMax = 42f;
            card.Tagline = Text(rt, "Tagline", def.Tagline, _fReg, 24f, InkLight, TextAlignmentOptions.TopLeft, null);
            Place(card.Tagline.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 1f), new Vector2(174f, -4f), new Vector2(-200f, 60f));

            var badge = Rect("KeyBadge", rt, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-34f, -34f), new Vector2(38f, 38f));
            Img(badge, UISprite("hotkey_badge"), Color.white, false);
            var key = Text(badge, "Key", (index + 1).ToString(), _fBold, 22f, Cream, TextAlignmentOptions.Center, null);
            Stretch(key.rectTransform, Vector2.zero);
            return card;
        }

        private static void BuildDetailPanel(Transform root, MainMenuUI ui)
        {
            var box = Rect("DetailPanel", root, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-50f, -30f), new Vector2(700f, 1150f));
            Img(box, UISprite("panel_parchment"), Color.white, true);

            var ribbon = Rect("Ribbon", box, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(600f, 110f));
            Img(ribbon, UISprite("ribbon_banner"), Color.white, true);
            ui.DetailName = Text(ribbon, "Title", "Champion", _fHead, 54f, Cream, TextAlignmentOptions.Center, null);
            Stretch(ui.DetailName.rectTransform, new Vector2(0f, 6f), new Vector2(0f, -12f));
            ui.DetailName.enableAutoSizing = true;
            ui.DetailName.fontSizeMin = 36f;
            ui.DetailName.fontSizeMax = 54f;

            ui.DetailTagline = Text(box, "Tagline", "Tagline", _fBold, 28f, InkLight, TextAlignmentOptions.Top, null);
            ui.DetailTagline.fontStyle = FontStyles.Italic;
            Place(ui.DetailTagline.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -78f), new Vector2(-100f, 40f));

            var accent = Rect("Accent", box, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -126f), new Vector2(220f, 5f));
            ui.DetailAccent = Img(accent, null, Color.white, false);

            ui.DetailDescription = Text(box, "Description", "Beschreibung", _fReg, 26f, Ink, TextAlignmentOptions.TopLeft, null);
            Place(ui.DetailDescription.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(-110f, 170f));
            ui.DetailDescription.lineSpacing = -4f;

            var hdr = Text(box, "AbilitiesHeader", "Fähigkeiten", _fHead, 36f, Ink, TextAlignmentOptions.BottomLeft, null);
            Place(hdr.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -318f), new Vector2(-110f, 50f));
            var line = Rect("Line", box, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -372f), new Vector2(-110f, 2f));
            Img(line, null, LineCol, false);

            var list = Rect("Abilities", box, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -386f), new Vector2(-90f, 720f));
            var vlg = list.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 6f;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            ui.AbilityContainer = list;

            // Vorlage einer Fähigkeits-Zeile
            var row = Rect("AbilityRowTemplate", list, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 112f));
            var le = row.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 112f;
            le.minHeight = 112f;
            var icon = Rect("Icon", row, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(46f, -8f), new Vector2(72f, 72f));
            var iconImg = Img(icon, null, Color.white, false);
            iconImg.preserveAspect = true;
            var kb = Rect("KeyBadge", row, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(46f, -80f), new Vector2(78f, 30f));
            Img(kb, UISprite("button_wood"), Color.white, true);
            var key = Text(kb, "Key", "LMB", _fBold, 19f, Cream, TextAlignmentOptions.Center, null);
            Stretch(key.rectTransform, Vector2.zero);
            var name = Text(row, "Name", "Fähigkeit", _fBold, 28f, Ink, TextAlignmentOptions.TopLeft, null);
            Place(name.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(104f, -6f), new Vector2(-110f, 38f));
            name.textWrappingMode = TextWrappingModes.NoWrap;
            name.overflowMode = TextOverflowModes.Ellipsis;
            var desc = Text(row, "Desc", "Beschreibung", _fReg, 22f, new Color(0.33f, 0.21f, 0.11f), TextAlignmentOptions.TopLeft, null);
            Place(desc.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(104f, -44f), new Vector2(-110f, 64f));
            desc.lineSpacing = -6f;
            row.gameObject.SetActive(false);
            ui.AbilityRowTemplate = row.gameObject;
        }

        private static void BuildSettings(Transform root, MainMenuUI ui)
        {
            var overlay = Rect("SettingsPanel", root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Img(overlay, null, new Color(0.08f, 0.04f, 0.03f, 0.78f), false).raycastTarget = true;
            ui.SettingsPanel = overlay.gameObject;

            var box = Rect("Box", overlay, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), new Vector2(1160f, 700f));
            Img(box, UISprite("panel_parchment"), Color.white, true);
            var ribbon = Rect("Ribbon", box, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(560f, 110f));
            Img(ribbon, UISprite("ribbon_banner"), Color.white, true);
            var t = Text(ribbon, "Title", "Einstellungen", _fHead, 54f, Cream, TextAlignmentOptions.Center, null);
            Stretch(t.rectTransform, new Vector2(0f, 6f), new Vector2(0f, -12f));

            var rows = Rect("Rows", box, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(-140f, 380f));
            var vlg = rows.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 26f;
            vlg.padding = new RectOffset(10, 10, 10, 10);
            vlg.childControlHeight = false;
            vlg.childControlWidth = false;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = false;

            TextMeshProUGUI val;
            ui.MasterSlider = SliderRow(rows, "MasterRow", "Gesamtlautstärke", out val); ui.MasterValue = val;
            ui.MusicSlider = SliderRow(rows, "MusicRow", "Musik", out val); ui.MusicValue = val;
            ui.SfxSlider = SliderRow(rows, "SfxRow", "Effekte", out val); ui.SfxValue = val;
            ui.FullscreenToggle = ToggleRow(rows, "FullscreenRow", "Vollbild");

            var hint = Text(box, "Hint", "Einstellungen werden automatisch gespeichert und gelten auch im Spiel.", _fReg, 22f, InkLight, TextAlignmentOptions.TopLeft, null);
            Place(hint.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(-160f, 40f));

            var back = MakeButton(box, "BackButton", "Zurück", true, 34f, Vector2.zero, new Vector2(320f, 84f));
            var brt = (RectTransform)back.transform;
            Place(brt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 46f), new Vector2(320f, 84f));
            ui.SettingsBackButton = back;
            overlay.gameObject.SetActive(false);
        }

        // ---------------- Erfolge & Champion-Sperren ----------------

        // Ergänzt die bestehende Menü-Szene um Erfolge-Knopf/-Seite, Schlösser auf den Karten und die Sperr-Hinweiszeile,
        // ohne den Rest neu zu bauen (ersetzt nur frühere Versionen dieser Objekte).
        public static void AddAchievementUIToScene()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("MainMenuBuilder: nur im Edit-Modus."); return; }
            if (!System.IO.File.Exists(ScenePath)) { Debug.LogWarning("MainMenuBuilder: " + ScenePath + " fehlt – erst \"Alles einrichten\"."); return; }
            LoadFonts();
            ImportMenuSprites();

            Scene prevActive = SceneManager.GetActiveScene();
            Scene menu = SceneManager.GetSceneByPath(ScenePath);
            bool wasLoaded = menu.IsValid() && menu.isLoaded;
            if (!wasLoaded) menu = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                MainMenuUI ui = null;
                foreach (var go in menu.GetRootGameObjects())
                {
                    ui = go.GetComponentInChildren<MainMenuUI>(true);
                    if (ui != null) break;
                }
                if (ui == null) { Debug.LogWarning("MainMenuBuilder: kein MainMenuUI in " + ScenePath); return; }

                var root = ui.transform;
                foreach (var n in new[] { "AchievementsPanel", "AchievementsButton", "PlayLockHint" })
                {
                    var old = root.Find(n);
                    if (old != null) Object.DestroyImmediate(old.gameObject);
                }
                foreach (var card in ui.Cards)
                {
                    if (card == null || card.PortraitBg == null) continue;
                    var oldLock = card.PortraitBg.transform.Find("Lock");
                    if (oldLock != null) Object.DestroyImmediate(oldLock.gameObject);
                }

                BuildAchievementExtras(root, ui);
                if (ui.Fader != null) ui.Fader.transform.SetAsLastSibling();
                EditorUtility.SetDirty(ui);
                EditorSceneManager.MarkSceneDirty(menu);
                EditorSceneManager.SaveScene(menu, ScenePath);
                Debug.Log("MainMenuBuilder: Erfolge-UI in " + ScenePath + " eingebaut.");
            }
            finally
            {
                if (prevActive.IsValid() && prevActive.isLoaded && prevActive != menu) SceneManager.SetActiveScene(prevActive);
                if (!wasLoaded && prevActive != menu) EditorSceneManager.CloseScene(menu, true);
            }
        }

        private static void BuildAchievementExtras(Transform root, MainMenuUI ui)
        {
            // Einstellungen + Erfolge teilen sich die mittlere Button-Zeile
            if (ui.SettingsButton != null)
                Place((RectTransform)ui.SettingsButton.transform, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(80f, 186f), new Vector2(264f, 82f));
            var ach = MakeButton(root, "AchievementsButton", "Erfolge", false, 32f, new Vector2(356f, 186f), new Vector2(264f, 82f));
            var achIcon = Rect("Icon", ach.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(22f, 2f), new Vector2(58f, 58f));
            Img(achIcon, Icon("icon_trophy"), Color.white, false).preserveAspect = true;
            var achText = ach.GetComponentInChildren<TextMeshProUGUI>();
            if (achText != null) achText.margin = new Vector4(64f, 0f, 0f, 0f);
            ui.AchievementsButton = ach;
            if (ui.SettingsButton != null) ach.transform.SetSiblingIndex(ui.SettingsButton.transform.GetSiblingIndex() + 1);

            // Navigation: Spielen ↕ Einstellungen ↔ Erfolge ↕ Beenden
            if (ui.PlayButton != null && ui.SettingsButton != null && ui.QuitButton != null)
            {
                SetVerticalNav(ui.PlayButton, ui.SettingsButton, ui.QuitButton);
                var nav = ui.SettingsButton.navigation;
                nav.selectOnRight = ach;
                ui.SettingsButton.navigation = nav;
                ach.navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnLeft = ui.SettingsButton,
                    selectOnUp = ui.PlayButton,
                    selectOnDown = ui.QuitButton,
                };
            }

            // Schloss über dem Porträt gesperrter Champions
            foreach (var card in ui.Cards)
            {
                if (card == null || card.PortraitBg == null) continue;
                var lockRt = Rect("Lock", card.PortraitBg.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -2f), new Vector2(76f, 76f));
                card.Lock = Img(lockRt, Icon("icon_lock"), Color.white, false);
                card.Lock.preserveAspect = true;
                lockRt.gameObject.SetActive(false);
            }

            // Hinweiszeile über "Spielen"
            var hint = Text(root, "PlayLockHint", "Gesperrt", _fBold, 25f, new Color(1f, 0.74f, 0.56f), TextAlignmentOptions.BottomLeft, _mBoldOutline);
            Place(hint.rectTransform, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(84f, 406f), new Vector2(1000f, 40f));
            hint.textWrappingMode = TextWrappingModes.NoWrap;
            hint.gameObject.SetActive(false);
            ui.PlayLockHint = hint;
            if (ui.PlayButton != null) hint.transform.SetSiblingIndex(ui.PlayButton.transform.GetSiblingIndex());

            BuildAchievementsPanel(root, ui);
            if (ui.Fader != null) ui.Fader.transform.SetAsLastSibling();
        }

        // ---------------- Schwierigkeit ----------------

        // Ergänzt die bestehende Menü-Szene um die Schwierigkeitsauswahl (Segment-Knöpfe zwischen Karten und "Spielen"),
        // ohne den Rest neu zu bauen (ersetzt nur frühere Versionen dieser Objekte).
        public static void AddDifficultyUIToScene()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("MainMenuBuilder: nur im Edit-Modus."); return; }
            if (!System.IO.File.Exists(ScenePath)) { Debug.LogWarning("MainMenuBuilder: " + ScenePath + " fehlt – erst \"Alles einrichten\"."); return; }
            LoadFonts();
            ImportMenuSprites();

            Scene prevActive = SceneManager.GetActiveScene();
            Scene menu = SceneManager.GetSceneByPath(ScenePath);
            bool wasLoaded = menu.IsValid() && menu.isLoaded;
            if (!wasLoaded) menu = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                MainMenuUI ui = null;
                foreach (var go in menu.GetRootGameObjects())
                {
                    ui = go.GetComponentInChildren<MainMenuUI>(true);
                    if (ui != null) break;
                }
                if (ui == null) { Debug.LogWarning("MainMenuBuilder: kein MainMenuUI in " + ScenePath); return; }

                var root = ui.transform;
                foreach (var n in new[] { "DifficultySelect", "DifficultyDescription" })
                {
                    var old = root.Find(n);
                    if (old != null) Object.DestroyImmediate(old.gameObject);
                }

                BuildDifficultyExtras(root, ui);
                if (ui.Fader != null) ui.Fader.transform.SetAsLastSibling();
                EditorUtility.SetDirty(ui);
                EditorSceneManager.MarkSceneDirty(menu);
                EditorSceneManager.SaveScene(menu, ScenePath);
                Debug.Log("MainMenuBuilder: Schwierigkeitsauswahl in " + ScenePath + " eingebaut.");
            }
            finally
            {
                if (prevActive.IsValid() && prevActive.isLoaded && prevActive != menu) SceneManager.SetActiveScene(prevActive);
                if (!wasLoaded && prevActive != menu) EditorSceneManager.CloseScene(menu, true);
            }
        }

        // Drei Segment-Knöpfe (Leicht/Normal/Schwer) unter den Champion-Karten, darunter die Faktor-Zeile
        // im Platz der Sperr-Hinweiszeile (MainMenuUI blendet je nach Sperre eine der beiden ein)
        private static void BuildDifficultyExtras(Transform root, MainMenuUI ui)
        {
            const float rowY = 462f, rowH = 52f, gap = 12f, totalW = 540f;
            int count = Mathf.Max(1, DifficultySO.All.Count);
            float w = (totalW - gap * (count - 1)) / count;

            var row = Rect("DifficultySelect", root, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(80f, rowY), new Vector2(totalW, rowH));
            var buttons = new Button[count];
            for (int i = 0; i < count; i++)
            {
                var d = i < DifficultySO.All.Count ? DifficultySO.All[i] : null;
                string label = d != null ? d.DisplayName : "Normal";
                var b = MakeButton(row, "Difficulty_" + (d != null ? d.Id : "normal"), label, false, 28f, new Vector2(i * (w + gap), 0f), new Vector2(w, rowH));
                b.GetComponentInChildren<TextMeshProUGUI>().font = _fBold;
                b.navigation = new Navigation { mode = Navigation.Mode.None };
                b.image.raycastTarget = true;
                buttons[i] = b;
            }
            ui.DifficultyButtons = buttons;
            ui.DifficultySelectedSprite = UISprite("button_wood");
            ui.DifficultySelectedHoverSprite = UISprite("button_wood_hover");
            ui.DifficultyNormalSprite = UISprite("button_parchment");
            ui.DifficultyNormalHoverSprite = UISprite("button_parchment_hover");
            ui.DifficultySelectedTextColor = Cream;
            ui.DifficultyNormalTextColor = Ink;

            var desc = Text(root, "DifficultyDescription", "Normal – Das vorgesehene Spielerlebnis.", _fBold, 22f,
                new Color(1f, 0.9f, 0.76f), TextAlignmentOptions.MidlineLeft, _mBoldOutline);
            Place(desc.rectTransform, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(84f, 404f), new Vector2(560f, 54f));
            desc.enableAutoSizing = true;
            desc.fontSizeMin = 16f;
            desc.fontSizeMax = 22f;
            desc.lineSpacing = -8f;
            ui.DifficultyDescription = desc;

            // Reihenfolge: hinter den Karten, vor "Spielen" (Sperr-Hinweis bleibt im selben Platz darüber)
            if (ui.PlayButton != null)
            {
                int idx = ui.PlayButton.transform.GetSiblingIndex();
                row.SetSiblingIndex(idx);
                desc.transform.SetSiblingIndex(idx + 1);
            }

            // Tasten-Hinweis um Q/E ergänzen
            var keyHint = root.Find("KeyHint");
            var kt = keyHint != null ? keyHint.GetComponent<TextMeshProUGUI>() : null;
            if (kt != null) kt.text = KeyHintText;
            if (ui.Fader != null) ui.Fader.transform.SetAsLastSibling();
        }

        private const string KeyHintText = "←/→ oder 1–3: Champion wechseln   ·   Q/E: Schwierigkeit   ·   Enter: Spielen   ·   Esc: zurück";

        private static void BuildAchievementsPanel(Transform root, MainMenuUI ui)
        {
            // Kräftige Abdunklung, damit Detail-Panel und Bühne nicht durchscheinen
            var overlay = Rect("AchievementsPanel", root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Img(overlay, null, new Color(0.04f, 0.025f, 0.02f, 0.9f), false).raycastTarget = true;
            ui.AchievementsPanel = overlay.gameObject;

            var box = Rect("Box", overlay, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -14f), new Vector2(1640f, 1180f));
            Img(box, UISprite("panel_parchment"), Color.white, true);
            var ribbon = Rect("Ribbon", box, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(560f, 110f));
            Img(ribbon, UISprite("ribbon_banner"), Color.white, true);
            var t = Text(ribbon, "Title", "Erfolge", _fHead, 54f, Cream, TextAlignmentOptions.Center, null);
            Stretch(t.rectTransform, new Vector2(0f, 6f), new Vector2(0f, -12f));
            ui.AchievementsTitle = t; // "Erfolge – Normal" (gewählte Stufe)

            // Zähler oben rechts
            var cIcon = Rect("CounterIcon", box, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(-300f, -34f), new Vector2(64f, 64f));
            Img(cIcon, Icon("icon_trophy"), Color.white, false).preserveAspect = true;
            ui.AchievementsCounter = Text(box, "Counter", "0 / 13", _fHead, 44f, Ink, TextAlignmentOptions.MidlineLeft, null);
            Place(ui.AchievementsCounter.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(-228f, -36f), new Vector2(180f, 60f));
            ui.AchievementsCounter.textWrappingMode = TextWrappingModes.NoWrap;

            var info = Text(box, "Info", InfoText, _fReg, 24f, InkLight, TextAlignmentOptions.MidlineLeft, null);
            Place(info.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(72f, -36f), new Vector2(480f, 60f));
            info.fontStyle = FontStyles.Italic;
            SetupInfo(info);

            // Scroll-Liste
            var scroll = Rect("Scroll", box, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0f, 14f), new Vector2(-120f, -248f));
            var viewport = Rect("Viewport", scroll, Vector2.zero, Vector2.one, new Vector2(0f, 1f), Vector2.zero, new Vector2(-40f, 0f));
            Img(viewport, null, new Color(1f, 1f, 1f, 0f), false).raycastTarget = true; // Fläche zum Ziehen/Scrollen
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Rect("Content", viewport, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 12f;
            vlg.padding = new RectOffset(4, 4, 4, 8);
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            ui.AchievementsContent = content;

            var sbRt = Rect("Scrollbar", scroll, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(22f, 0f));
            var track = Img(sbRt, null, new Color(0.24f, 0.15f, 0.08f, 0.18f), false);
            track.raycastTarget = true;
            var slide = Rect("Sliding Area", sbRt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var handle = Rect("Handle", slide, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var hImg = Img(handle, null, new Color(0.45f, 0.29f, 0.14f, 0.85f), false);
            hImg.raycastTarget = true;
            var bar = sbRt.gameObject.AddComponent<Scrollbar>();
            bar.handleRect = handle;
            bar.targetGraphic = hImg;
            bar.direction = Scrollbar.Direction.BottomToTop;
            bar.navigation = new Navigation { mode = Navigation.Mode.None };

            var sr = scroll.gameObject.AddComponent<ScrollRect>();
            sr.content = content;
            sr.viewport = viewport;
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 45f;
            sr.verticalScrollbar = bar;
            sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            ui.AchievementsScroll = sr;

            BuildAchievementRowTemplate(content, ui);

            var back = MakeButton(box, "BackButton", "Zurück", true, 34f, Vector2.zero, new Vector2(320f, 84f));
            Place((RectTransform)back.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(320f, 84f));
            ui.AchievementsBackButton = back;
            overlay.gameObject.SetActive(false);
        }

        // Hinweis oben links auf der Erfolge-Seite (Erfolge gelten pro Stufe)
        private const string InfoText = "Erfolge zählen auf ihrer Stufe und allen leichteren.\nFreischaltungen ab dem nächsten Spiel · Q/E: Stufe";

        private static void SetupInfo(TextMeshProUGUI info)
        {
            info.text = InfoText;
            info.enableAutoSizing = true;
            info.fontSizeMin = 16f;
            info.fontSizeMax = 22f;
            info.lineSpacing = -6f;
        }

        // Vorlage einer Erfolgs-Zeile: Medaille | Titel, Bedingung, Fortschritt | Belohnung
        private static void BuildAchievementRowTemplate(Transform content, MainMenuUI ui)
        {
            const float h = 152f;
            var row = Rect("AchievementRowTemplate", content, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, h));
            Img(row, UISprite("slot_card"), Color.white, true);
            var le = row.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = h;
            le.minHeight = h;

            var medal = Rect("Medal", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(88f, 0f), new Vector2(124f, 124f));
            var disc = Rect("Disc", medal, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(84f, 84f));
            Img(disc, MenuSprite("menu_disc"), new Color(0.36f, 0.25f, 0.16f), false);
            var icon = Rect("MedalIcon", medal, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(84f, 84f));
            Img(icon, null, Color.white, false).preserveAspect = true;
            var frame = Rect("MedalFrame", medal, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Img(frame, Icon("achievement_frame_locked"), Color.white, false).preserveAspect = true;

            var title = Text(row, "Title", "Erfolg", _fHead, 36f, Ink, TextAlignmentOptions.TopLeft, null);
            Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(166f, -20f), new Vector2(640f, 48f));
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.overflowMode = TextOverflowModes.Ellipsis;
            var desc = Text(row, "Desc", "Bedingung", _fReg, 25f, new Color(0.33f, 0.21f, 0.11f), TextAlignmentOptions.TopLeft, null);
            Place(desc.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(168f, -68f), new Vector2(680f, 34f));
            desc.textWrappingMode = TextWrappingModes.NoWrap;
            desc.enableAutoSizing = true;
            desc.fontSizeMin = 18f;
            desc.fontSizeMax = 25f;

            var barRt = Rect("Bar", row, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(168f, 18f), new Vector2(360f, 30f));
            Img(barRt, UISprite("bar_bg"), Color.white, true);
            var fill = Rect("BarFill", barRt, Vector2.zero, Vector2.one, new Vector2(0f, 0.5f), new Vector2(5f, 0f), new Vector2(-10f, -6f));
            Img(fill, UISprite("bar_fill"), BarFill, true);
            var prog = Text(row, "Progress", "0 / 1", _fBold, 25f, InkLight, TextAlignmentOptions.MidlineLeft, null);
            Place(prog.rectTransform, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(544f, 16f), new Vector2(220f, 34f));
            prog.textWrappingMode = TextWrappingModes.NoWrap;

            var div = Rect("Divider", row, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-480f, 0f), new Vector2(2f, -40f));
            Img(div, null, LineCol, false);
            var rIcon = Rect("RewardIcon", row, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-414f, 0f), new Vector2(96f, 96f));
            Img(rIcon, null, Color.white, false).preserveAspect = true;
            var rLabel = Text(row, "RewardLabel", "Schaltet frei:", _fReg, 22f, InkLight, TextAlignmentOptions.BottomLeft, null);
            Place(rLabel.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0f), new Vector2(-350f, 14f), new Vector2(330f, 44f));
            rLabel.enableAutoSizing = true;
            rLabel.fontSizeMin = 16f;
            rLabel.fontSizeMax = 22f;
            rLabel.lineSpacing = -10f;
            var rName = Text(row, "RewardName", "Belohnung", _fBold, 28f, Ink, TextAlignmentOptions.TopLeft, null);
            Place(rName.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 1f), new Vector2(-350f, 12f), new Vector2(330f, 62f));
            rName.enableAutoSizing = true;
            rName.fontSizeMin = 18f;
            rName.fontSizeMax = 28f;
            rName.lineSpacing = -8f;

            BuildTierMarkers(row);

            row.gameObject.SetActive(false);
            ui.AchievementRowTemplate = row.gameObject;
        }

        // Stufen-Marker L/N/S oben rechts im linken Zeilenteil (vor dem Trenner); Farben/Buchstaben setzt MainMenuUI
        private static void BuildTierMarkers(RectTransform row)
        {
            const float size = 40f, gap = 8f;
            int count = Progression.MaxRank;
            var tiers = Rect("Tiers", row, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-508f, -22f),
                new Vector2(count * size + (count - 1) * gap, size));
            for (int r = 1; r <= count; r++)
            {
                var m = Rect("Tier" + r, tiers, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2((r - 1) * (size + gap), 0f), new Vector2(size, size));
                Img(m, MenuSprite("menu_disc"), new Color(0.6f, 0.58f, 0.55f, 0.75f), false).preserveAspect = true;
                var lbl = Text(m, "Label", Progression.RankName(r).Substring(0, 1), _fBold, 24f, Cream, TextAlignmentOptions.Center, null);
                Stretch(lbl.rectTransform, Vector2.zero);
                lbl.textWrappingMode = TextWrappingModes.NoWrap;
            }
        }

        // Ergänzt die Erfolge-Seite der bestehenden Menü-Szene um die Stufen-Marker (Zeilenvorlage) und verknüpft die
        // Banner-Überschrift, ohne den Rest neu zu bauen (ersetzt nur frühere Marker).
        public static void AddAchievementTierUIToScene()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("MainMenuBuilder: nur im Edit-Modus."); return; }
            if (!System.IO.File.Exists(ScenePath)) { Debug.LogWarning("MainMenuBuilder: " + ScenePath + " fehlt – erst \"Alles einrichten\"."); return; }
            LoadFonts();
            ImportMenuSprites();

            Scene prevActive = SceneManager.GetActiveScene();
            Scene menu = SceneManager.GetSceneByPath(ScenePath);
            bool wasLoaded = menu.IsValid() && menu.isLoaded;
            if (!wasLoaded) menu = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                MainMenuUI ui = null;
                foreach (var go in menu.GetRootGameObjects())
                {
                    ui = go.GetComponentInChildren<MainMenuUI>(true);
                    if (ui != null) break;
                }
                if (ui == null || ui.AchievementRowTemplate == null || ui.AchievementsPanel == null)
                {
                    Debug.LogWarning("MainMenuBuilder: keine Erfolge-Seite in " + ScenePath + " – erst \"Erfolge-UI in bestehende Szene einbauen\".");
                    return;
                }

                var row = (RectTransform)ui.AchievementRowTemplate.transform;
                var old = row.Find("Tiers");
                if (old != null) Object.DestroyImmediate(old.gameObject);
                BuildTierMarkers(row);
                var ribbon = ui.AchievementsPanel.transform.Find("Box/Ribbon/Title");
                if (ribbon != null) ui.AchievementsTitle = ribbon.GetComponent<TextMeshProUGUI>();
                var info = ui.AchievementsPanel.transform.Find("Box/Info");
                if (info != null && info.GetComponent<TextMeshProUGUI>() != null) SetupInfo(info.GetComponent<TextMeshProUGUI>());

                EditorUtility.SetDirty(ui);
                EditorSceneManager.MarkSceneDirty(menu);
                EditorSceneManager.SaveScene(menu, ScenePath);
                Debug.Log("MainMenuBuilder: Stufen-Marker der Erfolge in " + ScenePath + " eingebaut.");
            }
            finally
            {
                if (prevActive.IsValid() && prevActive.isLoaded && prevActive != menu) SceneManager.SetActiveScene(prevActive);
                if (!wasLoaded && prevActive != menu) EditorSceneManager.CloseScene(menu, true);
            }
        }

        private static Slider SliderRow(Transform parent, string name, string label, out TextMeshProUGUI value)
        {
            var row = Rect(name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000f, 64f));
            var l = Text(row, "Label", label, _fBold, 30f, Ink, TextAlignmentOptions.MidlineLeft, null);
            Place(l.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(300f, 0f));

            var srt = Rect("Slider", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(310f, 0f), new Vector2(480f, 36f));
            var bg = Rect("Background", srt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, -12f));
            Img(bg, UISprite("bar_bg"), Color.white, true);
            var fillArea = Rect("Fill Area", srt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-12f, -20f));
            var fill = Rect("Fill", fillArea, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Img(fill, UISprite("bar_fill"), BarFill, true);
            var handleArea = Rect("Handle Slide Area", srt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-32f, 0f));
            var handle = Rect("Handle", handleArea, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 0f));
            var hImg = Img(handle, UISprite("hotkey_badge"), Color.white, false);
            hImg.raycastTarget = true;
            bg.GetComponent<Image>().raycastTarget = true; // Klick auf die Leiste setzt den Wert
            var slider = srt.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = hImg;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 1f;

            value = Text(row, "Value", "100 %", _fBold, 28f, InkLight, TextAlignmentOptions.MidlineLeft, null);
            Place(value.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(820f, 0f), new Vector2(120f, 0f));
            return slider;
        }

        private static Toggle ToggleRow(Transform parent, string name, string label)
        {
            var row = Rect(name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000f, 64f));
            var l = Text(row, "Label", label, _fBold, 30f, Ink, TextAlignmentOptions.MidlineLeft, null);
            Place(l.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(300f, 0f));
            var trt = Rect("Toggle", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(310f, 0f), new Vector2(52f, 52f));
            var bg = Rect("Background", trt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var bgImg = Img(bg, UISprite("bar_bg"), Color.white, true);
            var chk = Rect("Checkmark", bg, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-20f, -20f));
            var chkImg = Img(chk, UISprite("bar_fill"), BarFill, true);
            var tg = trt.gameObject.AddComponent<Toggle>();
            tg.targetGraphic = bgImg;
            bgImg.raycastTarget = true;
            tg.graphic = chkImg;
            tg.isOn = true;
            return tg;
        }

        // Holz- (wood=true, helle MedievalSharp-Schrift) oder Pergament-Button (dunkle Alegreya-Schrift), Anker unten links
        private static Button MakeButton(Transform parent, string name, string label, bool wood, float fontSize, Vector2 pos, Vector2 size)
        {
            var rt = Rect(name, parent, Vector2.zero, Vector2.zero, Vector2.zero, pos, size);
            var img = Img(rt, UISprite(wood ? "button_wood" : "button_parchment"), Color.white, true);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            img.raycastTarget = true; // Img() schaltet Raycasts ab – sonst ist der Button nicht klickbar
            btn.transition = Selectable.Transition.SpriteSwap;
            var ss = new SpriteState();
            ss.highlightedSprite = UISprite(wood ? "button_wood_hover" : "button_parchment_hover");
            ss.selectedSprite = ss.highlightedSprite;
            ss.pressedSprite = UISprite(wood ? "button_wood_pressed" : "button_parchment_hover");
            btn.spriteState = ss;
            var txt = Text(rt, "Text", label, wood ? _fHead : _fBold, fontSize, wood ? Cream : Ink, TextAlignmentOptions.Center, null);
            Stretch(txt.rectTransform, Vector2.zero);
            return btn;
        }

        private static void SetVerticalNav(params Button[] buttons)
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                var nav = new Navigation { mode = Navigation.Mode.Explicit };
                nav.selectOnUp = i > 0 ? buttons[i - 1] : null;
                nav.selectOnDown = i < buttons.Length - 1 ? buttons[i + 1] : null;
                buttons[i].navigation = nav;
            }
        }

        // ---------------- UI-Hilfen ----------------

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

        private static void Stretch(RectTransform rt, Vector2 pos, Vector2 size = default(Vector2))
        {
            Place(rt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), pos, size);
        }

        private static Image Img(RectTransform rt, Sprite sprite, Color color, bool sliced)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            img.raycastTarget = false;
            return img;
        }

        private static TextMeshProUGUI Text(Transform parent, string name, string text, TMP_FontAsset font, float size, Color color,
            TextAlignmentOptions align, Material mat)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = font;
            if (mat != null) t.fontSharedMaterial = mat;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            return t;
        }

        private static void LoadFonts()
        {
            _fHead = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontDir + "MedievalSharp SDF.asset");
            _fBold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontDir + "AlegreyaSans-Bold SDF.asset");
            _fReg = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontDir + "AlegreyaSans-Regular SDF.asset");
            _mHeadOutline = AssetDatabase.LoadAssetAtPath<Material>(FontDir + "MedievalSharp Outline.mat");
            _mBoldOutline = AssetDatabase.LoadAssetAtPath<Material>(FontDir + "AlegreyaSans-Bold Outline.mat");
        }

        private static Sprite UISprite(string name) { return AssetDatabase.LoadAssetAtPath<Sprite>(SpriteDir + name + ".png"); }
        private static Sprite Icon(string name) { return AssetDatabase.LoadAssetAtPath<Sprite>(IconDir + name + ".png"); }
        private static Sprite MenuSprite(string name) { return AssetDatabase.LoadAssetAtPath<Sprite>(MenuDir + name + ".png"); }

        private static void ImportMenuSprites()
        {
            foreach (var n in new[] { "menu_disc", "menu_ring", "menu_shade", "menu_glow" })
            {
                string path = MenuDir + n + ".png";
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp == null) continue;
                if (imp.textureType == TextureImporterType.Sprite) continue;
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.mipmapEnabled = false;
                imp.alphaIsTransparency = true;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.SaveAndReimport();
            }
        }

        // ---------------- 3D-Hilfen ----------------

        private static GameObject Place(string assetPath, Transform parent, Vector3 pos, float yaw, float scale)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (asset == null) { Debug.LogWarning("MainMenuBuilder: fehlt " + assetPath); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, SceneManager.GetActiveScene());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = Vector3.one * scale;
            return go;
        }

        private static Light NewLight(string name, Transform parent, LightType type, Color color, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var l = go.AddComponent<Light>();
            l.type = type;
            l.color = color;
            l.intensity = intensity;
            return l;
        }

        // Weiche Lichtpunkte (VFX_SoftDot_Add), langsam schwebend
        private static ParticleSystem NewMotes(string name, Transform parent, Vector3 localPos, Vector3 boxSize, Color color,
            float rate, float sizeMin, float sizeMax, bool drift)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.duration = 5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(drift ? 6f : 1.6f, drift ? 10f : 2.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, drift ? 0.15f : 0f);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startColor = color;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 300;
            main.useUnscaledTime = true;
            main.prewarm = drift;
            var em = ps.emission;
            em.rateOverTime = rate;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = boxSize;
            if (drift)
            {
                var noise = ps.noise;
                noise.enabled = true;
                noise.strength = 0.25f;
                noise.frequency = 0.25f;
                noise.scrollSpeed = 0.1f;
                var vel = ps.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.World;
                vel.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
                vel.y = new ParticleSystem.MinMaxCurve(0.03f, 0.12f);
                vel.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            }
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0.8f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/VFX/Materials/VFX_SoftDot_Add.mat");
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        // Dämmerungshimmel: Verlaufstextur (Horizont orange-rosa → Zenit violett) auf Skybox/Panoramic
        private static Material EnsureSkybox()
        {
            string texPath = MenuDir + "menu_sky_gradient.png";
            var imp = AssetImporter.GetAtPath(texPath) as TextureImporter;
            if (imp != null && (imp.mipmapEnabled || imp.wrapMode != TextureWrapMode.Clamp))
            {
                imp.mipmapEnabled = false;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.SaveAndReimport();
            }

            string path = MenuDir + "MenuSky.mat";
            var shader = Shader.Find("Skybox/Panoramic");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
            mat.SetFloat("_Mapping", 1f);   // Latitude-Longitude
            mat.DisableKeyword("_MAPPING_6_FRAMES_LAYOUT");
            mat.EnableKeyword("_MAPPING_LATITUDE_LONGITUDE_LAYOUT");
            mat.SetFloat("_ImageType", 0f); // 360°
            mat.SetFloat("_Exposure", 1f);
            mat.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f));
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material EnsureLitMaterial(string name, string texPath, Color tint, Vector2 tiling)
        {
            string path = MenuDir + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", tint);
            mat.SetTextureScale("_BaseMap", tiling);
            mat.SetFloat("_Smoothness", 0.05f);
            mat.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static VolumeProfile EnsureVolumeProfile()
        {
            string path = MenuDir + "MenuPostFX.asset";
            var prof = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (prof != null) return prof;
            prof = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(prof, path);

            var bloom = prof.Add<Bloom>(true);
            bloom.threshold.Override(0.9f);
            bloom.intensity.Override(0.7f);
            bloom.scatter.Override(0.65f);
            var vig = prof.Add<Vignette>(true);
            vig.intensity.Override(0.32f);
            vig.smoothness.Override(0.45f);
            vig.color.Override(new Color(0.12f, 0.04f, 0.08f));
            var ca = prof.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0.1f);
            ca.contrast.Override(12f);
            ca.saturation.Override(8f);
            var tm = prof.Add<Tonemapping>(true);
            tm.mode.Override(TonemappingMode.Neutral);
            foreach (var c in prof.components) AssetDatabase.AddObjectToAsset(c, prof);
            EditorUtility.SetDirty(prof);
            AssetDatabase.SaveAssets();
            return prof;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
