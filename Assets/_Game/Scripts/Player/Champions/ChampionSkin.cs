using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Wendet Shop-Items (ShopItemSO) auf ein Champion-Modell an – Spielfigur wie Menü-Vorschau. Hängt sich selbst an die
    // ChampionVisual (ChampionSkin.For / ApplyTo), braucht also keine Prefab-Änderung.
    // - Waffe: Ersatz-Attachments an ChampionVisual.SetWeaponOverrides (Standard-Attachments bleiben unverändert; neu
    //   angehängt, Zweihand-Griff/Tragehaltung werden dort neu verbunden). Vor dem ersten Attach wird nur gemerkt.
    // - Rüstung: Material-Slots aller Modell-Renderer (ohne Waffen/Zusatzteile/Partikel) nach Namen ersetzen, Originale
    //   gemerkt und wiederhergestellt; Zusatzteile (mit ihren Effekt-Kindern) an Humanoid-Knochen. Auf deaktivierten
    //   Visuals werden die Zusatzteile beim nächsten Aktivieren angehängt.
    [DisallowMultipleComponent]
    public class ChampionSkin : MonoBehaviour
    {
        public ChampionVisual Visual { get; private set; }
        public ShopItemSO Weapon { get; private set; }
        public ShopItemSO Armor { get; private set; }

        private bool _weaponApplied;  // Weapon ist an der Visual gesetzt (auch null = Standard)
        private bool _armorApplied;
        private bool _addOnsPending;  // Zusatzteile beim nächsten OnEnable anhängen
        private readonly Dictionary<Renderer, Material[]> _originals = new Dictionary<Renderer, Material[]>();
        private readonly List<GameObject> _addOns = new List<GameObject>();

        // Skin-Komponente einer Visual (wird bei Bedarf angelegt)
        public static ChampionSkin For(ChampionVisual visual)
        {
            if (visual == null) return null;
            var skin = visual.GetComponent<ChampionSkin>();
            if (skin == null) skin = visual.gameObject.AddComponent<ChampionSkin>();
            skin.Visual = visual;
            return skin;
        }

        // Ausrüstung einer Klasse aus dem gepackten Loadout (ShopRules) auf alle Visuals dieser Klasse unter root anwenden.
        // Items werden über den Katalog aufgelöst; unbekannt/0 = Standard.
        public static void ApplyTo(GameObject root, ChampionClass cls, long loadout)
        {
            if (root == null) return;
            var weapon = ShopCatalogSO.FindItem(cls, ShopSlot.Weapon, ShopRules.Get(loadout, (int)cls, (int)ShopSlot.Weapon));
            var armor = ShopCatalogSO.FindItem(cls, ShopSlot.Armor, ShopRules.Get(loadout, (int)cls, (int)ShopSlot.Armor));
            ApplyTo(root, cls, weapon, armor);
        }

        // Bestimmte Items auf alle Visuals der Klasse unter root anwenden (z. B. Anprobe im Menü)
        public static void ApplyTo(GameObject root, ChampionClass cls, ShopItemSO weapon, ShopItemSO armor)
        {
            if (root == null) return;
            foreach (var v in root.GetComponentsInChildren<ChampionVisual>(true))
                if (v != null && v.Class == cls) For(v).Apply(weapon, armor);
        }

        void Awake()
        {
            if (Visual == null) Visual = GetComponent<ChampionVisual>();
        }

        void OnEnable()
        {
            if (_addOnsPending) SpawnAddOns();
        }

        // Waffe und Rüstung setzen (null = Standard). Items falscher Klasse/falschen Slots gelten als Standard.
        public void Apply(ShopItemSO weapon, ShopItemSO armor)
        {
            if (Visual == null) Visual = GetComponent<ChampionVisual>();
            if (Visual == null) return;
            if (weapon != null && (weapon.Slot != ShopSlot.Weapon || weapon.Class != Visual.Class)) weapon = null;
            if (armor != null && (armor.Slot != ShopSlot.Armor || armor.Class != Visual.Class)) armor = null;

            // Waffe vor Rüstung: alte Waffen sind dann schon aus der Hierarchie gelöst
            if (!_weaponApplied || weapon != Weapon) ApplyWeapon(weapon);
            if (!_armorApplied || armor != Armor) ApplyArmor(armor);
        }

        // Zurück zur Standardausrüstung (exakt Originalzustand)
        public void ResetToStandard() => Apply(null, null);

        // ---------------- Waffe ----------------

        private void ApplyWeapon(ShopItemSO item)
        {
            Weapon = item;
            _weaponApplied = true;
            if (item == null)
            {
                Visual.SetWeaponOverrides(null, null, null);
                return;
            }
            Visual.SetWeaponOverrides(
                Build(item.RightHand, Visual.RightHandWeapon),
                Build(item.LeftHand, Visual.LeftHandWeapon),
                Build(item.Back, Visual.BackItem));
        }

        // Ersatz-Attachment: Prefab aus dem Item, Lage aus dem Item oder vom Standard-Sockel; kein Prefab → null (Standard)
        private static ChampionVisual.Attachment Build(ShopItemSO.WeaponOverride o, ChampionVisual.Attachment standard)
        {
            if (o == null || o.Prefab == null) return null;
            var a = new ChampionVisual.Attachment { Prefab = o.Prefab };
            if (o.OverrideTransform || standard == null)
            {
                a.LocalPosition = o.LocalPosition;
                a.LocalEuler = o.LocalEuler;
                a.WorldScale = o.WorldScale;
            }
            else
            {
                a.LocalPosition = standard.LocalPosition;
                a.LocalEuler = standard.LocalEuler;
                a.WorldScale = standard.WorldScale;
            }
            return a;
        }

        // ---------------- Rüstung ----------------

        private void ApplyArmor(ShopItemSO item)
        {
            Armor = item;
            _armorApplied = true;
            RestoreMaterials();
            ClearAddOns();
            if (item == null) return;
            SwapMaterials(item.Materials);
            if (gameObject.activeInHierarchy) SpawnAddOns();
            else _addOnsPending = true;
        }

        private void RestoreMaterials()
        {
            foreach (var kv in _originals)
                if (kv.Key != null) kv.Key.sharedMaterials = kv.Value;
            _originals.Clear();
        }

        private void SwapMaterials(ShopItemSO.MaterialSwap[] swaps)
        {
            if (swaps == null || swaps.Length == 0) return;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (!IsBodyRenderer(r)) continue;
                var mats = r.sharedMaterials;
                Material[] changed = null;
                for (int i = 0; i < mats.Length; i++)
                {
                    var rep = Replacement(swaps, mats[i]);
                    if (rep == null) continue;
                    if (changed == null) changed = (Material[])mats.Clone();
                    changed[i] = rep;
                }
                if (changed == null) continue;
                _originals[r] = mats;
                r.sharedMaterials = changed;
            }
        }

        private static Material Replacement(ShopItemSO.MaterialSwap[] swaps, Material m)
        {
            if (m == null) return null;
            string name = BaseName(m.name);
            foreach (var s in swaps)
                if (s != null && s.Replacement != null && !string.IsNullOrEmpty(s.SourceMaterial) && BaseName(s.SourceMaterial) == name)
                    return s.Replacement;
            return null;
        }

        // Materialname ohne „ (Instance)“-Anhänge
        public static string BaseName(string n)
        {
            if (n == null) return "";
            const string suffix = " (Instance)";
            while (n.EndsWith(suffix)) n = n.Substring(0, n.Length - suffix.Length);
            return n.Trim();
        }

        // Renderer des Modells selbst – nicht Waffen, Zusatzteile oder Partikel
        private bool IsBodyRenderer(Renderer r)
        {
            if (r == null || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) return false;
            var t = r.transform;
            if (Under(t, Visual.RightHandInstance) || Under(t, Visual.LeftHandInstance) || Under(t, Visual.BackInstance)) return false;
            foreach (var a in _addOns)
                if (a != null && Under(t, a.transform)) return false;
            return true;
        }

        private static bool Under(Transform t, Transform root) => root != null && (t == root || t.IsChildOf(root));

        // ---------------- Zusatzteile ----------------

        private void SpawnAddOns()
        {
            _addOnsPending = false;
            ClearAddOns();
            if (Armor == null || Armor.AddOns == null || Armor.AddOns.Length == 0) return;
            var anim = Visual.Animator != null ? Visual.Animator : GetComponentInChildren<Animator>(true);
            if (anim == null || !anim.isHuman) return;
            float figure = FigureScale();
            foreach (var a in Armor.AddOns)
            {
                if (a == null || a.Prefab == null) continue;
                var bone = anim.GetBoneTransform(a.Bone);
                if (bone == null) continue;
                var go = Instantiate(a.Prefab, bone);
                go.name = "Skin_" + a.Prefab.name;
                // Wie ChampionVisual.Spawn: Versatz in Metern, Größe unabhängig von der Skalierung des Rigs
                float boneScale = Mathf.Max(0.0001f, bone.lossyScale.x);
                Vector3 pos = a.LocalPosition;
                Vector3 euler = a.LocalEuler;
                Vector3 scale = Vector3.Scale(a.Prefab.transform.localScale, a.LocalScale);
                if (a.Mirror)
                {
                    // Spiegelung an der YZ-Ebene des Knochens: x-Versatz und Drehungen um Y/Z umkehren, Größe in x negativ
                    pos.x = -pos.x;
                    euler.y = -euler.y;
                    euler.z = -euler.z;
                    scale.x = -scale.x;
                }
                // Werte gelten für die Spielfigur (Größe 1); kleinere Figuren (Menü-Vorschau 0,84) anteilig wie ihre Waffen
                go.transform.localPosition = pos * figure / boneScale;
                go.transform.localRotation = Quaternion.Euler(euler);
                go.transform.localScale = scale * figure / boneScale;
                // Zusatzteile sind reine Optik
                foreach (var c in go.GetComponentsInChildren<Collider>(true)) DestroySafe(c);
                _addOns.Add(go);
            }
        }

        // Größe der Figur relativ zur Spielfigur: Weltgröße der Standardwaffe (Player 1, Menü-Vorschau 0,84)
        private float FigureScale()
        {
            foreach (var a in new[] { Visual.RightHandWeapon, Visual.LeftHandWeapon, Visual.BackItem })
                if (a != null && a.Prefab != null && a.WorldScale > 0f) return a.WorldScale;
            return 1f;
        }

        private void ClearAddOns()
        {
            foreach (var go in _addOns)
            {
                if (go == null) continue;
                go.SetActive(false);
                go.transform.SetParent(null, false);
                DestroySafe(go);
            }
            _addOns.Clear();
        }

        private static void DestroySafe(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }
}
