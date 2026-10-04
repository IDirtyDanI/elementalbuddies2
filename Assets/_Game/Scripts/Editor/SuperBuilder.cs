using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ElementalBuddies.EditorTools
{
    // Baut die Super-Elementare (Tri-Fusion) aus den Blender-Modellen unter Models/Super/<Name>/:
    // Modell-Import (Generic, Clips, URP-Materialien), Animator-Controller (Animations/Super), Effekt-Prefabs
    // (VFX/Prefabs/Super), Buddy-Prefab (Prefabs/Super), Config-Prefab-Zuweisung, Porträt (UI/Icons/portrait_super_*.png)
    // und Rezept-Icon in der offenen Szene (FusionManager.TriRecipes).
    // Menü: BuddyTD → Super-Elementare → <Name> bauen. Jeder Super hat eine eigene Build-Methode; die gemeinsamen
    // Bausteine (ImportCharacter, ImportProp, BuildController, NewBuddyRoot, Sockel, Partikel-Helfer, RenderPortrait,
    // AssignConfig, WireRecipeIcon) stehen unten und sind für Sturmfürst, Phönix und Weltenbaum wiederverwendbar.
    public static class SuperBuilder
    {
        internal const string ModelDir = "Assets/_Game/Models/Super/";
        internal const string AnimDir = "Assets/_Game/Animations/Super/";
        internal const string PrefabDir = "Assets/_Game/Prefabs/Super/";
        internal const string FxDir = "Assets/_Game/VFX/Prefabs/Super/";
        internal const string ConfigDir = "Assets/ScriptableObjects/Configs/";
        internal const string IconDir = "Assets/_Game/UI/Icons/";
        internal const string VfxMat = "Assets/_Game/VFX/Materials/";
        internal const string VfxPrefabs = "Assets/_Game/VFX/Prefabs/";
        internal const string SockelFbx = "Assets/_Game/Models/BuddySockel.fbx";

        // =====================================================================================================
        // Vulkan-Titan (Feuer + Eis + Erde)
        // =====================================================================================================

        internal const string TitanDir = ModelDir + "Titan/";
        public static readonly Color TitanLava = new Color(1f, 0.42f, 0.08f);
        public static readonly Color TitanIce = new Color(0.55f, 0.85f, 1f);
        // Modell: ~3,3 m hoch (mit Stacheln) → 0,9 ≈ 2,6 m über dem Sockel (2er-Fusionen ~1,8 m)
        public const float TitanScale = 0.9f;
        public const float TitanSockelHeight = 0.30f;
        // Cast-Clip 1,3 s, Freigabe bei 0,667 s; State-Speed 1,5 → Freigabe ~0,44 s nach dem Trigger (Meteor fällt 0,9 s)
        public const float TitanCastSpeed = 1.5f;

        [MenuItem("BuddyTD/Super-Elementare/Vulkan-Titan bauen")]
        public static void BuildVolcanoTitan()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("SuperBuilder: nur im Edit-Mode bauen.");
                return;
            }
            Folders();
            string matDir = TitanDir + "Materials/";
            Folder(TitanDir.TrimEnd('/'), "Materials");
            var glow = new System.Collections.Generic.Dictionary<string, float>
            {
                { "Titan_Lava_Glow", 2.3f }, { "Titan_Eye_Glow", 2.5f }, { "Titan_Ice_Glow", 2.2f }
            };
            ImportCharacter(TitanDir + "VolcanoTitan.fbx", matDir, glow, "Titan_Idle");
            ImportProp(TitanDir + "Titan_Meteor.fbx", matDir, glow);
            ImportProp(TitanDir + "Titan_Crater.fbx", matDir, glow);

            var ctrl = BuildController(AnimDir + "VolcanoTitan.controller", Clip(TitanDir + "VolcanoTitan.fbx", "Titan_Idle"),
                Clip(TitanDir + "VolcanoTitan.fbx", "Titan_Cast"), TitanCastSpeed, Clip(TitanDir + "VolcanoTitan.fbx", "Titan_Hit"));

            var meteor = BuildMeteor();
            var impact = BuildMeteorImpact();
            var crater = BuildMeteorCrater();
            var telegraph = BuildMeteorTelegraph();
            var stun = AssetDatabase.LoadAssetAtPath<GameObject>(VfxPrefabs + "VFX_Blinded.prefab");

            var config = AssetDatabase.LoadAssetAtPath<UnitConfigSO>(ConfigDir + "Super_Vulkan.asset");
            var prefab = BuildTitanPrefab(ctrl, config, meteor, impact, crater, telegraph, stun);
            AssignConfig(config, prefab);

            string icon = IconDir + "portrait_super_vulkan.png";
            RenderPortrait(prefab, icon, new Vector3(0f, 2.45f, -4.6f), new Vector3(0f, 1.7f, -0.2f), 28f);
            WireRecipeIcon(FusionElement.VolcanoTitan, icon);
            AssetDatabase.SaveAssets();
            Debug.Log("SuperBuilder: Vulkan-Titan gebaut (" + PrefabDir + "Super_Vulkan.prefab).");
        }

        // ---------------- Titan: Buddy-Prefab ----------------

        internal static GameObject BuildTitanPrefab(AnimatorController ctrl, UnitConfigSO config, GameObject meteor, GameObject impact,
            GameObject crater, GameObject telegraph, GameObject stun)
        {
            var root = NewBuddyRoot("Super_Vulkan");
            // Kapsel um den Rumpf (nicht um die weit ausgestellten Arme/Stacheln): blockiert das Bauen nur ~1,3 m um die Mitte
            var cap = root.AddComponent<CapsuleCollider>();
            cap.center = new Vector3(0f, 1.3f, 0f);
            cap.radius = 0.85f;
            cap.height = 2.6f;

            // Breiter Basalt-Sockel (BuddySockel vergrößert, Lava-Runen statt Feuer-Runen, Asche statt Moos)
            string mats = TitanDir + "Materials/";
            Sockel(root.transform, new Vector3(1.9f, 1.15f, 1.9f), new[]
            {
                Load<Material>(mats + "Titan_Basalt.mat"), Load<Material>(mats + "Titan_Obsidian.mat"),
                GlowMaterial(mats + "Titan_Sockel_Rune.mat", new Color(1f, 0.3f, 0.05f), 1.2f), Load<Material>(mats + "Titan_Ash.mat")
            });

            var visual = AddModel(root.transform, TitanDir + "VolcanoTitan.fbx", "Visual", new Vector3(0f, TitanSockelHeight, 0f), TitanScale, ctrl);
            var throwPoint = Deep(visual, "ThrowPoint");
            var vent = Deep(visual, "CraterVent");

            var fp = new GameObject("FirePoint").transform;
            fp.SetParent(root.transform, false);
            fp.position = throwPoint != null ? throwPoint.position : new Vector3(0f, 2.9f, -2f);

            var glowLight = PointLight(root.transform, "Glow", new Vector3(0f, 1.6f, -1.1f), TitanLava, 1.5f, 4.5f);

            // Krater auf dem Rücken: Rauchsäule, Glut, Glühen (folgen dem Chest-Knochen)
            if (vent != null) TitanVentFx(vent);
            // Wurf-Blitz an der Wurfhand-Position (zur Freigabe der Cast-Animation)
            if (throwPoint != null) TitanThrowFx(throwPoint);

            var fx = new GameObject("ElementFX").transform;
            fx.SetParent(root.transform, false);
            TitanElementFx(fx);

            var buddy = root.AddComponent<VolcanoTitanBuddy>();
            buddy.Config = config;
            buddy.Element = FusionElement.VolcanoTitan;
            buddy.BaseMaxHP = 600f;
            buddy.DamageReduction = 0.2f;
            buddy.Visual = visual;
            buddy.FirePoint = fp;
            buddy.MeteorPrefab = meteor;
            buddy.CraterPrefab = crater;
            buddy.ImpactVfxPrefab = impact;
            buddy.TelegraphPrefab = telegraph;
            buddy.StunVfxPrefab = stun;
            buddy.TelegraphColor = new Color(1f, 0.4f, 0.1f, 0.9f);
            if (glowLight == null) Debug.LogWarning("SuperBuilder: kein Glow-Licht");
            return SavePrefab(root, PrefabDir + "Super_Vulkan.prefab");
        }

        // Krater-Schlot: Rauchsäule + Glut in Weltkoordinaten (steigen senkrecht, egal wie der Oberkörper kippt)
        internal static void TitanVentFx(Transform ventBone)
        {
            var vent = FxAnchor(ventBone, "FX_Vent");
            var smoke = PS(vent, "FX_VentSmoke", Vector3.zero, SmokeAlpha, new Vector2(2.2f, 3.0f), new Vector2(0.45f, 0.7f),
                new Color(0.32f, 0.3f, 0.29f, 0.75f), 40, true);
            Rate(smoke, 9f); WorldSim(smoke); WorldRise(smoke, 0.8f, 1.2f); Noise(smoke, 0.25f, 0.5f); Spin(smoke, 35f);
            SizeLife(smoke, 0.7f, 2.8f);
            ColorLife(smoke, Fade(new Color(1f, 0.75f, 0.55f), new Color(0.6f, 0.58f, 0.56f), 0.85f, 0.12f, 0.45f));
            var em = PS(vent, "FX_VentEmbers", Vector3.zero, SparkAdd, new Vector2(0.8f, 1.4f), new Vector2(0.05f, 0.1f), TitanLava, 40, true);
            Rate(em, 16f); WorldSim(em); Cone(em, 25f, 0.15f);
            var m = em.main; m.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f); m.gravityModifier = 0.35f;
            ColorLife(em, Heat());
            var lava = PS(vent, "FX_VentGlow", Vector3.zero, SoftAdd, new Vector2(0.6f, 0.9f), new Vector2(0.7f, 0.95f),
                new Color(1f, 0.45f, 0.1f, 0.7f), 6, true);
            Rate(lava, 5f); ColorLife(lava, Fade(Color.white, Color.white, 1f, 0.3f, 0.6f));
        }

        // Wurf-Blitz: kurzer Feuerstoß + nach oben schießende Glut (SuperCastFx spielt ihn zur Freigabe ab)
        internal static void TitanThrowFx(Transform throwPoint)
        {
            var holder = FxAnchor(throwPoint, "FX_Throw");
            var cast = holder.gameObject.AddComponent<SuperCastFx>();
            cast.Delay = 0.667f / TitanCastSpeed;
            var flash = PS(holder, "Flash", Vector3.zero, SoftAdd, new Vector2(0.25f, 0.3f), new Vector2(2.2f, 2.6f), new Color(1f, 0.55f, 0.15f), 2, false);
            Burst(flash, 1); NoAwake(flash);
            SizeLife(flash, 0.4f, 1.2f); ColorLife(flash, Fade(Color.white, Color.white, 1f, 0.05f, 0.4f));
            LightModule(flash, 3f, 6f);
            var streak = PS(holder, "UpStreak", Vector3.zero, SparkAdd, new Vector2(0.4f, 0.7f), new Vector2(0.12f, 0.2f), TitanLava, 30, false);
            Burst(streak, 22); NoAwake(streak); WorldSim(streak); Cone(streak, 18f, 0.2f);
            var ms = streak.main; ms.startSpeed = new ParticleSystem.MinMaxCurve(9f, 15f);
            Stretch(streak, 0.06f, 2f); ColorLife(streak, Heat());
            var fire = PS(holder, "Fire", Vector3.zero, FlameAdd, new Vector2(0.3f, 0.45f), new Vector2(0.6f, 1.1f), new Color(1f, 0.5f, 0.12f), 20, false);
            Burst(fire, 12); NoAwake(fire); WorldSim(fire); Sphere(fire, 0.3f);
            var mf = fire.main; mf.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3f);
            Spin(fire, 180f); SizeLife(fire, 1f, 0.3f); ColorLife(fire, Heat());
        }

        // Umgebung: Hitze-Bodenring, Bodenglühen, aufsteigende Glut, ein paar treibende Frost-Funken (Eis-Anteil)
        internal static void TitanElementFx(Transform fx)
        {
            var ring = PS(fx, "HeatRing", new Vector3(0f, 0.32f, 0f), RingAdd, new Vector2(2.2f, 2.2f), new Vector2(3.4f, 3.4f),
                new Color(1f, 0.4f, 0.08f, 0.45f), 4, true);
            Rate(ring, 0.9f); Flat(ring); SizeLife(ring, 0.85f, 1.25f); ColorLife(ring, Fade(Color.white, Color.white, 1f, 0.25f, 0.6f));
            var glow = PS(fx, "LavaGlow", new Vector3(0f, 0.31f, 0f), SoftAdd, new Vector2(1.8f, 2.4f), new Vector2(3.0f, 3.4f),
                new Color(1f, 0.32f, 0.05f, 0.3f), 6, true);
            Rate(glow, 1.2f); Flat(glow); ColorLife(glow, Fade(Color.white, Color.white, 1f, 0.3f, 0.6f));
            var em = PS(fx, "Embers", new Vector3(0f, 1.0f, 0f), SoftAdd, new Vector2(1.4f, 2.2f), new Vector2(0.07f, 0.13f), TitanLava, 60, true);
            Rate(em, 16f); Sphere(em, 1.1f, new Vector3(1.3f, 1.1f, 1f), 0.3f); Rise(em, 0.6f, 1.3f); Noise(em, 0.5f, 1.1f);
            ColorLife(em, Heat());
            var frost = PS(fx, "FrostMotes", new Vector3(0f, 1.3f, 0f), SoftAdd, new Vector2(2.2f, 3.0f), new Vector2(0.06f, 0.11f), TitanIce, 24, true);
            Rate(frost, 6f); Sphere(frost, 1.3f, new Vector3(1.2f, 1f, 1f), 0.25f); Rise(frost, -0.15f, 0.25f); Noise(frost, 0.45f, 0.7f);
            Trails(frost, TrailAdd, 0.25f, 0.035f, new Color(0.6f, 0.9f, 1f));
            ColorLife(frost, Fade(Color.white, Color.white, 1f, 0.2f, 0.7f));
            var glint = PS(fx, "FrostGlints", new Vector3(0f, 1.5f, 0f), StarAdd, new Vector2(0.4f, 0.6f), new Vector2(0.16f, 0.26f), new Color(0.75f, 0.92f, 1f), 8, true);
            Rate(glint, 3f); Sphere(glint, 1.2f, new Vector3(1.2f, 1f, 1f), 0.2f); SizeBump(glint); Spin(glint, 90f);
        }

        // ---------------- Titan: Effekte ----------------

        // Meteor: Basaltbrocken (Titan_Meteor) mit Flammenmantel, Schweif, Rauch, Glut, Frostfunken, Licht.
        // Partikel in Weltkoordinaten; LavaBall.DetachTrails löst sie beim Einschlag ab.
        internal static GameObject BuildMeteor()
        {
            var root = new GameObject("VFX_Meteor");
            var rock = AddModel(root.transform, TitanDir + "Titan_Meteor.fbx", "Rock", Vector3.zero, 1.35f, null);
            rock.localRotation = Quaternion.Euler(20f, 35f, 10f) * rock.localRotation;
            var trail = new GameObject("Trail");
            trail.transform.SetParent(root.transform, false);
            var tr = trail.AddComponent<TrailRenderer>();
            tr.sharedMaterial = Load<Material>(VfxMat + "VFX_LavaTrail.mat");
            tr.time = 0.32f; tr.minVertexDistance = 0.1f;
            tr.widthCurve = new AnimationCurve(new Keyframe(0f, 1.3f), new Keyframe(1f, 0f));
            var tg = new Gradient();
            tg.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.85f, 0.4f), 0f), new GradientColorKey(new Color(1f, 0.3f, 0.05f), 0.5f), new GradientColorKey(new Color(0.4f, 0.08f, 0.02f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
            tr.colorGradient = tg;
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var flames = PS(root.transform, "Flames", Vector3.zero, FlameAdd, new Vector2(0.25f, 0.4f), new Vector2(1.0f, 1.6f), new Color(1f, 0.55f, 0.15f), 120, true);
            Rate(flames, 90f); WorldSim(flames); Sphere(flames, 0.45f); Spin(flames, 200f); SizeLife(flames, 1f, 0.2f); ColorLife(flames, Heat());
            var smoke = PS(root.transform, "Smoke", Vector3.zero, SmokeAlpha, new Vector2(1.0f, 1.5f), new Vector2(1.0f, 1.6f), new Color(0.2f, 0.18f, 0.17f, 0.85f), 120, true);
            Rate(smoke, 45f); WorldSim(smoke); Sphere(smoke, 0.4f); Spin(smoke, 40f); SizeLife(smoke, 0.6f, 2.2f); Noise(smoke, 0.3f, 0.6f);
            ColorLife(smoke, Fade(Color.white, new Color(0.55f, 0.52f, 0.5f), 0.9f, 0.08f, 0.4f));
            var embers = PS(root.transform, "Embers", Vector3.zero, SparkAdd, new Vector2(0.5f, 0.8f), new Vector2(0.08f, 0.14f), TitanLava, 80, true);
            Rate(embers, 55f); WorldSim(embers); Sphere(embers, 0.5f);
            var me = embers.main; me.startSpeed = new ParticleSystem.MinMaxCurve(1f, 4f); me.gravityModifier = 0.3f;
            ColorLife(embers, Heat());
            var frost = PS(root.transform, "FrostSparks", Vector3.zero, SoftAdd, new Vector2(0.4f, 0.7f), new Vector2(0.08f, 0.14f), TitanIce, 40, true);
            Rate(frost, 22f); WorldSim(frost); Sphere(frost, 0.55f);
            ColorLife(frost, Fade(Color.white, Color.white, 1f, 0.1f, 0.5f));
            PointLight(root.transform, "Glow", Vector3.zero, new Color(1f, 0.45f, 0.1f), 6f, 7f);
            return SaveFx(root, "VFX_Meteor");
        }

        // Einschlag (Radius 3,5, lebt 2,5 s, nicht skaliert): Lichtblitz, Feuerball, Lavaspritzer, Gesteinsbrocken,
        // Staubring, Druckwelle (orange + eisblau), Eissplitter, Rauchpilz, Glut
        internal static GameObject BuildMeteorImpact()
        {
            var root = new GameObject("VFX_MeteorImpact");
            var t = root.transform;
            var flash = PS(t, "Flash", new Vector3(0f, 0.8f, 0f), SoftAdd, new Vector2(0.35f, 0.35f), new Vector2(6f, 6f), new Color(1f, 0.7f, 0.35f), 2, false);
            Burst(flash, 1); SizeLife(flash, 0.5f, 1.2f); ColorLife(flash, Fade(Color.white, Color.white, 1f, 0.02f, 0.3f));
            LightModule(flash, 8f, 12f);
            var fireball = PS(t, "Fireball", new Vector3(0f, 0.5f, 0f), FlameAdd, new Vector2(0.5f, 0.85f), new Vector2(1.6f, 2.8f), new Color(1f, 0.5f, 0.12f), 50, false);
            Burst(fireball, 40); Hemisphere(fireball, 1.0f);
            var mf = fireball.main; mf.startSpeed = new ParticleSystem.MinMaxCurve(3f, 8f);
            Drag(fireball, 3f); Spin(fireball, 120f); SizeLife(fireball, 1f, 0.4f); ColorLife(fireball, Heat());
            var splash = PS(t, "LavaSplash", new Vector3(0f, 0.3f, 0f), SoftAdd, new Vector2(0.7f, 1.1f), new Vector2(0.15f, 0.3f), new Color(1f, 0.45f, 0.1f), 60, false);
            Burst(splash, 45); Cone(splash, 50f, 0.6f);
            var ms = splash.main; ms.startSpeed = new ParticleSystem.MinMaxCurve(5f, 11f); ms.gravityModifier = 2f;
            Stretch(splash, 0.04f, 1.5f); ColorLife(splash, Heat());

            var debris = PS(t, "Debris", new Vector3(0f, 0.3f, 0f), Load<Material>(TitanDir + "Materials/Titan_Basalt.mat"),
                new Vector2(1.3f, 1.7f), new Vector2(0.25f, 0.5f), Color.white, 20, false);
            Burst(debris, 16); Cone(debris, 55f, 0.8f);
            var md = debris.main; md.startSpeed = new ParticleSystem.MinMaxCurve(5f, 10f); md.gravityModifier = 2.2f;
            MeshParticles(debris, SingleSubmesh(TitanDir + "Titan_Meteor.fbx", TitanDir + "Titan_Debris.asset"));
            Collide(debris);
            var sz = debris.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.8f, 1f), new Keyframe(1f, 0f)));

            var shards = PS(t, "IceShards", new Vector3(0f, 0.3f, 0f), Load<Material>(VfxMat + "VFX_Ice_Shard.mat"),
                new Vector2(0.8f, 1.1f), new Vector2(0.25f, 0.45f), Color.white, 14, false);
            Burst(shards, 10); Cone(shards, 60f, 0.6f);
            var mi = shards.main; mi.startSpeed = new ParticleSystem.MinMaxCurve(4f, 8f); mi.gravityModifier = 1.6f;
            MeshParticles(shards, Load<Mesh>(VfxMat + "IceShard.asset"));
            var ssz = shards.sizeOverLifetime; ssz.enabled = true;
            ssz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.75f, 1f), new Keyframe(1f, 0f)));

            var dust = PS(t, "DustRing", new Vector3(0f, 0.35f, 0f), SmokeAlpha, new Vector2(1.2f, 1.7f), new Vector2(1.3f, 2.0f), new Color(0.72f, 0.64f, 0.56f, 0.9f), 40, false);
            Burst(dust, 32); Ring(dust, 0.6f);
            var mdu = dust.main; mdu.startSpeed = new ParticleSystem.MinMaxCurve(6f, 9f);
            Drag(dust, 4f); Spin(dust, 60f); SizeLife(dust, 0.7f, 1.8f); ColorLife(dust, Fade(Color.white, Color.white, 0.9f, 0.05f, 0.45f));

            var wave = PS(t, "Shockwave", new Vector3(0f, 0.4f, 0f), RingAdd, new Vector2(0.5f, 0.5f), new Vector2(1f, 1f), new Color(1f, 0.5f, 0.12f), 2, false);
            Burst(wave, 1); Flat(wave); SizeLife(wave, 1.5f, 8.5f); ColorLife(wave, Fade(Color.white, Color.white, 1f, 0.02f, 0.4f));
            var frostWave = PS(t, "FrostWave", new Vector3(0f, 0.38f, 0f), RingAdd, new Vector2(0.6f, 0.6f), new Vector2(1f, 1f), TitanIce, 2, false);
            Burst(frostWave, 1, 0.3f); Flat(frostWave); SizeLife(frostWave, 1f, 6f); ColorLife(frostWave, Fade(Color.white, Color.white, 0.9f, 0.05f, 0.4f));

            var mush = PS(t, "SmokeColumn", new Vector3(0f, 0.6f, 0f), SmokeAlpha, new Vector2(1.6f, 2.2f), new Vector2(1.4f, 2.2f), new Color(0.25f, 0.22f, 0.2f, 0.85f), 24, false);
            Burst(mush, 18, 0.05f); Sphere(mush, 0.8f);
            var mm = mush.main; mm.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
            Rise(mush, 1.2f, 2.4f); Spin(mush, 40f); SizeLife(mush, 0.6f, 1.7f);
            ColorLife(mush, Fade(new Color(1f, 0.7f, 0.5f), new Color(0.6f, 0.57f, 0.55f), 0.85f, 0.08f, 0.45f));
            var embers = PS(t, "Embers", new Vector3(0f, 0.5f, 0f), SparkAdd, new Vector2(0.8f, 1.4f), new Vector2(0.08f, 0.15f), TitanLava, 60, false);
            Burst(embers, 45); Hemisphere(embers, 0.8f);
            var mem = embers.main; mem.startSpeed = new ParticleSystem.MinMaxCurve(4f, 10f); mem.gravityModifier = 0.8f;
            ColorLife(embers, Heat());
            return SaveFx(root, "VFX_MeteorImpact");
        }

        // Krater (Einheitsradius 1, LavaPuddle skaliert x/z auf CraterRadius und schrumpft in den letzten 0,5 s):
        // Titan_Crater-Mesh (3 m Radius → 1/3), glühende Mitte, Rauchfäden, Blasen, Frostreif-Funkeln, Licht
        internal static GameObject BuildMeteorCrater()
        {
            var root = new GameObject("VFX_MeteorCrater");
            var t = root.transform;
            var holder = new GameObject("Crater").transform;
            holder.SetParent(t, false);
            holder.localPosition = new Vector3(0f, 0.04f, 0f);
            holder.localScale = new Vector3(1f / 3f, 1f, 1f / 3f);
            AddModel(holder, TitanDir + "Titan_Crater.fbx", "CraterMesh", Vector3.zero, 1f, null);
            var glow = PS(t, "HeatGlow", new Vector3(0f, 0.2f, 0f), SoftAdd, new Vector2(1.2f, 1.6f), new Vector2(1.6f, 2.0f), new Color(1f, 0.35f, 0.06f, 0.7f), 6, true);
            Rate(glow, 2.5f); Flat(glow); ColorLife(glow, Fade(Color.white, Color.white, 1f, 0.3f, 0.6f));
            var bub = PS(t, "Bubbles", new Vector3(0f, 0.18f, 0f), SoftAdd, new Vector2(0.4f, 0.7f), new Vector2(0.12f, 0.25f), new Color(1f, 0.6f, 0.2f), 20, true);
            Rate(bub, 10f); Ring(bub, 0.35f, 1f); SizeBump(bub);
            var smoke = PS(t, "SmokeWisps", new Vector3(0f, 0.25f, 0f), SmokeAlpha, new Vector2(1.6f, 2.2f), new Vector2(0.5f, 0.8f), new Color(0.3f, 0.27f, 0.25f, 0.7f), 30, true);
            Rate(smoke, 8f); Ring(smoke, 0.55f, 0.6f); WorldSim(smoke); WorldRise(smoke, 0.6f, 1.1f); Spin(smoke, 35f); Noise(smoke, 0.2f, 0.6f);
            SizeLife(smoke, 0.6f, 1.8f); ColorLife(smoke, Fade(Color.white, Color.white, 0.8f, 0.2f, 0.5f));
            var em = PS(t, "Embers", new Vector3(0f, 0.25f, 0f), SparkAdd, new Vector2(0.7f, 1.1f), new Vector2(0.05f, 0.1f), TitanLava, 20, true);
            Rate(em, 8f); Ring(em, 0.5f, 1f); Rise(em, 0.8f, 1.6f); ColorLife(em, Heat());
            var frost = PS(t, "FrostGlints", new Vector3(0f, 0.25f, 0f), StarAdd, new Vector2(0.4f, 0.6f), new Vector2(0.14f, 0.22f), new Color(0.75f, 0.92f, 1f), 8, true);
            Rate(frost, 3f); Ring(frost, 0.85f, 0.2f); SizeBump(frost); Spin(frost, 90f);
            PointLight(t, "Glow", new Vector3(0f, 0.6f, 0f), new Color(1f, 0.4f, 0.08f), 3f, 6f);
            return SaveFx(root, "VFX_MeteorCrater");
        }

        // Telegraph (Einheitsradius 1, wird auf ImpactRadius skaliert, lebt FallTime = 0,9 s): fester Ring,
        // drei nach innen laufende Pulsringe, rötliche Füllung, Zielpunkt
        internal static GameObject BuildMeteorTelegraph()
        {
            var root = new GameObject("VFX_MeteorTelegraph");
            var t = root.transform;
            var ring = PS(t, "Ring", new Vector3(0f, 0.3f, 0f), RingAdd, new Vector2(1.0f, 1.0f), new Vector2(2.1f, 2.1f), new Color(1f, 0.42f, 0.08f, 0.95f), 2, false);
            Burst(ring, 1); Flat(ring); ColorLife(ring, Fade(Color.white, Color.white, 1f, 0.08f, 0.95f));
            var pulse = PS(t, "Pulse", new Vector3(0f, 0.31f, 0f), RingAdd, new Vector2(0.32f, 0.32f), new Vector2(2.1f, 2.1f), new Color(1f, 0.6f, 0.2f, 0.9f), 4, false);
            var em = pulse.emission;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 1), new ParticleSystem.Burst(0.3f, 1), new ParticleSystem.Burst(0.6f, 1) });
            Flat(pulse); SizeLife(pulse, 1f, 0.15f); ColorLife(pulse, Fade(Color.white, Color.white, 1f, 0.2f, 0.7f));
            var fill = PS(t, "Fill", new Vector3(0f, 0.29f, 0f), SoftAdd, new Vector2(1.0f, 1.0f), new Vector2(2.4f, 2.4f), new Color(1f, 0.25f, 0.05f, 0.45f), 2, false);
            Burst(fill, 1); Flat(fill); SizeLife(fill, 0.6f, 1f); ColorLife(fill, Fade(Color.white, Color.white, 1f, 0.3f, 0.95f));
            var dot = PS(t, "Center", new Vector3(0f, 0.32f, 0f), SoftAdd, new Vector2(1.0f, 1.0f), new Vector2(0.35f, 0.35f), new Color(1f, 0.8f, 0.4f), 2, false);
            Burst(dot, 1); Flat(dot); SizeLife(dot, 0.5f, 1.4f);
            return SaveFx(root, "VFX_MeteorTelegraph");
        }

        // =====================================================================================================
        // Sturmfürst (Feuer + Eis + Licht)
        // =====================================================================================================

        internal const string StormDir = ModelDir + "Storm/";
        public static readonly Color StormCyan = new Color(0.45f, 0.9f, 1f);
        public static readonly Color StormBolt = new Color(0.75f, 0.93f, 1f);
        // Modell: ~2,3 m (schwebt ab 0,14 m über seinem Pivot) → ×1,15, 0,45 über dem Boden ≈ 3,2 m Oberkante (Titan ≈ 3,2 m)
        public const float StormScale = 1.15f;
        public const float StormSockelHeight = 0.45f;
        // Cast-Clip 1,2 s, Freigabe bei 0,6 s; State-Speed 1,5 → Freigabe 0,4 s nach dem Trigger (Wolke quillt 0,6 s auf)
        public const float StormCastSpeed = 1.5f;

        [MenuItem("BuddyTD/Super-Elementare/Sturmfürst bauen")]
        public static void BuildStormLord()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("SuperBuilder: nur im Edit-Mode bauen.");
                return;
            }
            Folders();
            Folder(ModelDir.TrimEnd('/'), "Storm");
            string matDir = StormDir + "Materials/";
            Folder(StormDir.TrimEnd('/'), "Materials");
            var glow = new System.Collections.Generic.Dictionary<string, float>
            {
                { "Storm_Core_Glow", 2.2f }, { "Storm_Eye_Glow", 3.0f }, { "Storm_Lightning_Glow", 2.4f },
                { "Storm_Ember_Glow", 2.2f }, { "Storm_Hail_Glow", 1.3f }, { "Storm_Seam_Glow", 1.8f }
            };
            ImportCharacter(StormDir + "StormLord.fbx", matDir, glow, "Storm_Idle");
            ImportProp(StormDir + "Storm_Cloud.fbx", matDir, glow);
            ImportProp(StormDir + "Storm_Bolt.fbx", matDir, glow);

            var ctrl = BuildController(AnimDir + "StormLord.controller", Clip(StormDir + "StormLord.fbx", "Storm_Idle"),
                Clip(StormDir + "StormLord.fbx", "Storm_Cast"), StormCastSpeed, Clip(StormDir + "StormLord.fbx", "Storm_Hit"));

            var boltMat = ParticleMat(matDir + "Storm_BoltLine_Add.mat", VfxMat + "VFX_Trail_Add.mat", new Color(1.6f, 2.0f, 2.4f));
            var boltMeshMat = ParticleMat(matDir + "Storm_BoltMesh_Add.mat", null, new Color(1.8f, 2.1f, 2.5f));
            var cloud = BuildStormCloud();
            var strike = BuildStormStrike(boltMeshMat);
            var wet = AssetDatabase.LoadAssetAtPath<GameObject>(VfxPrefabs + "VFX_Wet.prefab");

            var config = AssetDatabase.LoadAssetAtPath<UnitConfigSO>(ConfigDir + "Super_Sturm.asset");
            var prefab = BuildStormPrefab(ctrl, config, cloud, strike, wet, boltMat);
            AssignConfig(config, prefab);

            string icon = IconDir + "portrait_super_sturm.png";
            RenderPortrait(prefab, icon, new Vector3(0f, 2.6f, -5.6f), new Vector3(0f, 1.75f, -0.1f), 30f);
            WireRecipeIcon(FusionElement.StormLord, icon);
            AssetDatabase.SaveAssets();
            Debug.Log("SuperBuilder: Sturmfürst gebaut (" + PrefabDir + "Super_Sturm.prefab).");
        }

        // ---------------- Sturmfürst: Buddy-Prefab ----------------

        internal static GameObject BuildStormPrefab(AnimatorController ctrl, UnitConfigSO config, GameObject cloud, GameObject strike,
            GameObject wet, Material boltMat)
        {
            var root = NewBuddyRoot("Super_Sturm");
            var cap = root.AddComponent<CapsuleCollider>();
            cap.center = new Vector3(0f, 1.65f, 0f);
            cap.radius = 0.85f;
            cap.height = 2.9f;

            // Kleinerer, verwitterter Sockel: dunkler Stein, Grünspan-Kupfer, cyanfarbene Runen, nasser dunkler Belag
            string mats = StormDir + "Materials/";
            Sockel(root.transform, new Vector3(1.5f, 1.1f, 1.5f), new[]
            {
                LitMaterial(mats + "Storm_SockelStone.mat", new Color(0.33f, 0.36f, 0.42f), 0.45f),
                LitMaterial(mats + "Storm_Verdigris.mat", new Color(0.2f, 0.38f, 0.36f), 0.35f),
                GlowMaterial(mats + "Storm_Sockel_Rune.mat", new Color(0.2f, 0.5f, 0.62f), 0.35f),
                LitMaterial(mats + "Storm_SockelWet.mat", new Color(0.16f, 0.2f, 0.27f), 0.7f)
            });

            var visual = AddModel(root.transform, StormDir + "StormLord.fbx", "Visual", new Vector3(0f, StormSockelHeight, 0f), StormScale, ctrl);
            // Leichtes zusätzliches Schweben/Pendeln (nur Visual; Animator bewegt nur die Knochen)
            var fc = visual.gameObject.AddComponent<FloatingCreature>();
            fc.BobAmplitude = 0.08f; fc.BobFrequency = 0.45f;
            fc.Motion = FloatingCreature.IdleMotion.Sway; fc.SwayAngle = 5f; fc.SwayFrequency = 0.12f;
            fc.BreathAmount = 0.012f; fc.BreathFrequency = 0.33f;
            fc.PunchScale = 1.06f; fc.PunchDuration = 0.35f; fc.LungeDistance = 0f;

            var spawn = Deep(visual, "CloudSpawn");
            var tail = Deep(visual, "Tail3");
            var fp = new GameObject("FirePoint").transform;
            fp.SetParent(root.transform, false);
            fp.position = spawn != null ? spawn.position : new Vector3(0f, 2.3f, -1.2f);

            var glowLight = PointLight(root.transform, "Glow", new Vector3(0f, 2.5f, -0.4f), StormCyan, 0.9f, 5.5f);

            if (spawn != null) StormCastFxAt(spawn);
            var fx = new GameObject("ElementFX").transform;
            fx.SetParent(root.transform, false);
            StormElementFx(fx, glowLight, boltMat);
            // Wind um den Wolkenschweif (folgt dem Schweif-Knochen)
            if (tail != null) StormTailFx(tail);

            var buddy = root.AddComponent<StormLordBuddy>();
            buddy.Config = config;
            buddy.Element = FusionElement.StormLord;
            buddy.BaseMaxHP = 220f;
            buddy.Visual = visual;
            buddy.FirePoint = fp;
            buddy.StormCloudPrefab = cloud;
            buddy.StrikeEffectPrefab = strike;
            buddy.WetVfxPrefab = wet;
            buddy.BoltMaterial = boltMat;
            buddy.BoltColor = StormBolt;
            return SavePrefab(root, PrefabDir + "Super_Sturm.prefab");
        }

        // Freigabe-Blitz über den Händen (CloudSpawn): Lichtblitz, Funken nach oben, kleine Wolkenwirbel
        internal static void StormCastFxAt(Transform spawn)
        {
            var holder = FxAnchor(spawn, "FX_Cast");
            var cast = holder.gameObject.AddComponent<SuperCastFx>();
            cast.Delay = 0.6f / StormCastSpeed;
            var flash = PS(holder, "Flash", Vector3.zero, SoftAdd, new Vector2(0.22f, 0.28f), new Vector2(2.0f, 2.4f), StormBolt, 2, false);
            Burst(flash, 1); NoAwake(flash);
            SizeLife(flash, 0.4f, 1.2f); ColorLife(flash, Fade(Color.white, Color.white, 1f, 0.05f, 0.4f));
            LightModule(flash, 4f, 7f);
            var streak = PS(holder, "UpSparks", Vector3.zero, SparkAdd, new Vector2(0.3f, 0.55f), new Vector2(0.08f, 0.14f), StormBolt, 30, false);
            Burst(streak, 20); NoAwake(streak); WorldSim(streak); Cone(streak, 25f, 0.2f);
            var ms = streak.main; ms.startSpeed = new ParticleSystem.MinMaxCurve(8f, 14f);
            Stretch(streak, 0.06f, 2f); ColorLife(streak, Fade(Color.white, StormCyan, 1f, 0.02f, 0.5f));
            var puff = PS(holder, "Puffs", Vector3.zero, SmokeAlpha, new Vector2(0.6f, 0.9f), new Vector2(0.5f, 0.8f), new Color(0.42f, 0.48f, 0.6f, 0.8f), 12, false);
            Burst(puff, 8); NoAwake(puff); WorldSim(puff); Sphere(puff, 0.3f);
            var mp = puff.main; mp.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2.5f);
            Drag(puff, 3f); Spin(puff, 90f); SizeLife(puff, 0.7f, 1.6f); ColorLife(puff, Fade(Color.white, Color.white, 0.9f, 0.1f, 0.4f));
        }

        // Windschlieren um den Schweif + Nebelfetzen, die vom Schweifende abreißen
        internal static void StormTailFx(Transform tailBone)
        {
            var a = FxAnchor(tailBone, "FX_Tail");
            var wind = PS(a, "WindStreaks", new Vector3(0f, 0.1f, 0f), SoftAlpha, new Vector2(1.2f, 1.5f), new Vector2(0.02f, 0.03f),
                new Color(0.88f, 0.95f, 1f, 0.8f), 14, true);
            Rate(wind, 7f); Ring(wind, 0.55f); Orbit(wind, 4.5f, 0.55f, 0.05f);
            Trails(wind, TrailAlpha, 0.45f, 0.06f, new Color(0.85f, 0.94f, 1f));
            ColorLife(wind, Fade(Color.white, Color.white, 0.8f));
            var mist = PS(a, "Mist", Vector3.zero, SmokeAlpha, new Vector2(1.6f, 2.2f), new Vector2(0.45f, 0.7f), new Color(0.5f, 0.56f, 0.68f, 0.55f), 10, true);
            Rate(mist, 3.5f); WorldSim(mist); Sphere(mist, 0.25f); WorldRise(mist, -0.15f, 0.1f); Noise(mist, 0.3f, 0.5f); Spin(mist, 30f);
            SizeLife(mist, 0.6f, 1.6f); ColorLife(mist, Fade(Color.white, Color.white, 0.8f, 0.2f, 0.5f));
        }

        // Umgebung: Glyphenkreis auf dem Sockel, Nieselregen aus dem Körper, Funken, gelegentliche Lichtbögen (flackert das Licht)
        internal static void StormElementFx(Transform fx, Light glowLight, Material boltMat)
        {
            var ring = PS(fx, "GlyphRing", new Vector3(0f, 0.3f, 0f), RingAdd, new Vector2(3f, 3f), new Vector2(2.6f, 2.6f),
                new Color(0.4f, 0.85f, 1f, 0.25f), 3, true);
            Rate(ring, 0.6f); Flat(ring); Big(ring); SizeLife(ring, 0.95f, 1.08f); ColorLife(ring, Fade(Color.white, Color.white, 1f, 0.3f, 0.6f));
            var glow = PS(fx, "GlyphGlow", new Vector3(0f, 0.29f, 0f), SoftAdd, new Vector2(2.2f, 2.6f), new Vector2(2.2f, 2.5f),
                new Color(0.3f, 0.75f, 1f, 0.07f), 4, true);
            Rate(glow, 1f); Flat(glow); Big(glow); ColorLife(glow, Fade(Color.white, Color.white, 1f, 0.3f, 0.6f));

            var rain = PS(fx, "Drizzle", new Vector3(0f, 1.85f, 0f), SoftAlpha, new Vector2(0.38f, 0.5f), new Vector2(0.035f, 0.05f),
                new Color(0.72f, 0.85f, 1f, 0.7f), 30, true);
            Rate(rain, 22f); Sphere(rain, 0.8f, new Vector3(1.3f, 0.35f, 1.0f), 1f);
            Rise(rain, -4.2f, -3.6f); Stretch(rain, 0.08f, 1.5f);
            ColorLife(rain, Fade(Color.white, Color.white, 1f, 0.1f, 0.8f));

            var sp = PS(fx, "Sparks", new Vector3(0f, 1.95f, 0f), SparkAdd, new Vector2(0.15f, 0.3f), new Vector2(0.05f, 0.09f), StormBolt, 30, true);
            Sphere(sp, 0.95f, new Vector3(1.2f, 1.1f, 1f), 0.2f);
            var m = sp.main; m.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3f); m.gravityModifier = 0.5f;
            var em = sp.emission;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 3, 6, 20, 0.45f) { probability = 0.5f } });
            Stretch(sp, 0.05f, 2.5f);
            var st = PS(fx, "Static", new Vector3(0f, 1.95f, 0f), SoftAdd, new Vector2(0.6f, 1.0f), new Vector2(0.05f, 0.09f), StormCyan, 16, true);
            Rate(st, 8f); Sphere(st, 1.05f, new Vector3(1.2f, 1.1f, 1f), 0.15f); Noise(st, 0.8f, 2f);
            ColorLife(st, Fade(Color.white, Color.white));

            var arcs = new GameObject("Arcs"); arcs.transform.SetParent(fx, false);
            var ea = arcs.AddComponent<ElectricArcs>();
            ea.ArcMaterial = boltMat;
            ea.ArcColor = StormBolt;
            ea.ArcCount = 3; ea.Width = 0.05f;
            ea.HullCenter = new Vector3(0f, 1.95f, 0f);
            ea.HullRadius = new Vector3(1.2f, 1.0f, 1.0f);
            ea.PauseRange = new Vector2(0.25f, 1.4f);
            ea.FlickerLight = glowLight;
        }

        // ---------------- Sturmfürst: Effekte ----------------

        // Sturmwolke: StormCloud setzt die Wurzel auf Boden + CloudHeight (5 m) und skaliert sie in den letzten 0,5 s auf 0.
        // Storm_Cloud-Mesh (8 × 7 m ≈ Radius 4) quillt auf (StormCloudFx), Regen bis zum Boden, innere Blitze mit Licht,
        // Bodenteil (Schattenscheibe, Bereichsring r 4, Spritzer) lebt 6 s und blendet selbst ein/aus.
        internal static GameObject BuildStormCloud()
        {
            const float height = 5f, life = 6f, radius = 4f;
            var root = new GameObject("VFX_StormCloud");
            var t = root.transform;
            var body = new GameObject("Body").transform;
            body.SetParent(t, false);
            AddModel(body, StormDir + "Storm_Cloud.fbx", "CloudMesh", Vector3.zero, 1f, null);
            var flashes = PS(body, "InnerFlashes", new Vector3(0f, 0.7f, 0f), SoftAdd, new Vector2(0.12f, 0.2f), new Vector2(2.5f, 3.8f),
                new Color(0.7f, 0.9f, 1f, 0.9f), 4, true);
            var fem = flashes.emission; fem.rateOverTime = 0f;
            fem.SetBursts(new[] { new ParticleSystem.Burst(0f, 1, 1, 30, 0.35f) { probability = 0.55f } });
            Sphere(flashes, 1f, new Vector3(3f, 0.5f, 2.6f), 1f);
            ColorLife(flashes, Fade(Color.white, Color.white, 1f, 0.05f, 0.4f));
            LightModule(flashes, 5f, 10f); Big(flashes);
            var wisps = PS(body, "Wisps", new Vector3(0f, 0.2f, 0f), SmokeAlpha, new Vector2(1.6f, 2.2f), new Vector2(1.4f, 2.2f),
                new Color(0.25f, 0.29f, 0.38f, 0.7f), 16, true);
            Rate(wisps, 6f); Big(wisps); Ring(wisps, 3.6f, 0.3f); Spin(wisps, 25f); Noise(wisps, 0.3f, 0.4f);
            SizeLife(wisps, 0.7f, 1.3f); ColorLife(wisps, Fade(Color.white, Color.white, 0.9f, 0.25f, 0.6f));

            var rain = PS(t, "Rain", new Vector3(0f, 0.1f, 0f), SoftAlpha, new Vector2(0.36f, 0.43f), new Vector2(0.045f, 0.06f),
                new Color(0.7f, 0.82f, 1f, 0.75f), 140, true);
            var mr = rain.main; mr.prewarm = false; mr.startDelay = 0.25f;
            Rate(rain, 300f); Ring(rain, 3.6f, 1f);
            Rise(rain, -14.5f, -14.5f); Stretch(rain, 0.07f, 1f);
            ColorLife(rain, Fade(Color.white, Color.white, 1f, 0.05f, 0.9f));

            var ground = new GameObject("Ground").transform;
            ground.SetParent(t, false);
            ground.localPosition = new Vector3(0f, -height, 0f);
            // Soft-Dot-Textur hat einen kleinen Kern → Partikel ≈ 4 × Radius; zwei übereinander für genug Deckkraft
            var shadow = PS(ground, "Shadow", new Vector3(0f, 0.07f, 0f), SoftAlpha, new Vector2(life, life), new Vector2(radius * 4f, radius * 4f),
                new Color(0.02f, 0.03f, 0.07f, 0.9f), 3, false);
            Burst(shadow, 2); Flat(shadow); Big(shadow); ColorLife(shadow, Fade(Color.white, Color.white, 1f, 0.08f, 0.9f));
            var area = PS(ground, "AreaRing", new Vector3(0f, 0.09f, 0f), RingAdd, new Vector2(life, life), new Vector2(radius * 4.2f, radius * 4.2f),
                new Color(0.4f, 0.8f, 1f, 0.45f), 2, false);
            Burst(area, 1); Flat(area); Big(area); ColorLife(area, Fade(Color.white, Color.white, 1f, 0.06f, 0.9f));
            var splash = PS(ground, "Splashes", new Vector3(0f, 0.1f, 0f), RingAdd, new Vector2(0.25f, 0.35f), new Vector2(0.25f, 0.4f),
                new Color(0.75f, 0.88f, 1f, 0.6f), 40, true);
            var msp = splash.main; msp.prewarm = false; msp.startDelay = 0.55f;
            Rate(splash, 45f); Ring(splash, 3.6f, 1f); Flat(splash); SizeLife(splash, 0.4f, 1.4f);
            ColorLife(splash, Fade(Color.white, Color.white, 1f, 0.05f, 0.4f));
            var mistG = PS(ground, "GroundMist", new Vector3(0f, 0.3f, 0f), SmokeAlpha, new Vector2(2f, 2.6f), new Vector2(1.4f, 2.2f),
                new Color(0.55f, 0.62f, 0.75f, 0.35f), 14, true);
            var mmg = mistG.main; mmg.prewarm = false; mmg.startDelay = 0.6f;
            Rate(mistG, 5f); Ring(mistG, 3.2f, 1f); Spin(mistG, 20f); SizeLife(mistG, 0.6f, 1.4f);
            ColorLife(mistG, Fade(Color.white, Color.white, 1f, 0.3f, 0.6f));

            var fxc = root.AddComponent<StormCloudFx>();
            fxc.Body = body; fxc.Ground = ground; fxc.Height = height;
            return SaveFx(root, "VFX_StormCloud");
        }

        // Einschlag (am Gegner instanziert, lebt 1 s): Blitz-Mesh von der Wolke (5 m) herab, Lichtblitz, Funkenregen,
        // Druckring, Brandfleck, Rauchwölkchen. Die Kettenbögen zeichnet StormCloud als LineRenderer (BoltMaterial).
        internal static GameObject BuildStormStrike(Material boltMeshMat)
        {
            var root = new GameObject("VFX_StormStrike");
            var t = root.transform;
            var bolt = PS(t, "Bolt", new Vector3(0f, 5.1f, 0f), boltMeshMat, new Vector2(0.16f, 0.2f), new Vector2(0.82f, 0.82f), StormBolt, 3, false);
            var bem = bolt.emission;
            bem.SetBursts(new[] { new ParticleSystem.Burst(0f, 1), new ParticleSystem.Burst(0.09f, 1) });
            var br = bolt.GetComponent<ParticleSystemRenderer>();
            br.renderMode = ParticleSystemRenderMode.Mesh;
            br.mesh = SingleSubmesh(StormDir + "Storm_Bolt.fbx", StormDir + "Storm_BoltMesh.asset");
            br.alignment = ParticleSystemRenderSpace.Local;
            var mb = bolt.main; mb.startRotation3D = true;
            mb.startRotationX = 0f; mb.startRotationZ = 0f;
            mb.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            ColorLife(bolt, Fade(Color.white, StormCyan, 1f, 0.02f, 0.45f));
            Big(bolt);

            // Bodenteil: die Wurzel liegt am Gegner-Pivot (Läufer 1 m über dem Boden) → SnapToGround setzt ihn auf den Boden
            var g = new GameObject("Ground").transform;
            g.SetParent(t, false);
            var snap = root.AddComponent<SnapToGround>();
            snap.Targets = new[] { g };

            var flash = PS(g, "Flash", new Vector3(0f, 0.8f, 0f), SoftAdd, new Vector2(0.18f, 0.22f), new Vector2(3.2f, 3.6f), StormBolt, 2, false);
            Burst(flash, 1); SizeLife(flash, 0.5f, 1.2f); ColorLife(flash, Fade(Color.white, Color.white, 1f, 0.02f, 0.3f));
            LightModule(flash, 7f, 9f); Big(flash);
            var sparks = PS(g, "Sparks", new Vector3(0f, 0.2f, 0f), SparkAdd, new Vector2(0.3f, 0.55f), new Vector2(0.06f, 0.11f), StormBolt, 30, false);
            Burst(sparks, 22); Hemisphere(sparks, 0.3f);
            var ms = sparks.main; ms.startSpeed = new ParticleSystem.MinMaxCurve(4f, 8f); ms.gravityModifier = 1.5f;
            Stretch(sparks, 0.05f, 2f);
            ColorLife(sparks, Fade(Color.white, new Color(1f, 0.85f, 0.4f), 1f, 0.02f, 0.6f));
            var wave = PS(g, "Ring", new Vector3(0f, 0.12f, 0f), RingAdd, new Vector2(0.3f, 0.3f), new Vector2(1f, 1f), StormCyan, 2, false);
            Burst(wave, 1); Flat(wave); SizeLife(wave, 0.4f, 2.8f); ColorLife(wave, Fade(Color.white, Color.white, 1f, 0.02f, 0.4f)); Big(wave);
            var scorch = PS(g, "Scorch", new Vector3(0f, 0.08f, 0f), SoftAlpha, new Vector2(0.9f, 0.9f), new Vector2(1.5f, 1.8f), new Color(0.05f, 0.05f, 0.07f, 0.6f), 2, false);
            Burst(scorch, 1); Flat(scorch); Big(scorch); ColorLife(scorch, Fade(Color.white, Color.white, 1f, 0.05f, 0.5f));
            var smoke = PS(g, "Smoke", new Vector3(0f, 0.3f, 0f), SmokeAlpha, new Vector2(0.6f, 0.9f), new Vector2(0.5f, 0.8f), new Color(0.4f, 0.42f, 0.48f, 0.7f), 8, false);
            Burst(smoke, 5); Sphere(smoke, 0.3f); Rise(smoke, 0.8f, 1.5f); Spin(smoke, 60f); SizeLife(smoke, 0.6f, 1.5f);
            ColorLife(smoke, Fade(Color.white, Color.white, 0.8f, 0.1f, 0.4f));
            return SaveFx(root, "VFX_StormStrike");
        }

        // =====================================================================================================
        // Phönix (Feuer + Erde + Licht)
        // =====================================================================================================

        internal const string PhoenixDir = ModelDir + "Phoenix/";
        public static readonly Color PhoenixGold = new Color(1f, 0.78f, 0.3f);
        public static readonly Color PhoenixFire = new Color(1f, 0.45f, 0.1f);
        // Stange (Phoenix_Perch) ist 1,6 m hoch; der Vogel (1,95 m inkl. Halo) sitzt im Nest → ×1,1 → Oberkante ≈ 3,75 m
        public const float PhoenixPerchTop = 1.6f;
        public const float PhoenixScale = 1.1f;
        // Cast-Clip 0,6 s (Flügel auf + Absprung, endet in der Flugpose); Speed 1,5 → 0,4 s, danach Fly-Loop bis zur Rückkehr
        public const float PhoenixCastSpeed = 1.5f;

        [MenuItem("BuddyTD/Super-Elementare/Phönix bauen")]
        public static void BuildPhoenix()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("SuperBuilder: nur im Edit-Mode bauen.");
                return;
            }
            Folders();
            Folder(ModelDir.TrimEnd('/'), "Phoenix");
            string matDir = PhoenixDir + "Materials/";
            Folder(PhoenixDir.TrimEnd('/'), "Materials");
            var glow = new System.Collections.Generic.Dictionary<string, float>
            {
                { "Phoenix_Feather_Gold_Glow", 1.6f }, { "Phoenix_Flame_Glow", 2.2f }, { "Phoenix_Eye_Glow", 2.6f },
                { "Phoenix_Halo_Glow", 2.0f }, { "Phoenix_Ember_Glow", 2.2f }, { "Phoenix_Rune_Glow", 1.8f }
            };
            ImportCharacter(PhoenixDir + "Phoenix.fbx", matDir, glow, "Phoenix_Idle", "Phoenix_Fly");
            ImportProp(PhoenixDir + "Phoenix_Perch.fbx", matDir, glow);
            ImportProp(PhoenixDir + "Phoenix_Egg.fbx", matDir, glow);
            ImportProp(PhoenixDir + "Phoenix_Feather.fbx", matDir, glow);

            var ctrl = BuildPhoenixController(AnimDir + "Phoenix.controller", PhoenixDir + "Phoenix.fbx");
            var featherMesh = SingleSubmesh(PhoenixDir + "Phoenix_Feather.fbx", PhoenixDir + "Phoenix_FeatherMesh.asset");
            var featherMat = ParticleMat(matDir + "Phoenix_FeatherMesh_Add.mat", null, new Color(2.2f, 1.15f, 0.35f));

            var trail = BuildPhoenixFireTrail();
            var hit = BuildPhoenixDiveHit(featherMesh, featherMat);
            var rebirth = BuildPhoenixRebirth(featherMesh, featherMat);
            var wait = BuildPhoenixRebirthWait();
            var burn = AssetDatabase.LoadAssetAtPath<GameObject>(VfxPrefabs + "VFX_Burning.prefab");

            var config = AssetDatabase.LoadAssetAtPath<UnitConfigSO>(ConfigDir + "Super_Phoenix.asset");
            var prefab = BuildPhoenixPrefab(ctrl, config, trail, hit, rebirth, wait, burn, featherMesh, featherMat);
            AssignConfig(config, prefab);

            string icon = IconDir + "portrait_super_phoenix.png";
            RenderPortrait(prefab, icon, new Vector3(0f, 3.0f, -5.2f), new Vector3(0f, 2.5f, 0f), 32f);
            WireRecipeIcon(FusionElement.Phoenix, icon);
            AssetDatabase.SaveAssets();
            Debug.Log("SuperBuilder: Phönix gebaut (" + PrefabDir + "Super_Phoenix.prefab).");
        }

        // Idle (Loop) → Cast (Trigger, Speed 1,5) → Fly (Loop, solange Bool "Flying") → Idle; Rebirth per Trigger aus jedem
        // Zustand; Hit per Trigger. "Flying" setzt PhoenixVisualFx aus PhoenixBuddy.IsDiving.
        internal static AnimatorController BuildPhoenixController(string path, string fbx)
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (ac == null) ac = AnimatorController.CreateAnimatorControllerAtPath(path);
            var sm = ac.layers[0].stateMachine;
            foreach (var t in sm.anyStateTransitions) sm.RemoveAnyStateTransition(t);
            foreach (var s in sm.states) sm.RemoveState(s.state);
            while (ac.parameters.Length > 0) ac.RemoveParameter(0);
            ac.AddParameter("Cast", AnimatorControllerParameterType.Trigger);
            ac.AddParameter("Flying", AnimatorControllerParameterType.Bool);
            ac.AddParameter("Rebirth", AnimatorControllerParameterType.Trigger);
            ac.AddParameter("Hit", AnimatorControllerParameterType.Trigger);

            var idle = sm.AddState("Idle", new Vector3(300, 0, 0));
            idle.motion = Clip(fbx, "Phoenix_Idle");
            sm.defaultState = idle;
            var cast = sm.AddState("Cast", new Vector3(300, 120, 0));
            cast.motion = Clip(fbx, "Phoenix_Cast");
            cast.speed = PhoenixCastSpeed;
            var fly = sm.AddState("Fly", new Vector3(550, 120, 0));
            fly.motion = Clip(fbx, "Phoenix_Fly");
            var reb = sm.AddState("Rebirth", new Vector3(50, 120, 0));
            reb.motion = Clip(fbx, "Phoenix_Rebirth");
            var hit = sm.AddState("Hit", new Vector3(550, 0, 0));
            hit.motion = Clip(fbx, "Phoenix_Hit");

            var t1 = idle.AddTransition(cast);
            t1.AddCondition(AnimatorConditionMode.If, 0f, "Cast");
            t1.hasExitTime = false; t1.duration = 0.06f;
            var t2 = cast.AddTransition(fly);
            t2.AddCondition(AnimatorConditionMode.If, 0f, "Flying");
            t2.hasExitTime = true; t2.exitTime = 0.9f; t2.duration = 0.12f;
            var t3 = cast.AddTransition(idle);
            t3.AddCondition(AnimatorConditionMode.IfNot, 0f, "Flying");
            t3.hasExitTime = true; t3.exitTime = 0.95f; t3.duration = 0.2f;
            var t4 = fly.AddTransition(idle);
            t4.AddCondition(AnimatorConditionMode.IfNot, 0f, "Flying");
            t4.hasExitTime = false; t4.duration = 0.25f;
            var t5 = sm.AddAnyStateTransition(reb);
            t5.AddCondition(AnimatorConditionMode.If, 0f, "Rebirth");
            t5.hasExitTime = false; t5.duration = 0f; t5.canTransitionToSelf = false;
            var t6 = reb.AddTransition(idle);
            t6.hasExitTime = true; t6.exitTime = 0.95f; t6.duration = 0.15f;
            var t7 = idle.AddTransition(hit);
            t7.AddCondition(AnimatorConditionMode.If, 0f, "Hit");
            t7.hasExitTime = false; t7.duration = 0.05f;
            var t8 = hit.AddTransition(idle);
            t8.hasExitTime = true; t8.exitTime = 0.9f; t8.duration = 0.1f;
            EditorUtility.SetDirty(ac);
            return ac;
        }

        // ---------------- Phönix: Buddy-Prefab ----------------

        // Root (Collider, Slot) bleibt stehen; "Perch" (Steinsäule mit Nest) ist ein eigenes Kind, "Visual" (der Vogel) fliegt
        // den Sturzflug. Visual trägt die 180°-Drehung (Ruhe: Blick zur Kamera), das Modell darunter schaut entlang Visual +Z,
        // weil PhoenixBuddy das Visual im Flug per LookRotation in Flugrichtung dreht.
        internal static GameObject BuildPhoenixPrefab(AnimatorController ctrl, UnitConfigSO config, GameObject trail, GameObject hit,
            GameObject rebirth, GameObject wait, GameObject burn, Mesh featherMesh, Material featherMat)
        {
            var root = NewBuddyRoot("Super_Phoenix");
            // Kapsel um Säule + sitzenden Vogel (Säule ≈ 0,8 m breit, Schutt-Fuß breiter → nicht ganz umschlossen)
            var cap = root.AddComponent<CapsuleCollider>();
            cap.center = new Vector3(0f, 1.75f, 0f);
            cap.radius = 0.65f;
            cap.height = 3.5f;

            var perch = AddModel(root.transform, PhoenixDir + "Phoenix_Perch.fbx", "Perch", Vector3.zero, 1f, null);
            perch.localRotation = Quaternion.Euler(0f, 180f, 0f) * perch.localRotation; // Runen zur Kamera wie der Vogel

            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            visual.localPosition = new Vector3(0f, PhoenixPerchTop, 0f);
            visual.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var model = AddModel(visual, PhoenixDir + "Phoenix.fbx", "Model", Vector3.zero, PhoenixScale, ctrl);
            model.localRotation = Quaternion.identity; // AddModels 180° trägt hier das Visual

            var fp = new GameObject("FirePoint").transform;
            fp.SetParent(root.transform, false);
            fp.localPosition = new Vector3(0f, 2.5f, 0f);

            PointLight(root.transform, "Glow", new Vector3(0f, 2.1f, -0.3f), new Color(1f, 0.62f, 0.28f), 1.4f, 5.5f);

            var vfx = visual.gameObject.AddComponent<PhoenixVisualFx>();
            var diveTrails = new System.Collections.Generic.List<TrailRenderer>();
            var diveParticles = new System.Collections.Generic.List<ParticleSystem>();
            var tail = Deep(model, "Tail3");
            if (tail != null) PhoenixTailFx(tail, diveTrails, diveParticles);
            foreach (var tipName in new[] { "Wing_Tip_L", "Wing_Tip_R" })
            {
                var tip = Deep(model, tipName);
                if (tip != null) diveTrails.Add(PhoenixWingTrail(tip));
            }
            var halo = Deep(model, "Halo");
            if (halo != null) PhoenixHaloFx(halo);
            vfx.Animator = model.GetComponent<Animator>();
            vfx.DiveTrails = diveTrails.ToArray();
            vfx.DiveParticles = diveParticles.ToArray();
            vfx.RebirthParticles = new[] { PhoenixSelfRebirthBurst(visual) };

            var fx = new GameObject("ElementFX").transform;
            fx.SetParent(root.transform, false);
            PhoenixElementFx(fx, featherMesh, featherMat);

            var buddy = root.AddComponent<PhoenixBuddy>();
            buddy.Config = config;
            buddy.Element = FusionElement.Phoenix;
            buddy.Visual = visual;
            buddy.FirePoint = fp;
            buddy.FireTrailPrefab = trail;
            buddy.RebirthEffectPrefab = rebirth;
            buddy.RebirthWaitPrefab = wait;
            buddy.DiveHitVfxPrefab = hit;
            buddy.BurnVfxPrefab = burn;
            vfx.Owner = buddy;
            return SavePrefab(root, PrefabDir + "Super_Phoenix.prefab");
        }

        // Flammenschweif am Schwanz: immer leichtes Züngeln; im Sturzflug TrailRenderer + Flammen/Glut in Weltkoordinaten
        internal static void PhoenixTailFx(Transform tailBone, System.Collections.Generic.List<TrailRenderer> trails,
            System.Collections.Generic.List<ParticleSystem> dive)
        {
            var a = FxAnchor(tailBone, "FX_Tail");
            var lick = PS(a, "TailFlicker", Vector3.zero, FlameAdd, new Vector2(0.35f, 0.5f), new Vector2(0.22f, 0.34f), PhoenixFire, 16, true);
            Rate(lick, 9f); Sphere(lick, 0.12f); Rise(lick, 0.3f, 0.7f); Spin(lick, 120f); SizeLife(lick, 1f, 0.3f); ColorLife(lick, Heat());

            var trGo = new GameObject("FlameTrail");
            trGo.transform.SetParent(a, false);
            var tr = trGo.AddComponent<TrailRenderer>();
            tr.sharedMaterial = Load<Material>(VfxMat + "VFX_LavaTrail.mat");
            tr.time = 0.45f; tr.minVertexDistance = 0.12f;
            tr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 0f));
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.9f, 0.5f), 0f), new GradientColorKey(new Color(1f, 0.42f, 0.08f), 0.45f), new GradientColorKey(new Color(0.5f, 0.08f, 0.02f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
            tr.colorGradient = g;
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.emitting = false;
            trails.Add(tr);

            var flames = PS(a, "DiveFlames", Vector3.zero, FlameAdd, new Vector2(0.35f, 0.55f), new Vector2(0.5f, 0.85f), PhoenixFire, 80, true);
            Rate(flames, 70f); WorldSim(flames); Sphere(flames, 0.2f); Spin(flames, 160f); SizeLife(flames, 1f, 0.2f); ColorLife(flames, Heat());
            var fm = flames.main; fm.prewarm = false;
            var em = flames.emission; em.enabled = false;
            dive.Add(flames);
            var sparks = PS(a, "DiveEmbers", Vector3.zero, SparkAdd, new Vector2(0.5f, 0.9f), new Vector2(0.06f, 0.12f), PhoenixGold, 60, true);
            Rate(sparks, 40f); WorldSim(sparks); Sphere(sparks, 0.25f);
            var sm = sparks.main; sm.prewarm = false; sm.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2f); sm.gravityModifier = -0.2f;
            ColorLife(sparks, Heat());
            var em2 = sparks.emission; em2.enabled = false;
            dive.Add(sparks);
        }

        internal static TrailRenderer PhoenixWingTrail(Transform tip)
        {
            var a = FxAnchor(tip, "FX_" + tip.name);
            var tr = a.gameObject.AddComponent<TrailRenderer>();
            tr.sharedMaterial = TrailAdd;
            tr.time = 0.22f; tr.minVertexDistance = 0.1f;
            tr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.07f), new Keyframe(1f, 0f));
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.75f, 0.35f), 0f), new GradientColorKey(PhoenixFire, 1f) },
                new[] { new GradientAlphaKey(0.55f, 0f), new GradientAlphaKey(0f, 1f) });
            tr.colorGradient = g;
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.emitting = false;
            return tr;
        }

        // Sonnenschein um den Halo (folgt dem Kopf)
        internal static void PhoenixHaloFx(Transform haloBone)
        {
            var a = FxAnchor(haloBone, "FX_Halo");
            var glow = PS(a, "HaloGlow", Vector3.zero, SoftAdd, new Vector2(1.2f, 1.6f), new Vector2(1.3f, 1.5f), new Color(1f, 0.8f, 0.35f, 0.3f), 4, true);
            Rate(glow, 2f); ColorLife(glow, Fade(Color.white, Color.white, 1f, 0.3f, 0.6f));
            var glint = PS(a, "HaloGlints", Vector3.zero, StarAdd, new Vector2(0.4f, 0.6f), new Vector2(0.14f, 0.22f), new Color(1f, 0.9f, 0.55f), 6, true);
            Rate(glint, 2.5f); Sphere(glint, 0.3f, Vector3.one, 0.2f); SizeBump(glint); Spin(glint, 90f);
        }

        // Eigene Wiedergeburt: Funkenstoß + Lichtblitz am Vogel (PhoenixVisualFx spielt ihn bei PhoenixRebirth.OnReborn ab)
        internal static ParticleSystem PhoenixSelfRebirthBurst(Transform visual)
        {
            var holder = new GameObject("FX_SelfRebirth").transform;
            holder.SetParent(visual, false);
            holder.localPosition = new Vector3(0f, 0.9f, 0f);
            var burst = PS(holder, "Burst", Vector3.zero, FlameAdd, new Vector2(0.4f, 0.7f), new Vector2(0.6f, 1.1f), PhoenixGold, 40, false);
            Burst(burst, 30); NoAwake(burst); WorldSim(burst); Sphere(burst, 0.4f);
            var mb = burst.main; mb.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
            Drag(burst, 3f); Spin(burst, 150f); SizeLife(burst, 1f, 0.2f); ColorLife(burst, Heat());
            var flash = PS(burst.transform, "Flash", Vector3.zero, SoftAdd, new Vector2(0.4f, 0.4f), new Vector2(3.5f, 3.5f), PhoenixGold, 2, false);
            Burst(flash, 1); NoAwake(flash); ColorLife(flash, Fade(Color.white, Color.white, 1f, 0.05f, 0.4f));
            LightModule(flash, 5f, 8f);
            return burst;
        }

        // Umgebung (bleibt an der Stange, auch während der Vogel fliegt): Glutbett + Funken + Hitzeflimmern aus dem Nest,
        // langsam fallende Flammenfedern, Sonnen-Glutring am Boden = Aura +30 % (Radius 8 m; Ring-Textur: Ring bei ≈ Größe/4,2), Sonnenfunkeln auf dem Ring
        internal static void PhoenixElementFx(Transform fx, Mesh featherMesh, Material featherMat)
        {
            const float aura = 8f;
            var ring = PS(fx, "AuraRing", new Vector3(0f, 0.06f, 0f), RingAdd, new Vector2(3f, 3f), new Vector2(aura * 4.2f, aura * 4.2f),
                new Color(1f, 0.72f, 0.28f, 0.16f), 3, true);
            Rate(ring, 0.6f); Flat(ring); Big(ring); SizeLife(ring, 0.97f, 1.02f); ColorLife(ring, Fade(Color.white, Color.white, 1f, 0.3f, 0.6f));
            var motes = PS(fx, "AuraMotes", new Vector3(0f, 0.1f, 0f), StarAdd, new Vector2(1.2f, 1.8f), new Vector2(0.14f, 0.24f),
                new Color(1f, 0.85f, 0.45f, 0.8f), 16, true);
            Rate(motes, 6f); Ring(motes, aura, 0.02f); Rise(motes, 0.2f, 0.5f); SizeBump(motes); Spin(motes, 60f);
            var groundGlow = PS(fx, "SunGlow", new Vector3(0f, 0.05f, 0f), SoftAdd, new Vector2(2.5f, 3f), new Vector2(7f, 8f),
                new Color(1f, 0.65f, 0.25f, 0.12f), 3, true);
            Rate(groundGlow, 1f); Flat(groundGlow); Big(groundGlow); ColorLife(groundGlow, Fade(Color.white, Color.white, 1f, 0.3f, 0.6f));

            var nest = PS(fx, "NestGlow", new Vector3(0f, PhoenixPerchTop - 0.02f, 0f), SoftAdd, new Vector2(1.2f, 1.6f), new Vector2(1.6f, 1.9f),
                new Color(1f, 0.45f, 0.1f, 0.45f), 5, true);
            Rate(nest, 2.5f); Flat(nest); ColorLife(nest, Fade(Color.white, Color.white, 1f, 0.3f, 0.6f));
            var embers = PS(fx, "NestEmbers", new Vector3(0f, PhoenixPerchTop - 0.05f, 0f), SparkAdd, new Vector2(1.2f, 2f), new Vector2(0.05f, 0.1f),
                PhoenixFire, 40, true);
            Rate(embers, 12f); Ring(embers, 0.45f, 0.5f); Rise(embers, 0.6f, 1.4f); Noise(embers, 0.4f, 1.2f); ColorLife(embers, Heat());
            var shimmer = PS(fx, "HeatShimmer", new Vector3(0f, PhoenixPerchTop + 0.1f, 0f), SoftAdd, new Vector2(1.4f, 1.9f), new Vector2(0.7f, 1.1f),
                new Color(1f, 0.55f, 0.2f, 0.07f), 10, true);
            Rate(shimmer, 4f); Ring(shimmer, 0.35f, 1f); Rise(shimmer, 0.5f, 0.9f); Noise(shimmer, 0.3f, 0.8f); SizeLife(shimmer, 0.6f, 1.5f);
            ColorLife(shimmer, Fade(Color.white, Color.white, 1f, 0.25f, 0.5f));

            var feathers = PS(fx, "FallingFeathers", new Vector3(0f, 3.4f, 0f), featherMat, new Vector2(5f, 7f), new Vector2(0.6f, 0.85f), Color.white, 12, true);
            Rate(feathers, 1.2f); Sphere(feathers, 1.6f, new Vector3(1f, 0.25f, 1f), 1f);
            FeatherParticles(feathers, featherMesh, 1.2f);
            Rise(feathers, -0.42f, -0.3f); Noise(feathers, 0.35f, 0.35f);
            ColorLife(feathers, Fade(Color.white, new Color(1f, 0.5f, 0.2f), 1f, 0.1f, 0.75f));
        }

        // Feder-Mesh als Partikel: additiv, langsames Trudeln, kein Schatten
        internal static void FeatherParticles(ParticleSystem ps, Mesh mesh, float spin)
        {
            MeshParticles(ps, mesh);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var rot = ps.rotationOverLifetime;
            rot.x = new ParticleSystem.MinMaxCurve(-spin, spin); rot.y = new ParticleSystem.MinMaxCurve(-spin, spin); rot.z = new ParticleSystem.MinMaxCurve(-spin, spin);
        }

        // Partikel in einem Feuerspur-Prefab: nur die Emissionsform skaliert mit dem Objekt (Breite × Länge), nicht die Partikelgröße
        internal static void TrailShape(ParticleSystem ps, float sx, float sz)
        {
            var m = ps.main; m.scalingMode = ParticleSystemScalingMode.Shape; m.loop = false; m.prewarm = false; m.duration = 3.3f;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            var sh = ps.shape; sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(sx, 0f, sz);
        }

        // ---------------- Phönix: Effekte ----------------

        // Feuerspur (Basis Länge 1 entlang Z, Breite 1; FireTrail skaliert auf 2,5 × 12 m, lebt 4 s, schrumpft zuletzt in X):
        // Brandfleck, glühendes Glutbett mit Licht, Flammen, Glut, Rauch
        internal static GameObject BuildPhoenixFireTrail()
        {
            var root = new GameObject("VFX_PhoenixFireTrail");
            var t = root.transform;
            var scorch = PS(t, "Scorch", new Vector3(0f, 0.04f, 0f), SoftAlpha, new Vector2(4.2f, 4.2f), new Vector2(3.6f, 4.4f),
                new Color(0.05f, 0.03f, 0.02f, 0.95f), 16, false);
            Burst(scorch, 13); Flat(scorch); Big(scorch); TrailShape(scorch, 0.35f, 0.95f);
            ColorLife(scorch, Fade(Color.white, Color.white, 1f, 0.03f, 0.8f));
            var bed = PS(t, "EmberBed", new Vector3(0f, 0.07f, 0f), SoftAdd, new Vector2(4f, 4f), new Vector2(3f, 3.8f),
                new Color(1f, 0.35f, 0.06f, 0.8f), 20, false);
            Burst(bed, 18); Flat(bed); Big(bed); TrailShape(bed, 0.3f, 0.95f);
            ColorLife(bed, Fade(Color.white, new Color(0.8f, 0.2f, 0.05f), 1f, 0.04f, 0.6f));
            var lights = PS(t, "Lights", new Vector3(0f, 0.6f, 0f), SoftAdd, new Vector2(4f, 4f), new Vector2(0.1f, 0.1f), PhoenixFire, 3, false);
            var lem = lights.emission; lem.rateOverTime = 0f;
            lem.SetBursts(new[] { new ParticleSystem.Burst(0f, 3) });
            TrailShape(lights, 0f, 0.7f);
            ColorLife(lights, Fade(Color.white, Color.white, 1f, 0.05f, 0.6f));
            LightModule(lights, 2.5f, 5f);
            var ll = lights.lights; ll.maxLights = 3;

            var flames = PS(t, "Flames", new Vector3(0f, 0.15f, 0f), FlameAdd, new Vector2(0.5f, 0.9f), new Vector2(0.8f, 1.5f), PhoenixFire, 200, false);
            Rate(flames, 110f); TrailShape(flames, 0.8f, 0.97f);
            var fe = flames.emission; fe.SetBursts(new[] { new ParticleSystem.Burst(0f, 40) });
            Rise(flames, 0.8f, 1.6f); Spin(flames, 140f); SizeLife(flames, 1f, 0.25f); ColorLife(flames, Heat());
            var embers = PS(t, "Embers", new Vector3(0f, 0.2f, 0f), SparkAdd, new Vector2(0.8f, 1.4f), new Vector2(0.05f, 0.1f), PhoenixGold, 60, false);
            Rate(embers, 22f); TrailShape(embers, 0.8f, 0.97f); Rise(embers, 1f, 2.4f); Noise(embers, 0.5f, 1.2f); ColorLife(embers, Heat());
            var smoke = PS(t, "Smoke", new Vector3(0f, 0.4f, 0f), SmokeAlpha, new Vector2(1.4f, 2f), new Vector2(0.8f, 1.3f),
                new Color(0.22f, 0.19f, 0.17f, 0.5f), 20, false);
            Rate(smoke, 6f); TrailShape(smoke, 0.6f, 0.95f); Rise(smoke, 0.7f, 1.3f); Spin(smoke, 40f); SizeLife(smoke, 0.6f, 1.6f);
            ColorLife(smoke, Fade(Color.white, Color.white, 0.8f, 0.15f, 0.5f));
            return SaveFx(root, "VFX_PhoenixFireTrail");
        }

        // Treffer im Sturzflug (am Gegner + 0,9 m, lebt 1 s): Lichtblitz, Feuerstoß, Funken, Federfetzen, Bodenring
        internal static GameObject BuildPhoenixDiveHit(Mesh featherMesh, Material featherMat)
        {
            var root = new GameObject("VFX_PhoenixDiveHit");
            var t = root.transform;
            var flash = PS(t, "Flash", Vector3.zero, SoftAdd, new Vector2(0.2f, 0.25f), new Vector2(2.2f, 2.6f), PhoenixGold, 2, false);
            Burst(flash, 1); SizeLife(flash, 0.5f, 1.2f); ColorLife(flash, Fade(Color.white, Color.white, 1f, 0.02f, 0.3f));
            LightModule(flash, 3f, 5f);
            var fire = PS(t, "FireBurst", Vector3.zero, FlameAdd, new Vector2(0.35f, 0.55f), new Vector2(0.5f, 0.9f), PhoenixFire, 20, false);
            Burst(fire, 14); Sphere(fire, 0.3f);
            var mf = fire.main; mf.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
            Drag(fire, 4f); Spin(fire, 150f); SizeLife(fire, 1f, 0.3f); ColorLife(fire, Heat());
            var sparks = PS(t, "Sparks", Vector3.zero, SparkAdd, new Vector2(0.3f, 0.5f), new Vector2(0.06f, 0.1f), PhoenixGold, 24, false);
            Burst(sparks, 18); Sphere(sparks, 0.2f);
            var ms = sparks.main; ms.startSpeed = new ParticleSystem.MinMaxCurve(4f, 8f); ms.gravityModifier = 1f;
            Stretch(sparks, 0.05f, 2f); ColorLife(sparks, Heat());
            var feathers = PS(t, "Feathers", Vector3.zero, featherMat, new Vector2(0.7f, 0.9f), new Vector2(0.5f, 0.7f), Color.white, 4, false);
            Burst(feathers, 3); Sphere(feathers, 0.2f); FeatherParticles(feathers, featherMesh, 4f);
            var mfe = feathers.main; mfe.startSpeed = new ParticleSystem.MinMaxCurve(2f, 3.5f); mfe.gravityModifier = 0.4f;
            ColorLife(feathers, Fade(Color.white, Color.white, 1f, 0.05f, 0.6f));
            var ring = PS(t, "Ring", new Vector3(0f, -0.82f, 0f), RingAdd, new Vector2(0.4f, 0.4f), new Vector2(1f, 1f), PhoenixFire, 2, false);
            Burst(ring, 1); Flat(ring); SizeLife(ring, 0.4f, 2.6f); ColorLife(ring, Fade(Color.white, Color.white, 1f, 0.02f, 0.4f));
            return SaveFx(root, "VFX_PhoenixDiveHit");
        }

        // Wiedergeburt (am Boden, lebt 3 s): Säule aus goldenem Feuer, Lichtstrahl-Funken, Federstoß, Blitzlicht, Bodenring, Glut
        internal static GameObject BuildPhoenixRebirth(Mesh featherMesh, Material featherMat)
        {
            var root = new GameObject("VFX_PhoenixRebirth");
            var t = root.transform;
            var column = PS(t, "FireColumn", new Vector3(0f, 0.1f, 0f), FlameAdd, new Vector2(0.6f, 0.9f), new Vector2(0.8f, 1.4f), PhoenixGold, 120, false);
            var mc = column.main; mc.duration = 1.1f; mc.startSpeed = new ParticleSystem.MinMaxCurve(6f, 11f); mc.maxParticles = 160;
            Rate(column, 130f); Cone(column, 6f, 0.5f); Spin(column, 140f); SizeLife(column, 1f, 0.3f);
            ColorLife(column, GoldHeat());
            var beam = PS(t, "BeamSparks", new Vector3(0f, 0.2f, 0f), SparkAdd, new Vector2(0.5f, 0.8f), new Vector2(0.1f, 0.16f), PhoenixGold, 40, false);
            Burst(beam, 28); Cone(beam, 4f, 0.35f);
            var mb = beam.main; mb.startSpeed = new ParticleSystem.MinMaxCurve(9f, 14f);
            Stretch(beam, 0.08f, 3f); ColorLife(beam, GoldHeat());
            var feathers = PS(t, "FeatherBurst", new Vector3(0f, 1.2f, 0f), featherMat, new Vector2(1.6f, 2.4f), new Vector2(0.8f, 1.2f), Color.white, 18, false);
            Burst(feathers, 14, 0.15f); Sphere(feathers, 0.4f); FeatherParticles(feathers, featherMesh, 2.5f);
            var mfe = feathers.main; mfe.startSpeed = new ParticleSystem.MinMaxCurve(3f, 6f); mfe.gravityModifier = 0.15f;
            Drag(feathers, 2f); ColorLife(feathers, Fade(Color.white, new Color(1f, 0.5f, 0.2f), 1f, 0.05f, 0.7f));
            var flash = PS(t, "Flash", new Vector3(0f, 1.2f, 0f), SoftAdd, new Vector2(0.5f, 0.5f), new Vector2(5f, 5.5f), PhoenixGold, 2, false);
            Burst(flash, 1); SizeLife(flash, 0.5f, 1.2f); ColorLife(flash, Fade(Color.white, Color.white, 1f, 0.03f, 0.35f)); Big(flash);
            LightModule(flash, 8f, 10f);
            var ring = PS(t, "Ring", new Vector3(0f, 0.08f, 0f), RingAdd, new Vector2(0.8f, 0.8f), new Vector2(1f, 1f), PhoenixGold, 2, false);
            Burst(ring, 1); Flat(ring); Big(ring); SizeLife(ring, 0.5f, 9f); ColorLife(ring, Fade(Color.white, Color.white, 1f, 0.02f, 0.4f));
            var disc = PS(t, "SunDisc", new Vector3(0f, 0.06f, 0f), SoftAdd, new Vector2(1.5f, 1.5f), new Vector2(5f, 5f), new Color(1f, 0.7f, 0.3f, 0.6f), 2, false);
            Burst(disc, 1); Flat(disc); Big(disc); ColorLife(disc, Fade(Color.white, Color.white, 1f, 0.05f, 0.5f));
            var embers = PS(t, "Embers", new Vector3(0f, 0.15f, 0f), SparkAdd, new Vector2(1.2f, 2f), new Vector2(0.06f, 0.12f), PhoenixFire, 50, false);
            Burst(embers, 40, 0.1f); Ring(embers, 0.8f, 1f); Rise(embers, 1.5f, 3f); Noise(embers, 0.6f, 1.2f); ColorLife(embers, Heat());
            return SaveFx(root, "VFX_PhoenixRebirth");
        }

        // Wartezeit (3 s / 5 s; PhoenixRebirth hängt sich an und zerstört es beim Wiedererscheinen): glühendes Glut-Ei
        // mit pulsierendem Schein + Licht, aufsteigende Glut, kleine Flammen, Bodenglühen
        internal static GameObject BuildPhoenixRebirthWait()
        {
            var root = new GameObject("VFX_PhoenixRebirthWait");
            var t = root.transform;
            var egg = AddModel(t, PhoenixDir + "Phoenix_Egg.fbx", "Egg", Vector3.zero, 1.4f, null);
            foreach (var r in egg.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var pulse = PS(t, "Pulse", new Vector3(0f, 0.45f, 0f), SoftAdd, new Vector2(0.7f, 0.7f), new Vector2(2.6f, 2.6f), new Color(1f, 0.55f, 0.15f, 0.95f), 3, true);
            Rate(pulse, 1.4f); SizeBump(pulse); LightModule(pulse, 3.5f, 5f);
            var ground = PS(t, "GroundGlow", new Vector3(0f, 0.04f, 0f), SoftAdd, new Vector2(1.5f, 2f), new Vector2(3f, 3.4f), new Color(1f, 0.4f, 0.1f, 0.55f), 4, true);
            Rate(ground, 1.5f); Flat(ground); ColorLife(ground, Fade(Color.white, Color.white, 1f, 0.3f, 0.6f));
            var em = PS(t, "Embers", new Vector3(0f, 0.2f, 0f), SparkAdd, new Vector2(1f, 1.6f), new Vector2(0.06f, 0.12f), PhoenixFire, 40, true);
            Rate(em, 20f); Ring(em, 0.55f, 1f); Rise(em, 0.6f, 1.4f); Noise(em, 0.4f, 1.2f); ColorLife(em, Heat());
            var lick = PS(t, "Flames", new Vector3(0f, 0.4f, 0f), FlameAdd, new Vector2(0.4f, 0.6f), new Vector2(0.35f, 0.55f), PhoenixFire, 24, true);
            Rate(lick, 16f); Sphere(lick, 0.4f, new Vector3(1f, 1.2f, 1f), 0.3f); Rise(lick, 0.5f, 1f); Spin(lick, 120f);
            SizeLife(lick, 1f, 0.2f); ColorLife(lick, Heat());
            return SaveFx(root, "VFX_PhoenixRebirthWait");
        }

        // Goldenes Feuer: weißgold → gold → orange, blendet aus
        internal static Gradient GoldHeat()
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.97f, 0.8f), 0f), new GradientColorKey(new Color(1f, 0.78f, 0.3f), 0.35f), new GradientColorKey(new Color(1f, 0.4f, 0.08f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.06f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        // =====================================================================================================
        // Weltenbaum (Eis + Erde + Licht)
        // =====================================================================================================

        internal const string TreeDir = ModelDir + "Tree/";
        public static readonly Color TreeGreen = new Color(0.55f, 1f, 0.45f);
        public static readonly Color TreeGold = new Color(1f, 0.86f, 0.4f);
        public static readonly Color TreeFrost = new Color(0.65f, 0.9f, 1f);
        // Modell ~2,9 m hoch, steht ohne Sockel auf seinen Wurzeln → ×1,1 ≈ 3,2 m (wie Titan/Sturmfürst)
        public const float TreeScale = 1.1f;
        // Cast-Clip 1,4 s, Wurzel-Stampfer bei 0,8 s; State-Speed 1,6 → 0,5 s nach dem Trigger. Die Wurzeln (VFX_TreeRoot)
        // wachsen ab 0,35 s nach dem Trigger und stehen bei ≈ 0,55 s.
        public const float TreeCastSpeed = 1.6f;
        public const float TreeAura = 11f;

        [MenuItem("BuddyTD/Super-Elementare/Weltenbaum bauen")]
        public static void BuildWorldTree()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("SuperBuilder: nur im Edit-Mode bauen.");
                return;
            }
            Folders();
            Folder(ModelDir.TrimEnd('/'), "Tree");
            string matDir = TreeDir + "Materials/";
            Folder(TreeDir.TrimEnd('/'), "Materials");
            var glow = new System.Collections.Generic.Dictionary<string, float>
            {
                { "Tree_Sun_Glow", 2.6f }, { "Tree_Eye_Glow", 2.6f }, { "Tree_Frost_Glow", 2.4f },
                { "Tree_Bud_Glow", 2.2f }, { "Tree_Petal_Glow", 1.5f }
            };
            ImportCharacter(TreeDir + "WorldTree.fbx", matDir, glow, "Tree_Idle");
            ImportProp(TreeDir + "Tree_Root.fbx", matDir, glow);
            ImportProp(TreeDir + "Tree_Blossom.fbx", matDir, glow);
            ImportProp(TreeDir + "Tree_Leaf.fbx", matDir, glow);
            // Blasse Glow-Grundfarben kippen nach Bloom/Tonemapping ins Weiße → Emission gesättigter (Früchte gold, Eis blau),
            // damit Früchte, Blüten und Eiskristalle aus der Draufsicht lesbar bleiben
            TreeEmission(matDir + "Tree_Sun_Glow.mat", new Color(1f, 0.72f, 0.18f), new Color(1f, 0.55f, 0.05f), 1.8f);
            TreeEmission(matDir + "Tree_Frost_Glow.mat", new Color(0.35f, 0.66f, 0.95f), new Color(0.15f, 0.55f, 1f), 1.6f);
            TreeEmission(matDir + "Tree_Petal_Glow.mat", new Color(1f, 0.8f, 0.85f), new Color(1f, 0.5f, 0.65f), 0.8f);
            TreeEmission(matDir + "Tree_Bud_Glow.mat", new Color(0.7f, 0.92f, 0.3f), new Color(0.6f, 1f, 0.15f), 1.4f);
            TreeEmission(matDir + "Tree_Eye_Glow.mat", new Color(0.75f, 0.95f, 0.35f), new Color(0.7f, 1f, 0.2f), 2.0f);

            var ctrl = BuildController(AnimDir + "WorldTree.controller", Clip(TreeDir + "WorldTree.fbx", "Tree_Idle"),
                Clip(TreeDir + "WorldTree.fbx", "Tree_Cast"), TreeCastSpeed, Clip(TreeDir + "WorldTree.fbx", "Tree_Hit"));

            var leafMesh = SingleSubmesh(TreeDir + "Tree_Leaf.fbx", TreeDir + "Tree_LeafMesh.asset");
            var blossomMesh = SingleSubmesh(TreeDir + "Tree_Blossom.fbx", TreeDir + "Tree_BlossomMesh.asset");
            var leafMat = ParticleLitMat(matDir + "Tree_LeafParticle.mat", new Color(0.42f, 0.72f, 0.28f));
            var blossomMat = ParticleMat(matDir + "Tree_BlossomMesh_Add.mat", null, new Color(1.5f, 0.95f, 0.3f));

            var root = BuildTreeRoot(leafMesh, leafMat);
            var pulse = BuildTreeHealPulse(blossomMesh, blossomMat);
            var sparkle = BuildTreeHealSparkle(blossomMesh, blossomMat);

            var config = AssetDatabase.LoadAssetAtPath<UnitConfigSO>(ConfigDir + "Super_Weltenbaum.asset");
            var prefab = BuildTreePrefab(ctrl, config, root, pulse, sparkle, leafMesh, leafMat, blossomMesh, blossomMat);
            AssignConfig(config, prefab);

            string icon = IconDir + "portrait_super_weltenbaum.png";
            RenderPortrait(prefab, icon, new Vector3(0f, 2.9f, -7.0f), new Vector3(0f, 1.85f, 0f), 32f);
            WireRecipeIcon(FusionElement.WorldTree, icon);
            AssetDatabase.SaveAssets();
            Debug.Log("SuperBuilder: Weltenbaum gebaut (" + PrefabDir + "Super_Weltenbaum.prefab).");
        }

        // ---------------- Weltenbaum: Buddy-Prefab ----------------

        // Kein Sockel: der Baum steht auf seinen eigenen Wurzeln. Kapsel nur um den Stamm (die Wurzeln/Arme sind Deko).
        internal static GameObject BuildTreePrefab(AnimatorController ctrl, UnitConfigSO config, GameObject rootFx, GameObject pulse,
            GameObject sparkle, Mesh leafMesh, Material leafMat, Mesh blossomMesh, Material blossomMat)
        {
            var root = NewBuddyRoot("Super_Weltenbaum");
            var cap = root.AddComponent<CapsuleCollider>();
            cap.center = new Vector3(0f, 1.4f, 0f);
            cap.radius = 0.7f;
            cap.height = 2.8f;

            var visual = AddModel(root.transform, TreeDir + "WorldTree.fbx", "Visual", Vector3.zero, TreeScale, ctrl);
            var head = Deep(visual, "Head");
            var fp = new GameObject("FirePoint").transform;
            fp.SetParent(root.transform, false);
            fp.position = head != null ? head.position : new Vector3(0f, 1.8f, 0f);

            PointLight(root.transform, "Glow", new Vector3(0f, 2.3f, -0.3f), new Color(0.75f, 1f, 0.5f), 1.3f, 6f);

            // Stampfer zur Freigabe der Cast-Animation: Erdstoß + Staubring + grüner Bodenring am Stammfuß
            TreeSlamFx(root.transform, leafMesh, leafMat);

            var fx = new GameObject("ElementFX").transform;
            fx.SetParent(root.transform, false);
            TreeElementFx(fx, leafMesh, leafMat, blossomMesh, blossomMat);
            // Frost-Funkeln an den Kronen-Knochen (bewegen sich mit dem Idle-Wiegen)
            var canopy = Deep(visual, "Canopy");
            if (canopy != null) TreeCanopyFx(canopy);

            var buddy = root.AddComponent<WorldTreeBuddy>();
            buddy.Config = config;
            buddy.Element = FusionElement.WorldTree;
            buddy.BaseMaxHP = 900f;
            buddy.DamageReduction = 0.25f;
            buddy.Visual = visual;
            buddy.FirePoint = fp;
            buddy.RootEffectPrefab = rootFx;
            buddy.HealPulsePrefab = pulse;
            buddy.HealSparklePrefab = sparkle;
            return SavePrefab(root, PrefabDir + "Super_Weltenbaum.prefab");
        }

        internal static void TreeSlamFx(Transform root, Mesh leafMesh, Material leafMat)
        {
            var holder = new GameObject("FX_Slam").transform;
            holder.SetParent(root, false);
            var cast = holder.gameObject.AddComponent<SuperCastFx>();
            cast.Delay = 0.8f / TreeCastSpeed;
            var dust = PS(holder, "Dust", new Vector3(0f, 0.2f, 0f), SmokeAlpha, new Vector2(0.9f, 1.3f), new Vector2(0.8f, 1.3f),
                new Color(0.55f, 0.45f, 0.33f, 0.8f), 24, false);
            Burst(dust, 18); NoAwake(dust); Ring(dust, 1.1f);
            var md = dust.main; md.startSpeed = new ParticleSystem.MinMaxCurve(3f, 5f);
            Drag(dust, 4f); Spin(dust, 60f); SizeLife(dust, 0.7f, 1.6f); ColorLife(dust, Fade(Color.white, Color.white, 0.85f, 0.05f, 0.45f));
            var clods = PS(holder, "Clods", new Vector3(0f, 0.2f, 0f), SoftAlpha, new Vector2(0.5f, 0.8f), new Vector2(0.12f, 0.22f),
                new Color(0.3f, 0.2f, 0.12f, 1f), 24, false);
            Burst(clods, 18); NoAwake(clods); Cone(clods, 55f, 1.0f);
            var mc = clods.main; mc.startSpeed = new ParticleSystem.MinMaxCurve(3f, 6f); mc.gravityModifier = 2f;
            var ring = PS(holder, "Ring", new Vector3(0f, 0.08f, 0f), RingAdd, new Vector2(0.5f, 0.5f), new Vector2(4.2f, 4.2f), TreeGreen, 2, false);
            Burst(ring, 1); NoAwake(ring); Flat(ring); Big(ring); SizeLife(ring, 0.4f, 2.2f); ColorLife(ring, Fade(Color.white, TreeGold, 0.9f, 0.03f, 0.4f));
            var leaves = PS(holder, "Leaves", new Vector3(0f, 2.2f, 0f), leafMat, new Vector2(1.6f, 2.2f), new Vector2(0.9f, 1.3f), Color.white, 12, false);
            Burst(leaves, 8); NoAwake(leaves); Sphere(leaves, 1.2f, new Vector3(1f, 0.4f, 1f), 1f); FeatherParticles(leaves, leafMesh, 3f);
            var ml = leaves.main; ml.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.5f); ml.gravityModifier = 0.15f;
            Drag(leaves, 1.5f); ColorLife(leaves, Fade(Color.white, Color.white, 1f, 0.05f, 0.8f));
        }

        // Umgebung: fallende Blätter, treibende Pollen + leuchtende Blüten aus der Krone, grün-goldenes Bodenglühen,
        // Heil-Aura-Ring (Radius 11 m = Heilradius; Ring-Textur: Größe ≈ 4,2 × Radius) mit Funkeln auf dem Ring
        internal static void TreeElementFx(Transform fx, Mesh leafMesh, Material leafMat, Mesh blossomMesh, Material blossomMat)
        {
            var ring = PS(fx, "AuraRing", new Vector3(0f, 0.06f, 0f), RingAdd, new Vector2(3f, 3f), new Vector2(TreeAura * 4.2f, TreeAura * 4.2f),
                new Color(0.7f, 1f, 0.45f, 0.14f), 3, true);
            Rate(ring, 0.6f); Flat(ring); Big(ring); SizeLife(ring, 0.98f, 1.01f); ColorLife(ring, Fade(Color.white, Color.white, 1f, 0.3f, 0.6f));
            var motes = PS(fx, "AuraMotes", new Vector3(0f, 0.1f, 0f), StarAdd, new Vector2(1.4f, 2f), new Vector2(0.14f, 0.24f),
                new Color(0.8f, 1f, 0.55f, 0.75f), 18, true);
            Rate(motes, 6f); Ring(motes, TreeAura, 0.02f); Rise(motes, 0.2f, 0.5f); SizeBump(motes); Spin(motes, 60f);
            var groundGlow = PS(fx, "GroundGlow", new Vector3(0f, 0.05f, 0f), SoftAdd, new Vector2(2.5f, 3f), new Vector2(6.4f, 7.2f),
                new Color(0.6f, 0.95f, 0.4f, 0.13f), 3, true);
            Rate(groundGlow, 1f); Flat(groundGlow); Big(groundGlow); ColorLife(groundGlow, Fade(Color.white, Color.white, 1f, 0.3f, 0.6f));

            var leaves = PS(fx, "FallingLeaves", new Vector3(0f, 2.6f, 0f), leafMat, new Vector2(4.5f, 6f), new Vector2(0.9f, 1.3f), Color.white, 16, true);
            Rate(leaves, 2f); Sphere(leaves, 1.3f, new Vector3(1f, 0.35f, 1f), 1f);
            FeatherParticles(leaves, leafMesh, 1.5f);
            Rise(leaves, -0.5f, -0.35f); Noise(leaves, 0.4f, 0.4f);
            ColorLife(leaves, Fade(Color.white, new Color(0.85f, 0.75f, 0.35f), 1f, 0.08f, 0.8f));
            var pollen = PS(fx, "Pollen", new Vector3(0f, 2.4f, 0f), SoftAdd, new Vector2(3f, 4.5f), new Vector2(0.06f, 0.11f), TreeGold, 50, true);
            Rate(pollen, 10f); Sphere(pollen, 1.6f, new Vector3(1f, 0.6f, 1f), 0.4f); Rise(pollen, -0.1f, 0.25f); Noise(pollen, 0.5f, 0.6f);
            ColorLife(pollen, Fade(Color.white, Color.white, 1f, 0.2f, 0.7f));
            var blossoms = PS(fx, "BlossomMotes", new Vector3(0f, 2.8f, 0f), blossomMat, new Vector2(5f, 6.5f), new Vector2(0.6f, 0.85f), Color.white, 8, true);
            Rate(blossoms, 0.9f); Sphere(blossoms, 1.3f, new Vector3(1f, 0.3f, 1f), 1f);
            FeatherParticles(blossoms, blossomMesh, 0.8f);
            Rise(blossoms, -0.4f, -0.28f); Noise(blossoms, 0.35f, 0.35f);
            ColorLife(blossoms, Fade(Color.white, Color.white, 1f, 0.1f, 0.75f));
            var canopyGlow = PS(fx, "CanopyGlow", new Vector3(0f, 2.7f, 0f), SoftAdd, new Vector2(1.6f, 2.2f), new Vector2(1.4f, 1.9f),
                new Color(1f, 0.9f, 0.45f, 0.12f), 8, true);
            Rate(canopyGlow, 3f); Sphere(canopyGlow, 1.2f, new Vector3(1f, 0.4f, 1f), 0.3f); ColorLife(canopyGlow, Fade(Color.white, Color.white, 1f, 0.3f, 0.6f));
        }

        // Frost-Funkeln + Eisglitzern an der Krone (Eis-Anteil), folgt dem Canopy-Knochen
        internal static void TreeCanopyFx(Transform canopyBone)
        {
            var a = FxAnchor(canopyBone, "FX_Canopy");
            var glint = PS(a, "FrostGlints", new Vector3(0f, 0.5f, 0f), StarAdd, new Vector2(0.5f, 0.8f), new Vector2(0.16f, 0.28f), TreeFrost, 10, true);
            Rate(glint, 4f); Sphere(glint, 1.3f, new Vector3(1f, 0.5f, 1f), 0.15f); SizeBump(glint); Spin(glint, 90f);
            var frost = PS(a, "FrostMotes", new Vector3(0f, 0.5f, 0f), SoftAdd, new Vector2(1.8f, 2.6f), new Vector2(0.05f, 0.09f), TreeFrost, 20, true);
            Rate(frost, 5f); Sphere(frost, 1.4f, new Vector3(1f, 0.5f, 1f), 0.2f); Rise(frost, -0.2f, 0.15f); Noise(frost, 0.4f, 0.7f);
            ColorLife(frost, Fade(Color.white, Color.white, 0.9f, 0.2f, 0.7f));
        }

        // ---------------- Weltenbaum: Effekte ----------------

        // Wurzeln (am Gegner, lebt RootDuration 2,5 s): drei dornige Wurzelranken (Tree_Root, krümmen sich zum Gegner),
        // Erdstoß, Staub, Erdbrocken, Blätter, grüner Bodenring, Erdfleck. TreeRootFx setzt den Bodenteil auf den Boden
        // (Gegner-Pivot liegt höher), lässt die Ranken wachsen/versinken und startet die Partikel nach Delay.
        internal static GameObject BuildTreeRoot(Mesh leafMesh, Material leafMat)
        {
            var root = new GameObject("VFX_TreeRoot");
            var g = new GameObject("Ground").transform;
            g.SetParent(root.transform, false);
            var roots = new System.Collections.Generic.List<Transform>();
            float[] angles = { 20f, 140f, 260f };
            float[] scales = { 1.0f, 0.9f, 0.95f };
            for (int i = 0; i < angles.Length; i++)
            {
                float a = angles[i] * Mathf.Deg2Rad;
                Vector3 p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.65f;
                var holder = new GameObject("Root" + i).transform;
                holder.SetParent(g, false);
                holder.localPosition = new Vector3(p.x, -0.04f, p.z);
                // Spitze zeigt im Modell nach +X → zur Mitte (zum Gegner) drehen
                holder.localRotation = Quaternion.FromToRotation(Vector3.right, -p.normalized);
                holder.localScale = new Vector3(1.25f, 1f, 1.25f) * scales[i]; // etwas dicker, liest sich aus der Spielkamera besser
                AddModel(holder, TreeDir + "Tree_Root.fbx", "Mesh", Vector3.zero, 1f, null);
                roots.Add(holder);
            }

            var burst = PS(g, "EarthBurst", new Vector3(0f, 0.15f, 0f), SmokeAlpha, new Vector2(0.8f, 1.2f), new Vector2(0.6f, 1.0f),
                new Color(0.5f, 0.4f, 0.28f, 0.85f), 16, false);
            Burst(burst, 12); NoAwake(burst); Ring(burst, 0.5f);
            var mb = burst.main; mb.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            Drag(burst, 4f); Spin(burst, 60f); SizeLife(burst, 0.7f, 1.5f); ColorLife(burst, Fade(Color.white, Color.white, 0.85f, 0.05f, 0.45f));
            var clods = PS(g, "Clods", new Vector3(0f, 0.1f, 0f), SoftAlpha, new Vector2(0.5f, 0.8f), new Vector2(0.1f, 0.18f),
                new Color(0.28f, 0.19f, 0.11f, 1f), 20, false);
            Burst(clods, 14); NoAwake(clods); Cone(clods, 45f, 0.5f);
            var mc = clods.main; mc.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 5f); mc.gravityModifier = 2f;
            var leaves = PS(g, "Leaves", new Vector3(0f, 0.6f, 0f), leafMat, new Vector2(1.2f, 1.8f), new Vector2(0.8f, 1.1f), Color.white, 8, false);
            Burst(leaves, 5); NoAwake(leaves); Sphere(leaves, 0.4f); FeatherParticles(leaves, leafMesh, 4f);
            var ml = leaves.main; ml.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3f); ml.gravityModifier = 0.35f;
            Drag(leaves, 2f); ColorLife(leaves, Fade(Color.white, Color.white, 1f, 0.05f, 0.75f));
            var thorns = PS(g, "ThornSparks", new Vector3(0f, 0.4f, 0f), SparkAdd, new Vector2(0.3f, 0.5f), new Vector2(0.06f, 0.1f), TreeGreen, 16, false);
            Burst(thorns, 12); NoAwake(thorns); Hemisphere(thorns, 0.5f);
            var mt = thorns.main; mt.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4f); mt.gravityModifier = 0.6f;
            Stretch(thorns, 0.05f, 2f); ColorLife(thorns, Fade(Color.white, TreeGold, 1f, 0.02f, 0.5f));
            var ring = PS(g, "Ring", new Vector3(0f, 0.06f, 0f), RingAdd, new Vector2(0.45f, 0.45f), new Vector2(4.2f * 1.1f, 4.2f * 1.1f), TreeGreen, 2, false);
            Burst(ring, 1); NoAwake(ring); Flat(ring); Big(ring); SizeLife(ring, 0.3f, 1f); ColorLife(ring, Fade(Color.white, TreeGold, 0.8f, 0.03f, 0.4f));
            var patch = PS(g, "EarthPatch", new Vector3(0f, 0.04f, 0f), SoftAlpha, new Vector2(2.1f, 2.1f), new Vector2(4f * 0.75f, 4f * 0.8f),
                new Color(0.2f, 0.13f, 0.07f, 0.75f), 2, false);
            Burst(patch, 1); NoAwake(patch); Flat(patch); Big(patch); ColorLife(patch, Fade(Color.white, Color.white, 1f, 0.06f, 0.8f));
            var glow = PS(g, "BindGlow", new Vector3(0f, 0.05f, 0f), SoftAdd, new Vector2(2.0f, 2.0f), new Vector2(4f * 0.6f, 4f * 0.6f),
                new Color(0.55f, 1f, 0.35f, 0.22f), 2, false);
            Burst(glow, 1); NoAwake(glow); Flat(glow); Big(glow); ColorLife(glow, Fade(Color.white, Color.white, 1f, 0.1f, 0.75f));

            var rfx = root.AddComponent<TreeRootFx>();
            rfx.Ground = g;
            rfx.Roots = roots.ToArray();
            rfx.Delay = 0.35f;
            rfx.Life = 2.5f;
            return SaveFx(root, "VFX_TreeRoot");
        }

        // Heilpuls (Einheitsradius 1; WorldTreeBuddy skaliert auf (r, 1, r), TreeHealPulseFx überträgt r auf Partikel; lebt 2 s,
        // kommt jede Sekunde, solange jemand geheilt wird → dezent): nach außen laufender grün-goldener Ring, aufsteigende
        // Blüten + Funkel auf der Fläche, kurzes Glühen am Stamm
        internal static GameObject BuildTreeHealPulse(Mesh blossomMesh, Material blossomMat)
        {
            var root = new GameObject("VFX_TreeHealPulse");
            var t = root.transform;
            var ring = PS(t, "Ring", new Vector3(0f, 0.04f, 0f), RingAdd, new Vector2(1.1f, 1.1f), new Vector2(4.2f, 4.2f),
                new Color(0.75f, 1f, 0.5f, 0.3f), 2, false);
            Burst(ring, 1); NoAwake(ring); Flat(ring); Big(ring); SizeLife(ring, 0.15f, 1f); ColorLife(ring, Fade(Color.white, TreeGold, 1f, 0.1f, 0.55f));
            var blossoms = PS(t, "Blossoms", new Vector3(0f, 0.15f, 0f), blossomMat, new Vector2(1.3f, 1.8f), new Vector2(0.5f, 0.75f), Color.white, 6, false);
            Burst(blossoms, 4); NoAwake(blossoms); Ring(blossoms, 0.6f, 1f); FeatherParticles(blossoms, blossomMesh, 1.5f);
            Rise(blossoms, 0.6f, 1.1f); ColorLife(blossoms, Fade(Color.white, Color.white, 1f, 0.15f, 0.6f));
            var motes = PS(t, "Motes", new Vector3(0f, 0.15f, 0f), StarAdd, new Vector2(0.9f, 1.3f), new Vector2(0.12f, 0.2f), new Color(0.85f, 1f, 0.55f, 0.8f), 16, false);
            Burst(motes, 10); NoAwake(motes); Ring(motes, 0.8f, 1f); Rise(motes, 0.5f, 1.2f); SizeBump(motes); Spin(motes, 80f);
            var core = PS(t, "TrunkGlow", new Vector3(0f, 1.2f, 0f), SoftAdd, new Vector2(0.8f, 0.8f), new Vector2(2.4f, 2.4f), new Color(0.7f, 1f, 0.45f, 0.18f), 2, false);
            Burst(core, 1); NoAwake(core); ColorLife(core, Fade(Color.white, Color.white, 1f, 0.2f, 0.5f));
            var fxc = root.AddComponent<TreeHealPulseFx>();
            fxc.ScaleSize = new[] { ring };
            fxc.ScaleShape = new[] { blossoms, motes };
            return SaveFx(root, "VFX_TreeHealPulse");
        }

        // Heil-Funkeln am geheilten Buddy (Position + 0,8 m, lebt 2 s): grün-goldener Schein, aufsteigende Funkel, 2 Blüten
        internal static GameObject BuildTreeHealSparkle(Mesh blossomMesh, Material blossomMat)
        {
            var root = new GameObject("VFX_TreeHealSparkle");
            var t = root.transform;
            var glow = PS(t, "Glow", Vector3.zero, SoftAdd, new Vector2(0.6f, 0.6f), new Vector2(1.5f, 1.5f), new Color(0.7f, 1f, 0.45f, 0.45f), 2, false);
            Burst(glow, 1); SizeLife(glow, 0.6f, 1.2f); ColorLife(glow, Fade(Color.white, Color.white, 1f, 0.1f, 0.4f));
            var stars = PS(t, "Sparkles", new Vector3(0f, -0.3f, 0f), StarAdd, new Vector2(0.7f, 1.0f), new Vector2(0.14f, 0.24f), new Color(0.85f, 1f, 0.5f), 12, false);
            Burst(stars, 8); Sphere(stars, 0.45f, new Vector3(1f, 0.6f, 1f), 1f); Rise(stars, 0.8f, 1.5f); SizeBump(stars); Spin(stars, 90f);
            var motes = PS(t, "GoldMotes", new Vector3(0f, -0.2f, 0f), SoftAdd, new Vector2(0.8f, 1.2f), new Vector2(0.06f, 0.1f), TreeGold, 12, false);
            Burst(motes, 8); Sphere(motes, 0.4f); Rise(motes, 0.6f, 1.2f); ColorLife(motes, Fade(Color.white, Color.white, 1f, 0.1f, 0.6f));
            var blossoms = PS(t, "Blossoms", new Vector3(0f, 0.1f, 0f), blossomMat, new Vector2(1.0f, 1.3f), new Vector2(0.4f, 0.55f), Color.white, 3, false);
            Burst(blossoms, 2); Sphere(blossoms, 0.35f); FeatherParticles(blossoms, blossomMesh, 2f); Rise(blossoms, 0.5f, 0.9f);
            ColorLife(blossoms, Fade(Color.white, Color.white, 1f, 0.15f, 0.6f));
            return SaveFx(root, "VFX_TreeHealSparkle");
        }

        internal static void TreeEmission(string path, Color baseColor, Color color, float intensity)
        {
            var m = Load<Material>(path);
            if (m == null) return;
            m.SetColor("_BaseColor", baseColor);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color * intensity);
            EditorUtility.SetDirty(m);
        }

        // URP-Particles/Simple Lit (deckend), für Blatt-Mesh-Partikel, die nicht leuchten sollen
        internal static Material ParticleLitMat(string path, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Simple Lit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            if (m.shader != shader) m.shader = shader;
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_Cull", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        // Große (Boden-)Partikel nicht auf die halbe Bildschirmgröße begrenzen (Standard maxParticleSize 0,5)
        internal static void Big(ParticleSystem ps) { ps.GetComponent<ParticleSystemRenderer>().maxParticleSize = 10f; }

        // URP-Particles/Unlit additiv (Vorlage = Material mit Textur oder untexturiert), HDR-Grundfarbe für Bloom
        internal static Material ParticleMat(string path, string templatePath, Color hdr)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var template = templatePath != null ? AssetDatabase.LoadAssetAtPath<Material>(templatePath) : SoftAdd;
            if (m == null)
            {
                m = new Material(template);
                AssetDatabase.CreateAsset(m, path);
            }
            else m.CopyPropertiesFromMaterial(template);
            if (templatePath == null) m.SetTexture("_BaseMap", null);
            m.SetColor("_BaseColor", hdr);
            EditorUtility.SetDirty(m);
            return m;
        }

        internal static Material LitMaterial(string path, Color color, float smoothness)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        // =====================================================================================================
        // Gemeinsame Bausteine (für alle Supers)
        // =====================================================================================================

        internal static void Folders()
        {
            Folder("Assets/_Game/Models", "Super");
            Folder("Assets/_Game/Animations", "Super");
            Folder("Assets/_Game/Prefabs", "Super");
            Folder("Assets/_Game/VFX/Prefabs", "Super");
        }

        internal static void Folder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }

        internal static T Load<T>(string path) where T : Object
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a == null) Debug.LogWarning("SuperBuilder: Asset fehlt: " + path + " (" + typeof(T).Name + ")");
            return a;
        }

        // ---------------- Modelle ----------------

        // Charakter-FBX: Generic-Rig (Blender-Ruhepose ist keine T-Pose, nur eigene Clips), alle Takes als Clips,
        // loopClips loopen; eingebettete Materialien → URP-Lit unter matDir (Remap per Name)
        internal static void ImportCharacter(string path, string matDir, System.Collections.Generic.Dictionary<string, float> glow, params string[] loopClips)
        {
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null) { Debug.LogError("SuperBuilder: kein Modell unter " + path); return; }
            imp.animationType = ModelImporterAnimationType.Generic;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.importAnimation = true;
            imp.importCameras = false; imp.importLights = false;
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
            var clips = imp.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.loopTime = System.Array.IndexOf(loopClips, c.name) >= 0;
                c.loopPose = false;
            }
            imp.clipAnimations = clips;
            RemapMaterials(imp, path, matDir, glow);
            imp.SaveAndReimport();
        }

        // Statisches Prop-FBX (ohne Rig/Animation), Materialien wie beim Charakter (gleiche Namen → gleiche Assets)
        internal static void ImportProp(string path, string matDir, System.Collections.Generic.Dictionary<string, float> glow)
        {
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null) { Debug.LogError("SuperBuilder: kein Modell unter " + path); return; }
            imp.animationType = ModelImporterAnimationType.None;
            imp.importAnimation = false;
            imp.importCameras = false; imp.importLights = false;
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
            RemapMaterials(imp, path, matDir, glow);
            imp.SaveAndReimport();
        }

        internal static void RemapMaterials(ModelImporter imp, string path, string matDir, System.Collections.Generic.Dictionary<string, float> glow)
        {
            // Eingebettete Materialien (erster Import) bzw. bereits umgeleitete (Folge-Builds: Werte neu anwenden)
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                var em = o as Material;
                if (em == null) continue;
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), em.name);
                imp.AddRemap(id, ModelMaterial(em, matDir, glow));
            }
            foreach (var kv in imp.GetExternalObjectMap())
            {
                var ext = kv.Value as Material;
                if (ext != null && AssetDatabase.GetAssetPath(ext).StartsWith(matDir)) ModelMaterial(ext, matDir, glow);
            }
        }

        // URP-Lit aus dem eingebetteten Material; *_Glow: Emission = Grundfarbe × Intensität (HDR)
        internal static Material ModelMaterial(Material embedded, string matDir, System.Collections.Generic.Dictionary<string, float> glow)
        {
            string p = matDir + embedded.name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, p);
            }
            if (m.shader.name != "Universal Render Pipeline/Lit") m.shader = Shader.Find("Universal Render Pipeline/Lit");
            Color baseCol = embedded.HasProperty("_BaseColor") ? embedded.GetColor("_BaseColor") : Color.white;
            baseCol.a = 1f;
            m.SetColor("_BaseColor", baseCol);
            float smooth = embedded.HasProperty("_Smoothness") ? embedded.GetFloat("_Smoothness") : 0.3f;
            m.SetFloat("_Smoothness", Mathf.Clamp(smooth, 0.05f, 0.6f));
            m.SetFloat("_Metallic", 0f);
            float intensity;
            if (embedded.name.EndsWith("_Glow"))
            {
                if (glow == null || !glow.TryGetValue(embedded.name, out intensity)) intensity = 2.5f;
                m.EnableKeyword("_EMISSION");
                // Lava: Emission etwas röter als die Grundfarbe, sonst kippt sie nach dem Tonemapping ins Gelbe
                Color emCol = embedded.name.Contains("Lava") ? new Color(1f, 0.2f, 0.025f) : baseCol;
                m.SetColor("_EmissionColor", emCol * intensity);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                m.DisableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.black);
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        // Modell-Instanz (entpackt) als Kind; ctrl != null → Animator ohne Root Motion
        internal static Transform AddModel(Transform parent, string fbx, string name, Vector3 localPos, float scale, AnimatorController ctrl)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            // Einzel-Objekt-FBX tragen die Blender-Achsen (−90° X, Skalierung 100) am Wurzelknoten → multiplizieren statt setzen.
            // Blender-Modelle schauen in Unity nach +Z; Buddies schauen wie die 2er-Fusionen zur Kamera (−Z) → 180°
            go.transform.localRotation = (ctrl != null ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity) * go.transform.localRotation;
            go.transform.localScale = go.transform.localScale * scale;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = false;
            }
            if (ctrl != null)
            {
                var an = go.GetComponent<Animator>();
                if (an == null) an = go.AddComponent<Animator>();
                an.runtimeAnimatorController = ctrl;
                an.applyRootMotion = false;
                an.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            }
            else
            {
                var an = go.GetComponent<Animator>();
                if (an != null) Object.DestroyImmediate(an);
            }
            return go.transform;
        }

        internal static AnimationClip Clip(string path, string name)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                var c = o as AnimationClip;
                if (c == null || c.name.StartsWith("__preview")) continue;
                if (c.name == name) return c;
            }
            Debug.LogWarning("SuperBuilder: Clip " + name + " fehlt in " + path);
            return null;
        }

        // Controller wie bei den Fusionen: Idle (Loop) → Cast per Trigger "Cast" → Idle bei 92 %; optional Hit per Trigger "Hit".
        // Bestehender Controller wird geleert statt gelöscht (GUID bleibt).
        internal static AnimatorController BuildController(string path, AnimationClip idle, AnimationClip cast, float castSpeed, AnimationClip hit)
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (ac == null) ac = AnimatorController.CreateAnimatorControllerAtPath(path);
            var sm = ac.layers[0].stateMachine;
            foreach (var t in sm.anyStateTransitions) sm.RemoveAnyStateTransition(t);
            foreach (var s in sm.states) sm.RemoveState(s.state);
            while (ac.parameters.Length > 0) ac.RemoveParameter(0);
            ac.AddParameter("Cast", AnimatorControllerParameterType.Trigger);
            var idleS = sm.AddState("Idle", new Vector3(300, 0, 0));
            idleS.motion = idle;
            sm.defaultState = idleS;
            var castS = sm.AddState("Cast", new Vector3(300, 120, 0));
            castS.motion = cast;
            castS.speed = castSpeed;
            var toCast = idleS.AddTransition(castS);
            toCast.AddCondition(AnimatorConditionMode.If, 0f, "Cast");
            toCast.hasExitTime = false; toCast.duration = 0.08f;
            var again = castS.AddTransition(castS);
            again.AddCondition(AnimatorConditionMode.If, 0f, "Cast");
            again.hasExitTime = false; again.duration = 0.05f;
            var back = castS.AddTransition(idleS);
            back.hasExitTime = true; back.exitTime = 0.92f; back.duration = 0.15f;
            if (hit != null)
            {
                ac.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
                var hitS = sm.AddState("Hit", new Vector3(550, 0, 0));
                hitS.motion = hit;
                var toHit = idleS.AddTransition(hitS);
                toHit.AddCondition(AnimatorConditionMode.If, 0f, "Hit");
                toHit.hasExitTime = false; toHit.duration = 0.05f;
                var hitBack = hitS.AddTransition(idleS);
                hitBack.hasExitTime = true; hitBack.exitTime = 0.9f; hitBack.duration = 0.1f;
            }
            EditorUtility.SetDirty(ac);
            return ac;
        }

        // ---------------- Prefab-Bausteine ----------------

        // Buddy-Root: Layer und Tag "Buddy" (Collider/Komponenten fügt der Aufrufer hinzu)
        internal static GameObject NewBuddyRoot(string name)
        {
            var go = new GameObject(name);
            int layer = LayerMask.NameToLayer("Buddy");
            if (layer >= 0) go.layer = layer;
            go.tag = "Buddy";
            return go;
        }

        // BuddySockel (wie bei den Fusionen), skaliert und mit eigenen Materialien (Reihenfolge: Stein, Stein hell, Runen, Moos)
        internal static Transform Sockel(Transform parent, Vector3 scale, Material[] mats)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(SockelFbx);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = "Sockel";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = scale;
            var mr = go.GetComponentInChildren<MeshRenderer>();
            if (mr != null && mats != null)
            {
                var cur = mr.sharedMaterials;
                for (int i = 0; i < cur.Length && i < mats.Length; i++) if (mats[i] != null) cur[i] = mats[i];
                mr.sharedMaterials = cur;
            }
            return go.transform;
        }

        internal static Material GlowMaterial(string path, Color color, float intensity)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", color * 0.6f);
            m.SetFloat("_Smoothness", 0.2f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color * intensity);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(m);
            return m;
        }

        // Effekt-Anker unter einem Knoten des FBX-Rigs (Blender-Knoten: Skalierung 100, Rotation −90° X):
        // Welt-Rotation neutral und Welt-Skalierung 1, damit Partikelgrößen/Formen wie gewohnt in Metern gelten
        internal static Transform FxAnchor(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.rotation = parent.root.rotation;
            Vector3 ls = parent.lossyScale;
            float rootScale = parent.root.lossyScale.x;
            t.localScale = new Vector3(rootScale / ls.x, rootScale / ls.y, rootScale / ls.z);
            return t;
        }

        internal static Light PointLight(Transform parent, string name, Vector3 pos, Color color, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color; l.intensity = intensity; l.range = range;
            l.shadows = LightShadows.None;
            return l;
        }

        internal static Transform Deep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        internal static GameObject SavePrefab(GameObject root, string path)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        internal static GameObject SaveFx(GameObject root, string name)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (!(r is ParticleSystemRenderer) && !(r is TrailRenderer)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return SavePrefab(root, FxDir + name + ".prefab");
        }

        internal static void AssignConfig(UnitConfigSO config, GameObject prefab)
        {
            if (config == null) { Debug.LogWarning("SuperBuilder: Config fehlt für " + prefab.name); return; }
            config.Prefab = prefab;
            EditorUtility.SetDirty(config);
        }

        // Rezept-Icon in der offenen Szene (FusionManager.TriRecipes – die Szene überschreibt die Code-Defaults). Speichert die Szene nicht.
        internal static void WireRecipeIcon(FusionElement result, string iconPath)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
            var fm = Object.FindFirstObjectByType<FusionManager>(FindObjectsInactive.Include);
            if (fm == null || sprite == null) { Debug.LogWarning("SuperBuilder: FusionManager oder Icon fehlt (" + iconPath + ")"); return; }
            bool found = false;
            foreach (var r in fm.TriRecipes)
            {
                if (r.Result != result) continue;
                r.Icon = sprite;
                found = true;
            }
            if (!found) Debug.LogWarning("SuperBuilder: kein TriRecipe für " + result + " in der Szene");
            EditorUtility.SetDirty(fm);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(fm.gameObject.scene);
        }

        // ---------------- Porträt ----------------

        // Rendert das Prefab (Idle-Pose bei t = 0,5 s, ohne Partikel/Lichter des Prefabs) wie die portrait_fusion_*:
        // 256×256, transparenter Hintergrund (zweimal gerendert: schwarz/weiß → Alpha), Import als Sprite mit den
        // Einstellungen von portrait_fusion_magma.png. camPos/lookAt im Prefab-Raum.
        internal static void RenderPortrait(GameObject prefab, string path, Vector3 camPos, Vector3 lookAt, float fov)
        {
            RenderPortrait(prefab, path, camPos, lookAt, fov, null);
        }

        // prepare: optional, verändert die Instanz nach dem Pose-Sampling (z. B. Stufe-4-Zustand der Basis-Buddies, Stage4Builder)
        internal static void RenderPortrait(GameObject prefab, string path, Vector3 camPos, Vector3 lookAt, float fov, System.Action<GameObject> prepare)
        {
            const int size = 256;
            Vector3 origin = new Vector3(0f, -400f, 0f);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            inst.transform.position = origin;
            foreach (var ps in inst.GetComponentsInChildren<ParticleSystem>(true)) ps.gameObject.SetActive(false);
            foreach (var l in inst.GetComponentsInChildren<Light>(true)) l.enabled = false;
            var an = inst.GetComponentInChildren<Animator>();
            if (an != null && an.runtimeAnimatorController != null && an.runtimeAnimatorController.animationClips.Length > 0)
                an.runtimeAnimatorController.animationClips[0].SampleAnimation(an.gameObject, 0.5f);
            if (prepare != null) prepare(inst);

            var camGo = new GameObject("PortraitCam");
            var cam = camGo.AddComponent<Camera>();
            cam.transform.position = origin + camPos;
            cam.transform.LookAt(origin + lookAt);
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 30f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.allowHDR = false; cam.allowMSAA = true;
            var camData = cam.GetUniversalAdditionalCameraData();
            if (camData != null) camData.renderPostProcessing = false;
            // Ohne Tonemapping würde HDR-Emission (> 1) ins Gelbe/Weiße klippen → für das Porträt auf LDR normieren
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || !mats[i].IsKeywordEnabled("_EMISSION")) continue;
                    Color e = mats[i].GetColor("_EmissionColor");
                    float max = Mathf.Max(e.r, Mathf.Max(e.g, e.b));
                    if (max <= 1f) continue;
                    var mpb = new MaterialPropertyBlock();
                    mpb.SetColor("_EmissionColor", e / max * 1.1f);
                    r.SetPropertyBlock(mpb, i);
                }
            }
            var keyGo = new GameObject("PortraitKey");
            var key = keyGo.AddComponent<Light>();
            key.type = LightType.Directional; key.intensity = 0.9f; key.color = new Color(1f, 0.96f, 0.9f);
            keyGo.transform.rotation = Quaternion.LookRotation((lookAt - camPos).normalized + Vector3.down * 0.5f + Vector3.right * 0.3f);

            var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            var black = Grab(cam, rt, Color.black, size);
            var white = Grab(cam, rt, Color.white, size);
            var outTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            var b = black.GetPixels(); var w = white.GetPixels();
            // Hintergrund nach Tonemapping: Ecke oben rechts (leer) als Referenz für „voll transparent"
            float bgDiff = Mathf.Max(0.05f, (w[px.Length - 1].r - b[px.Length - 1].r + w[px.Length - 1].g - b[px.Length - 1].g + w[px.Length - 1].b - b[px.Length - 1].b) / 3f);
            for (int i = 0; i < px.Length; i++)
            {
                float a = Mathf.Clamp01(1f - (w[i].r - b[i].r + w[i].g - b[i].g + w[i].b - b[i].b) / 3f / bgDiff);
                px[i] = a > 0.004f ? new Color(Mathf.Clamp01(b[i].r / a), Mathf.Clamp01(b[i].g / a), Mathf.Clamp01(b[i].b / a), a) : new Color(0f, 0f, 0f, 0f);
            }
            outTex.SetPixels(px);
            outTex.Apply();
            File.WriteAllBytes(path, outTex.EncodeToPNG());

            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(black); Object.DestroyImmediate(white); Object.DestroyImmediate(outTex);
            Object.DestroyImmediate(camGo); Object.DestroyImmediate(keyGo); Object.DestroyImmediate(inst);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            var refImp = AssetImporter.GetAtPath(IconDir + "portrait_fusion_magma.png") as TextureImporter;
            if (imp != null)
            {
                if (refImp != null)
                {
                    var s = new TextureImporterSettings();
                    refImp.ReadTextureSettings(s);
                    imp.SetTextureSettings(s);
                }
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.alphaIsTransparency = true;
                imp.SaveAndReimport();
            }
        }

        internal static Texture2D Grab(Camera cam, RenderTexture rt, Color bg, int size)
        {
            cam.backgroundColor = bg;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            return tex;
        }

        // ---------------- Partikel-Materialien ----------------

        internal static Material SoftAdd => Load<Material>(VfxMat + "VFX_SoftDot_Add.mat");
        internal static Material SparkAdd => Load<Material>(VfxMat + "VFX_Spark_Add.mat");
        internal static Material StarAdd => Load<Material>(VfxMat + "VFX_Star_Add.mat");
        internal static Material RingAdd => Load<Material>(VfxMat + "VFX_Ring_Add.mat");
        internal static Material FlameAdd => Load<Material>(VfxMat + "VFX_Flame_Add.mat");
        internal static Material TrailAdd => Load<Material>(VfxMat + "VFX_Trail_Add.mat");
        internal static Material SmokeAlpha => Load<Material>(VfxMat + "VFX_Smoke_Alpha.mat");
        internal static Material SoftAlpha => Load<Material>(VfxMat + "VFX_SoftDot_Alpha.mat");
        internal static Material TrailAlpha => Load<Material>(VfxMat + "VFX_Trail_Alpha.mat");

        // ---------------- Partikel-Helfer ----------------

        // Grundsystem: Local Space, Hierarchy-Skalierung, ohne Form; loop = Dauer-Effekt mit Prewarm, sonst einmalig (2,5 s)
        internal static ParticleSystem PS(Transform parent, string name, Vector3 pos, Material mat, Vector2 life, Vector2 size, Color color, int max, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = loop; main.playOnAwake = true; main.prewarm = loop;
            main.duration = loop ? 5f : 2.5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startColor = color;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = max;
            var em = ps.emission; em.rateOverTime = 0f;
            var sh = ps.shape; sh.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        internal static void Rate(ParticleSystem ps, float rate) { var em = ps.emission; em.rateOverTime = rate; }

        internal static void Burst(ParticleSystem ps, int count, float time = 0f)
        {
            var em = ps.emission; em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(time, (short)count) });
        }

        internal static void NoAwake(ParticleSystem ps) { var m = ps.main; m.playOnAwake = false; }

        internal static void WorldSim(ParticleSystem ps) { var m = ps.main; m.simulationSpace = ParticleSystemSimulationSpace.World; }

        internal static void Flat(ParticleSystem ps)
        {
            ps.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        }

        internal static void Stretch(ParticleSystem ps, float velocityScale, float lengthScale)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = velocityScale; r.lengthScale = lengthScale;
        }

        internal static void MeshParticles(ParticleSystem ps, Mesh mesh)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh; r.mesh = mesh;
            r.alignment = ParticleSystemRenderSpace.Local;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            var m = ps.main; m.startRotation3D = true;
            m.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.separateAxes = true;
            rot.x = new ParticleSystem.MinMaxCurve(-6f, 6f); rot.y = new ParticleSystem.MinMaxCurve(-6f, 6f); rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
        }

        // Partikel-Meshes dürfen nur ein Submesh haben: Kopie mit zusammengeführten Submeshes als Asset
        // (inkl. FBX-Knotentransform: Blender-Export hat Rotation −90° X und Skalierung 100 am Knoten)
        internal static Mesh SingleSubmesh(string fbx, string path)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var mf = go != null ? go.GetComponentInChildren<MeshFilter>() : null;
            if (mf == null || mf.sharedMesh == null) return null;
            var src = mf.sharedMesh;
            var combine = new CombineInstance[src.subMeshCount];
            for (int i = 0; i < combine.Length; i++) combine[i] = new CombineInstance { mesh = src, subMeshIndex = i, transform = mf.transform.localToWorldMatrix };
            var m = new Mesh { name = Path.GetFileNameWithoutExtension(path) };
            m.CombineMeshes(combine, true, true);
            m.RecalculateBounds();
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(m, path); return m; }
            EditorUtility.CopySerialized(m, existing);
            Object.DestroyImmediate(m);
            return existing;
        }

        // Kollision mit der Welt (Boden), damit Brocken liegen bleiben statt durchzufallen
        internal static void Collide(ParticleSystem ps)
        {
            var c = ps.collision; c.enabled = true;
            c.type = ParticleSystemCollisionType.World; c.mode = ParticleSystemCollisionMode.Collision3D;
            c.dampen = 0.5f; c.bounce = 0.25f; c.quality = ParticleSystemCollisionQuality.Low;
            c.collidesWith = ~0 & ~(1 << Mathf.Max(0, LayerMask.NameToLayer("Buddy")));
        }

        // Ein Licht pro Partikel (Intensität/Farbe folgen dem Partikel → Blitz klingt ohne Skript ab)
        internal static void LightModule(ParticleSystem ps, float intensity, float range)
        {
            var go = new GameObject(ps.name + "Light");
            go.transform.SetParent(ps.transform, false);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = ps.main.startColor.color; l.range = range; l.intensity = intensity; l.shadows = LightShadows.None;
            go.SetActive(false);
            var lm = ps.lights; lm.enabled = true; lm.light = l; lm.ratio = 1f; lm.maxLights = 1;
            lm.useParticleColor = true; lm.sizeAffectsRange = false; lm.alphaAffectsIntensity = true;
            lm.intensityMultiplier = 1f; lm.rangeMultiplier = 1f;
        }

        internal static void Cone(ParticleSystem ps, float angle, float radius)
        {
            var sh = ps.shape; sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = angle; sh.radius = radius;
            sh.rotation = new Vector3(-90f, 0f, 0f);
        }

        internal static void Hemisphere(ParticleSystem ps, float radius)
        {
            var sh = ps.shape; sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = radius;
            sh.rotation = new Vector3(-90f, 0f, 0f);
        }

        internal static void Sphere(ParticleSystem ps, float radius) { Sphere(ps, radius, Vector3.one, 1f); }

        internal static void Sphere(ParticleSystem ps, float radius, Vector3 scale, float thickness)
        {
            var sh = ps.shape; sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = radius; sh.radiusThickness = thickness; sh.scale = scale;
        }

        // Kreis in der Bodenebene (XZ); Startgeschwindigkeit zeigt nach außen
        internal static void Ring(ParticleSystem ps, float radius, float thickness = 0f)
        {
            var sh = ps.shape; sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Circle;
            sh.radius = radius; sh.radiusThickness = thickness;
            sh.rotation = new Vector3(-90f, 0f, 0f);
        }

        internal static void Rise(ParticleSystem ps, float min, float max)
        {
            var v = ps.velocityOverLifetime; v.enabled = true;
            v.space = ParticleSystemSimulationSpace.Local;
            v.x = new ParticleSystem.MinMaxCurve(0f, 0f); v.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            v.y = new ParticleSystem.MinMaxCurve(min, max);
        }

        internal static void WorldRise(ParticleSystem ps, float min, float max)
        {
            var v = ps.velocityOverLifetime; v.enabled = true;
            v.space = ParticleSystemSimulationSpace.World;
            v.x = new ParticleSystem.MinMaxCurve(0f, 0f); v.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            v.y = new ParticleSystem.MinMaxCurve(min, max);
        }

        // Kreisen um die lokale Y-Achse (+ Steigen, optional nach außen)
        internal static void Orbit(ParticleSystem ps, float orbitalY, float up, float radial = 0f)
        {
            var v = ps.velocityOverLifetime; v.enabled = true;
            v.space = ParticleSystemSimulationSpace.Local;
            v.x = 0f; v.z = 0f; v.y = up;
            v.orbitalX = 0f; v.orbitalZ = 0f; v.orbitalY = orbitalY;
            v.radial = radial;
        }

        internal static void Drag(ParticleSystem ps, float drag)
        {
            var l = ps.limitVelocityOverLifetime; l.enabled = true; l.drag = drag; l.limit = 100f;
        }

        internal static void Noise(ParticleSystem ps, float strength, float freq)
        {
            var n = ps.noise; n.enabled = true; n.strength = strength; n.frequency = freq; n.scrollSpeed = 0.3f;
        }

        internal static void Spin(ParticleSystem ps, float degPerSec)
        {
            var m = ps.main; m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var r = ps.rotationOverLifetime; r.enabled = true;
            r.z = new ParticleSystem.MinMaxCurve(-degPerSec * Mathf.Deg2Rad, degPerSec * Mathf.Deg2Rad);
        }

        internal static void Trails(ParticleSystem ps, Material mat, float lifetime, float width, Color c)
        {
            var t = ps.trails; t.enabled = true;
            t.mode = ParticleSystemTrailMode.PerParticle;
            t.ratio = 1f; t.lifetime = lifetime; t.minVertexDistance = 0.04f;
            t.dieWithParticles = true; t.inheritParticleColor = true; t.sizeAffectsWidth = false;
            t.widthOverTrail = new ParticleSystem.MinMaxCurve(width, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            t.colorOverTrail = g;
            ps.GetComponent<ParticleSystemRenderer>().trailMaterial = mat;
        }

        internal static void SizeLife(ParticleSystem ps, float a, float b)
        {
            var s = ps.sizeOverLifetime; s.enabled = true;
            s.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, a, 1f, b));
        }

        internal static void SizeBump(ParticleSystem ps)
        {
            var s = ps.sizeOverLifetime; s.enabled = true;
            s.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0f)));
        }

        internal static void ColorLife(ParticleSystem ps, Gradient g) { var c = ps.colorOverLifetime; c.enabled = true; c.color = g; }

        internal static Gradient Fade(Color a, Color b, float peak = 1f, float inAt = 0.15f, float outAt = 0.7f)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peak, inAt), new GradientAlphaKey(peak, outAt), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        // Glut-Verlauf: hellgelb → orange → dunkelrot, blendet aus
        internal static Gradient Heat()
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.92f, 0.6f), 0f), new GradientColorKey(new Color(1f, 0.5f, 0.1f), 0.4f), new GradientColorKey(new Color(0.75f, 0.12f, 0.02f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            return g;
        }
    }
}
