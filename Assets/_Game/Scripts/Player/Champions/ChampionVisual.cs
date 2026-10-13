using UnityEngine;

namespace ElementalBuddies
{
    // Modell eines Champions (Kind des Players, z. B. "Visual_Knight"). PlayerAbilities schaltet nur das Visual
    // der aktiven Klasse an. Waffen werden zur Laufzeit an die Hand-Knochen des Humanoid-Rigs gehängt
    // (Animator.GetBoneTransform) – neue Modelle brauchen also nur einen Humanoid-Avatar.
    public class ChampionVisual : MonoBehaviour
    {
        [System.Serializable]
        public class Attachment
        {
            public GameObject Prefab;
            [Tooltip("Lokale Position (Meter) / Rotation relativ zum Knochen.")]
            public Vector3 LocalPosition;
            public Vector3 LocalEuler;
            [Tooltip("Weltgröße (1 = Prefab-Originalgröße), unabhängig von der Skalierung des Rigs.")]
            public float WorldScale = 1f;
        }

        public ChampionClass Class;
        [Tooltip("Leer = Animator in diesem Objekt oder seinen Kindern.")]
        public Animator Animator;

        [Header("Waffen-Sockel")]
        public Attachment RightHandWeapon = new Attachment();
        public Attachment LeftHandWeapon = new Attachment();
        [Tooltip("Am Oberkörper (Chest/Spine), z. B. Köcher.")]
        public Attachment BackItem = new Attachment();

        [Header("Rumpf-Ausgleich (Lauf-Animationen)")]
        [Tooltip("Layer, deren Maske den Rumpf (Body) übernimmt, aber nicht die Beine/Hüfte – z. B. UpperBody (Angriffe), beim Berserker auch HoldArm. Wird vom Menü „Lauf-Animationen einbauen“ aus den Masken gesetzt.")]
        public string[] SpineOverrideLayers = { "UpperBody" };
        [Tooltip("Layer ohne Maske (ganzer Körper inkl. Hüfte), z. B. FullBody: dort kein Ausgleich.")]
        public string[] FullBodyLayers = { "FullBody" };

        public Transform RightHandInstance { get; private set; }
        public Transform LeftHandInstance { get; private set; }
        public Transform BackInstance { get; private set; }

        private bool _attached;
        private bool _editorPreview;

        // Waffen auch außerhalb des Play-Modus anhängen (Vorschau-Renders im Editor); Objekte danach selbst aufräumen
        public void AttachForPreview()
        {
            _editorPreview = true;
            Attach();
        }

        void Awake()
        {
            if (Animator == null) Animator = GetComponentInChildren<Animator>(true);
        }

        void OnEnable()
        {
            Attach();
        }

        // Waffen einmalig an die Knochen hängen (auch im Editor aufrufbar, z. B. für Vorschau-Renders)
        public void Attach()
        {
            if (_attached || !Application.isPlaying && !_editorPreview) return;
            if (Animator == null) Animator = GetComponentInChildren<Animator>(true);
            if (Animator == null || !Animator.isHuman) return;
            _attached = true;
            RightHandInstance = Spawn(RightHandWeapon, Animator.GetBoneTransform(HumanBodyBones.RightHand), "Weapon_R");
            LeftHandInstance = Spawn(LeftHandWeapon, Animator.GetBoneTransform(HumanBodyBones.LeftHand), "Weapon_L");
            Transform chest = Animator.GetBoneTransform(HumanBodyBones.UpperChest);
            if (chest == null) chest = Animator.GetBoneTransform(HumanBodyBones.Chest);
            if (chest == null) chest = Animator.GetBoneTransform(HumanBodyBones.Spine);
            BackInstance = Spawn(BackItem, chest, "BackItem");
        }

        // ---------------- Rumpf-Ausgleich ----------------
        // Die seitlichen Lauf-Clips drehen die Hüfte bis ~60° in Laufrichtung und den Rumpf (Spine/Chest/UpperChest) zurück,
        // damit Brust und Kopf zur Maus schauen. Überschreibt ein Layer den Rumpf mit einer eigenen Pose (Angriff, Trage-
        // Haltung), fehlt diese Gegendrehung: der Rumpf stünde relativ zur Hüfte wie im Clip, also um den Hüftwinkel verdreht,
        // und Hieb/Schuss gingen schräg. Darum nach dem Animator (vor der Waffen-IK) den Rumpf um −Hüftwinkel × Anteil der
        // überschreibenden Layer zurückdrehen. Hüftwinkel = Gier der Linie zwischen den Oberschenkeln relativ zur Figur.
        private int[] _spineLayerIdx, _fullLayerIdx;
        private RuntimeAnimatorController _layerIdxFor;
        public float LastTorsoCounter { get; private set; }

        public void CounterLocomotionTwist()
        {
            var a = Animator;
            LastTorsoCounter = 0f;
            if (a == null || !a.isHuman || a.runtimeAnimatorController == null) return;
            if (_layerIdxFor != a.runtimeAnimatorController || _spineLayerIdx == null)
            {
                _layerIdxFor = a.runtimeAnimatorController;
                _spineLayerIdx = LayerIndices(a, SpineOverrideLayers);
                _fullLayerIdx = LayerIndices(a, FullBodyLayers);
            }
            // Anteil, mit dem Rumpf-Layer (nach Layer-Reihenfolge) die Rumpfpose bestimmen; Ganzkörper-Layer heben ihn auf
            float w = 0f;
            for (int i = 1; i < a.layerCount; i++)
            {
                bool spine = System.Array.IndexOf(_spineLayerIdx, i) >= 0;
                bool full = !spine && System.Array.IndexOf(_fullLayerIdx, i) >= 0;
                if (!spine && !full) continue;
                float li = a.GetLayerWeight(i) * LayerActivity(a, i);
                if (li <= 0f) continue;
                w = spine ? w * (1f - li) + li : w * (1f - li);
            }
            if (w < 0.001f) return;

            var lLeg = a.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            var rLeg = a.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            if (lLeg == null || rLeg == null) return;
            Vector3 up = a.transform.up;
            Vector3 across = Vector3.ProjectOnPlane(rLeg.position - lLeg.position, up);
            if (across.sqrMagnitude < 1e-6f) return;
            float hips = Vector3.SignedAngle(Vector3.ProjectOnPlane(a.transform.right, up), across, up);
            hips = Mathf.Clamp(hips, -80f, 80f);
            float total = -hips * w;
            LastTorsoCounter = total;
            if (Mathf.Abs(total) < 0.1f) return;

            var spineT = a.GetBoneTransform(HumanBodyBones.Spine);
            var chestT = a.GetBoneTransform(HumanBodyBones.Chest);
            var upperT = a.GetBoneTransform(HumanBodyBones.UpperChest);
            if (upperT != null)
            {
                Turn(spineT, total * 0.4f, up);
                Turn(chestT, total * 0.3f, up);
                Turn(upperT, total * 0.3f, up);
            }
            else
            {
                Turn(spineT, total * 0.5f, up);
                Turn(chestT, total * 0.5f, up);
            }
        }

        private static void Turn(Transform t, float angle, Vector3 up)
        {
            if (t != null) t.rotation = Quaternion.AngleAxis(angle, up) * t.rotation;
        }

        // 0 = Layer spielt nichts (leerer State), 1 = Clip aktiv; während Übergängen anteilig
        private static float LayerActivity(Animator a, int layer)
        {
            float cur = a.GetCurrentAnimatorClipInfoCount(layer) > 0 ? 1f : 0f;
            if (!a.IsInTransition(layer)) return cur;
            float next = a.GetNextAnimatorClipInfoCount(layer) > 0 ? 1f : 0f;
            return Mathf.Lerp(cur, next, a.GetAnimatorTransitionInfo(layer).normalizedTime);
        }

        private static int[] LayerIndices(Animator a, string[] names)
        {
            var list = new System.Collections.Generic.List<int>();
            if (names != null)
                foreach (var n in names)
                {
                    int i = string.IsNullOrEmpty(n) ? -1 : a.GetLayerIndex(n);
                    if (i > 0) list.Add(i);
                }
            return list.ToArray();
        }

        private static Transform Spawn(Attachment a, Transform bone, string name)
        {
            if (a == null || a.Prefab == null || bone == null) return null;
            var go = Instantiate(a.Prefab, bone);
            go.name = name;
            float boneScale = Mathf.Max(0.0001f, bone.lossyScale.x);
            go.transform.localPosition = a.LocalPosition / boneScale; // Versatz in Metern entlang der Knochen-Achsen
            go.transform.localRotation = Quaternion.Euler(a.LocalEuler);
            go.transform.localScale = a.Prefab.transform.localScale * (a.WorldScale / boneScale);
            // Waffen sind reine Optik: keine Collider, die Treffer/Physik stören
            foreach (var c in go.GetComponentsInChildren<Collider>(true))
            {
                if (Application.isPlaying) Destroy(c);
                else DestroyImmediate(c);
            }
            return go.transform;
        }
    }
}
