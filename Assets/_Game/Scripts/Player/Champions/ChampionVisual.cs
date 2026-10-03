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
