using UnityEditor;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // Richtet das Player-Prefab für die drei Champions ein: Visual_Mage/_Knight/_Archer (Blender-Modelle, Humanoid),
    // Animator-Controller, ChampionVisual mit Waffen-Sockeln, die drei Kits (Werte, Effekte, Icons, Sounds) und den
    // Pfeil-Abschusspunkt. Menü: BuddyTD → Champion-Player einrichten. Idempotent (vorhandene Kits/Visuals werden aktualisiert).
    public static class ChampionPlayerSetup
    {
        private const string PlayerPrefab = "Assets/_Game/Prefabs/Player.prefab";
        private const string Models = "Assets/_Game/Models/Characters/";
        private const string Weapons = "Assets/_Game/Models/Characters/Weapons/";
        private const string Icons = "Assets/_Game/UI/Icons/";
        private const string Audio = "Assets/_Game/Audio/";
        private const string Vfx = "Assets/_Game/VFX/Prefabs/";

        // Einheitliche Modell-Skalierung (Blender-Figuren ~2.1–2.4 m → ~1.8 m wie der bisherige Magier)
        public const float ModelScale = 0.76f;
        // Füße auf der Unterkante des CharacterControllers (center 0, height 2)
        public const float FootY = -1f;
        private const string IdleClip = "Assets/StarterAssets/ThirdPersonController/Character/Animations/Stand--Idle.anim.fbx";

        [MenuItem("BuddyTD/Champion-Player einrichten")]
        public static void Setup()
        {
            var root = PrefabUtility.LoadPrefabContents(PlayerPrefab);
            try
            {
                SetupVisuals(root);
                SetupKits(root);
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefab);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("ChampionPlayerSetup: Player-Prefab mit drei Champions eingerichtet.");
        }

        // ---------------- Visuals ----------------

        private static void SetupVisuals(GameObject root)
        {
            // Altes Meshy-Visual entfernen (wird durch Visual_Mage ersetzt)
            var old = root.transform.Find("Visual");
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var mage = Visual(root, "Visual_Mage", "MageChampion.fbx", ChampionClass.Mage, "Assets/_Game/Animations/PlayerVisual.controller");
            var knight = Visual(root, "Visual_Knight", "KnightChampion.fbx", ChampionClass.Knight, "Assets/_Game/Animations/KnightVisual.controller");
            var archer = Visual(root, "Visual_Archer", "ArcherChampion.fbx", ChampionClass.Archer, "Assets/_Game/Animations/ArcherVisual.controller");

            Vector3 fwd = Vector3.forward, up = Vector3.up, right = Vector3.right;
            // Stab: senkrecht. Schwert: Klinge (+Y) schräg nach vorne oben, Schneide (X) senkrecht
            Socket(mage, mage.RightHandWeapon, HumanBodyBones.RightHand, Weapons + "Staff.fbx", Quaternion.LookRotation(fwd, up), Vector3.zero);
            Vector3 blade = (fwd + up * 0.35f).normalized;
            Socket(knight, knight.RightHandWeapon, HumanBodyBones.RightHand, Weapons + "Sword.fbx", Quaternion.LookRotation(right, blade), Vector3.zero);
            // Schild: Vorderseite (+Z, Blender −Y) nach vorne
            Socket(knight, knight.LeftHandWeapon, HumanBodyBones.LeftHand, Weapons + "Shield.fbx", Quaternion.LookRotation(fwd, up), new Vector3(-0.05f, 0.1f, 0.12f));
            // Bogen: senkrecht (+Y), Wölbung (−X) nach vorne
            Socket(archer, archer.LeftHandWeapon, HumanBodyBones.LeftHand, Weapons + "Bow.fbx", Quaternion.LookRotation(right, up), Vector3.zero);
            archer.BackItem.Prefab = null;

            mage.gameObject.SetActive(true);
            knight.gameObject.SetActive(false);
            archer.gameObject.SetActive(false);

            // Pfeil-Abschusspunkt am Root (nicht am Knochen)
            var spawn = root.transform.Find("ArrowSpawn");
            if (spawn == null)
            {
                spawn = new GameObject("ArrowSpawn").transform;
                spawn.SetParent(root.transform, false);
            }
            spawn.localPosition = new Vector3(0.15f, 0.25f, 0.7f);
            spawn.localRotation = Quaternion.identity;
        }

        private static ChampionVisual Visual(GameObject root, string name, string fbx, ChampionClass cls, string controller)
        {
            var existing = root.transform.Find(name);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Models + fbx);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            go.name = name;
            go.transform.localPosition = new Vector3(0f, FootY, 0f);
            go.transform.localRotation = Quaternion.identity; // Champion-FBX schauen bereits nach +Z (Render-Test: mit 180° sah man den Rücken)
            go.transform.localScale = Vector3.one * ModelScale;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            var anim = go.GetComponent<Animator>();
            if (anim == null) anim = go.AddComponent<Animator>();
            anim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controller);
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var cv = go.AddComponent<ChampionVisual>();
            cv.Class = cls;
            cv.Animator = anim;
            return cv;
        }

        // Waffe so ausrichten, dass sie in der Idle-Pose (StarterAssets Idle) die gewünschte Weltrotation hat und im Handteller sitzt.
        // Gemessen an einer temporären, per AnimationMode gesampelten Szenen-Instanz des Modells.
        private static void Socket(ChampionVisual cv, ChampionVisual.Attachment a, HumanBodyBones boneId, string prefabPath, Quaternion worldRot, Vector3 extraOffset)
        {
            a.Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            a.WorldScale = ModelScale;
            var model = PrefabUtility.GetCorrespondingObjectFromSource(cv.gameObject);
            if (model == null) return;

            var temp = (GameObject)Object.Instantiate(model);
            temp.hideFlags = HideFlags.HideAndDontSave;
            temp.transform.SetPositionAndRotation(new Vector3(0f, -300f, 0f), Quaternion.identity);
            temp.transform.localScale = Vector3.one * ModelScale;
            var anim = temp.GetComponent<Animator>();
            AnimationClip idle = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(IdleClip))
                if (o is AnimationClip && !o.name.StartsWith("__preview")) idle = (AnimationClip)o;
            bool sampled = false;
            try
            {
                if (idle != null)
                {
                    if (!AnimationMode.InAnimationMode()) { AnimationMode.StartAnimationMode(); sampled = true; }
                    AnimationMode.SampleAnimationClip(temp, idle, 1f);
                }
                var bone = anim != null ? anim.GetBoneTransform(boneId) : null;
                if (bone == null) return;
                // Handteller: zwischen Handgelenk und Mittelfinger-Grundgelenk
                var finger = anim.GetBoneTransform(boneId == HumanBodyBones.RightHand ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
                Vector3 palm = finger != null ? Vector3.Lerp(bone.position, finger.position, 0.55f) : bone.position;
                a.LocalEuler = (Quaternion.Inverse(bone.rotation) * worldRot).eulerAngles;
                a.LocalPosition = Quaternion.Inverse(bone.rotation) * (palm - bone.position + extraOffset);
            }
            finally
            {
                if (sampled) AnimationMode.StopAnimationMode();
                Object.DestroyImmediate(temp);
            }
        }

        // ---------------- Kits ----------------

        private static T Get<T>(GameObject root) where T : Component
        {
            var c = root.GetComponent<T>();
            return c != null ? c : root.AddComponent<T>();
        }

        private static GameObject Prefab(string path) { return AssetDatabase.LoadAssetAtPath<GameObject>(path); }
        private static GameObject Champ(string name) { return Prefab(ChampionFxBuilder.Dir + name + ".prefab"); }
        private static GameObject ByGuid(string guid) { return Prefab(AssetDatabase.GUIDToAssetPath(guid)); }
        private static Sprite Icon(string name) { return AssetDatabase.LoadAssetAtPath<Sprite>(Icons + name + ".png"); }

        private static ChampionSfx Sfx(string clip, float volume, float pMin, float pMax, float interval = 0.05f)
        {
            return new ChampionSfx { Clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Audio + clip), Volume = volume, Pitch = new Vector2(pMin, pMax), MinInterval = interval };
        }

        private static ChampionKit.AbilityDisplay D(AbilityId id, string name, string icon)
        {
            return new ChampionKit.AbilityDisplay { Id = id, Name = name, Icon = Icon(icon) };
        }

        private static void SetupKits(GameObject root)
        {
            var spawn = root.transform.Find("ArrowSpawn");

            // ---- Magier (Werte wie bisher in PlayerAbilities) ----
            var mage = Get<MageKit>(root);
            mage.DisplayName = "Magier";
            if (mage.ArcaneBallPrefab == null) mage.ArcaneBallPrefab = Prefab("Assets/_Game/Prefabs/ArcaneBall.prefab");
            mage.ObstacleLayer = 1;
            if (mage.FireWave.CastEffectPrefab == null) mage.FireWave.CastEffectPrefab = ByGuid("423d320dc682b40869f3426fb5709dfa");
            if (mage.FireWave.BurnVfxPrefab == null) mage.FireWave.BurnVfxPrefab = ByGuid("9b2f858c065484439a8507ccf83cb5c5");
            if (mage.FrostNova.CastEffectPrefab == null) mage.FrostNova.CastEffectPrefab = ByGuid("c02a290f0d01640608733aabe636d97b");
            if (mage.FrostNova.FrozenVfxPrefab == null) mage.FrostNova.FrozenVfxPrefab = ByGuid("4a39c28ab0eae4e2baa0c34042ff2038");
            if (mage.StoneWall.WallPrefab == null) mage.StoneWall.WallPrefab = ByGuid("17db8f5d19f584c018be6fdd6251bf58");
            if (mage.HolyCircle.CastEffectPrefab == null) mage.HolyCircle.CastEffectPrefab = ByGuid("7862d9904f5224fd0bfc5b85d9c4d895");
            if (mage.HolyCircle.BlindVfxPrefab == null) mage.HolyCircle.BlindVfxPrefab = ByGuid("e8dcdc109ff9c4c4f978531732e0bfd3");
            mage.Abilities = new System.Collections.Generic.List<ChampionKit.AbilityDisplay>
            {
                D(AbilityId.ArcaneBall, "Arkanball", "ability_arcane"),
                D(AbilityId.Blink, "Blinzeln", "ability_blink"),
                D(AbilityId.FireWave, "Flammenwelle", "emblem_fire"),
                D(AbilityId.FrostNova, "Frostnova", "emblem_ice"),
                D(AbilityId.StoneWall, "Steinwall", "emblem_earth"),
                D(AbilityId.HolyCircle, "Heiliger Kreis", "emblem_light"),
            };

            GameObject burn = mage.FireWave.BurnVfxPrefab, frozen = mage.FrostNova.FrozenVfxPrefab, blind = mage.HolyCircle.BlindVfxPrefab;

            // ---- Schwertkämpfer ----
            var k = Get<KnightKit>(root);
            k.DisplayName = "Schwertkämpfer";
            k.SlashFxPrefab = Champ("VFX_SwordSlash");
            k.SlashColor = new Color(1.3f, 1.45f, 1.8f, 1f);
            k.FinisherColor = new Color(2f, 1.5f, 0.45f, 1f);
            k.HitFxPrefab = Champ("VFX_SwordHit");
            k.BlockSparkPrefab = Champ("VFX_BlockSpark");
            k.BlockGlowPrefab = Champ("VFX_BlockGlow");
            k.FlameWhirl.CastEffectPrefab = Champ("VFX_FlameWhirl");
            k.FlameWhirl.EffectLifetime = 2.5f;
            k.FlameWhirl.ArcFxPrefab = Champ("VFX_SwordSlash");
            k.FlameWhirl.BurnVfxPrefab = burn;
            k.FrostStrike.CastEffectPrefab = Champ("VFX_FrostStrike");
            k.FrostStrike.EffectLifetime = 2.5f;
            k.FrostStrike.FrozenVfxPrefab = frozen;
            k.Earthquake.CastEffectPrefab = Champ("VFX_Earthquake");
            k.Earthquake.EffectLifetime = 2.5f;
            k.Earthquake.StunVfxPrefab = blind;
            k.LightOath.CastEffectPrefab = Champ("VFX_LightOath");
            k.LightOath.EffectLifetime = 2.5f;
            k.LightOath.AuraPrefab = Champ("VFX_LightOathAura");
            k.SwingSfx = Sfx("AIPfeilsSchuss.wav", 0.45f, 1.25f, 1.45f);
            k.FinisherSfx = Sfx("AIPfeilsSchuss.wav", 0.6f, 0.8f, 0.9f);
            k.BlockHitSfx = Sfx("Pfeiltreffer.mp3", 0.6f, 0.6f, 0.72f, 0.2f);
            k.BlockBreakSfx = Sfx("explosion.wav", 0.45f, 1.5f, 1.7f);
            k.Abilities = new System.Collections.Generic.List<ChampionKit.AbilityDisplay>
            {
                D(AbilityId.SwordSlash, "Schwerthieb", "ability_knight_slash"),
                D(AbilityId.ShieldBlock, "Schildblock", "ability_knight_block"),
                D(AbilityId.FlameWhirl, "Flammenwirbel", "ability_knight_flamewhirl"),
                D(AbilityId.FrostStrike, "Frostschlag", "ability_knight_froststrike"),
                D(AbilityId.Earthquake, "Erdbeben", "ability_knight_earthquake"),
                D(AbilityId.LightOath, "Lichtschwur", "ability_knight_lightoath"),
            };

            // ---- Bogenschütze ----
            var a = Get<ArcherKit>(root);
            a.DisplayName = "Bogenschütze";
            a.ProjectileSpawn = spawn;
            a.ArrowPrefab = Champ("Arrow");
            a.ArrowHitFxPrefab = Champ("VFX_ArrowHit");
            a.RollDustPrefab = Champ("VFX_RollDust");
            a.FireArrowRain.CastEffectPrefab = Champ("VFX_FireRainArea");
            a.FireArrowRain.ScaleEffectBySize = true;
            a.FireArrowRain.EffectLifetime = 2.5f;
            a.FireArrowRain.FallingArrowPrefab = Champ("FireArrow_Falling");
            a.FireArrowRain.ImpactFxPrefab = Champ("VFX_FireImpact");
            a.FireArrowRain.BurnVfxPrefab = burn;
            a.FrostArrow.ArrowPrefab = Champ("FrostArrow");
            a.FrostArrow.CastEffectPrefab = Champ("VFX_FrostArrowCast");
            a.FrostArrow.EffectLifetime = 1.5f;
            a.FrostArrow.FrozenVfxPrefab = frozen;
            a.ThornTrap.TrapPrefab = Champ("ThornTrap");
            a.ThornTrap.RootVfxPrefab = Champ("VFX_ThornRoot");
            a.ThornTrap.SnapFxPrefab = Champ("VFX_ThornSnap");
            a.LightArrow.BeamPrefab = Champ("VFX_LightBeam");
            a.LightArrow.HitFxPrefab = Prefab(Vfx + "LightHit.prefab");
            a.LightArrow.BlindVfxPrefab = blind;
            a.ShootSfx = Sfx("Pfeilschuss.mp3", 0.6f, 0.95f, 1.1f);
            a.ArrowHitSfx = Sfx("Pfeiltreffer.mp3", 0.55f, 0.95f, 1.1f);
            a.RollSfx = Sfx("AIPfeilsSchuss.wav", 0.35f, 0.55f, 0.65f);
            a.Abilities = new System.Collections.Generic.List<ChampionKit.AbilityDisplay>
            {
                D(AbilityId.ArrowShot, "Pfeilschuss", "ability_archer_shot"),
                D(AbilityId.Roll, "Rolle", "ability_archer_roll"),
                D(AbilityId.FireArrowRain, "Feuerpfeil-Regen", "ability_archer_firerain"),
                D(AbilityId.FrostArrow, "Frostpfeil", "ability_archer_frostarrow"),
                D(AbilityId.ThornTrap, "Dornenfalle", "ability_archer_trap"),
                D(AbilityId.LightArrow, "Lichtpfeil", "ability_archer_lightarrow"),
            };

            // Champion-Definitionen (Hauptmenü-Assets eines anderen Agenten), falls vorhanden
            foreach (var g in AssetDatabase.FindAssets("t:ChampionDefinitionSO"))
            {
                var def = AssetDatabase.LoadAssetAtPath<ChampionDefinitionSO>(AssetDatabase.GUIDToAssetPath(g));
                if (def == null) continue;
                if (def.Class == ChampionClass.Mage) mage.Definition = def;
                else if (def.Class == ChampionClass.Knight) k.Definition = def;
                else a.Definition = def;
            }

            EditorUtility.SetDirty(root);
        }
    }
}
