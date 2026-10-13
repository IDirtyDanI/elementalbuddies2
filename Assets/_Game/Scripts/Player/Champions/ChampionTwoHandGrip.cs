using UnityEngine;

namespace ElementalBuddies
{
    // Zweihand-Griff (Berserker-Axt): führt die freie linke Faust nach dem Animator und nach ChampionWeaponHold
    // (Freiraum/Handgelenk der Waffenhand) per Zwei-Knochen-IK auf den zweiten Griffpunkt der Waffe (Kind-Empty "Grip2").
    // Die Clips haben die Faust schon am Stiel – die IK gleicht nur Retargeting-/Korrektur-Abweichungen aus.
    // Ellenbogen-Ebene und Handdrehung bleiben aus der Animation. In FreeStates (z. B. Kampfschrei: Hand lässt los)
    // wird die IK weich ausgeblendet.
    [DefaultExecutionOrder(110)]
    public class ChampionTwoHandGrip : MonoBehaviour
    {
        public ChampionVisual Visual;
        [Tooltip("Name des Griffpunkts in der Waffe der rechten Hand.")]
        public string GripName = "Grip2";
        [Tooltip("Faustmitte (Röhre der Finger) im lokalen Raum des linken Handknochens – gemessen von ChampionPlayerSetup.")]
        public Vector3 PalmOffset = new Vector3(0f, 0.08f, 0.02f);
        [Tooltip("Achse der Faust-Röhre (kleiner Finger → Zeigefinger) im lokalen Raum des linken Handknochens; Null = Handdrehung bleibt ganz aus der Animation.")]
        public Vector3 PalmAxis;
        [Tooltip("Aktions-States, in denen die linke Hand den Stiel loslässt (IK aus).")]
        public string[] FreeStates = { "Roar" };
        [Tooltip("Aktions-Layer, deren aktueller State geprüft wird.")]
        public string[] ActionLayers = { "UpperBody", "FullBody" };
        public float FadeSpeed = 8f;
        [Tooltip("Reicht der gestreckte Arm nicht bis zum Griff (Haltungen an der Reichweitengrenze, Retargeting-Abweichung), " +
                 "zieht das Schlüsselbein die Schulter bis zu so vielen Grad nach.")]
        public float MaxShoulderAngle = 25f;

        private float _weight = 1f;
        private Transform _grip;
        private Transform _gripWeapon;
        private int[] _layerIdx;

        void OnEnable()
        {
            if (Visual == null) Visual = GetComponent<ChampionVisual>();
            _layerIdx = null;
        }

        void LateUpdate()
        {
            Apply(false, Time.deltaTime);
        }

        // immediate = true: Gewicht ohne Überblendung (Editor-Sampling, Clipping-Prüfung)
        public void Apply(bool immediate, float dt = 0f)
        {
            var anim = Visual != null ? Visual.Animator : null;
            if (anim == null || !anim.isHuman) return;
            var weapon = Visual.RightHandInstance;
            if (weapon == null) return;
            if (_gripWeapon != weapon) { _grip = FindDeep(weapon, GripName); _gripWeapon = weapon; }
            if (_grip == null) return;

            float target = Free(anim) ? 0f : 1f;
            _weight = immediate ? target : Mathf.MoveTowards(_weight, target, FadeSpeed * dt);
            if (_weight < 0.001f) return;

            var upper = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var lower = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            var hand = anim.GetBoneTransform(HumanBodyBones.LeftHand);
            if (upper == null || lower == null || hand == null) return;

            // Faust-Röhre am Stiel ausrichten (kleinste Drehung, Richtung wie animiert), dann die Hand so verschieben,
            // dass die Faustmitte auf dem Griffpunkt liegt
            Quaternion rot = hand.rotation;
            if (PalmAxis.sqrMagnitude > 1e-8f)
            {
                Vector3 axNow = rot * PalmAxis.normalized;
                Vector3 shaft = weapon.up;
                if (Vector3.Dot(axNow, shaft) < 0f) shaft = -shaft;
                rot = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(axNow, shaft), _weight) * rot;
            }
            Vector3 palm = hand.position + rot * Vector3.Scale(PalmOffset, hand.lossyScale);
            Vector3 want = hand.position + (_grip.position - palm) * _weight;
            // Ellenbogen-Hinweis: animierte Ellenbogen-Lage (weg von der Schulter-Hand-Achse) beibehalten
            Vector3 hint = lower.position + (lower.position - (upper.position + hand.position) * 0.5f);
            ReachWithShoulder(anim, upper, lower, hand, want, ref hint);
            ChampionWeaponHold.TwoBoneIK(upper, lower, hand, want, hint);
            hand.rotation = rot;
        }

        // Ziel außerhalb der Armlänge: Schlüsselbein in kleinen Schritten zum Ziel drehen, bis es reicht (max. MaxShoulderAngle)
        private void ReachWithShoulder(Animator anim, Transform upper, Transform lower, Transform hand, Vector3 want, ref Vector3 hint)
        {
            var shoulder = anim.GetBoneTransform(HumanBodyBones.LeftShoulder);
            if (shoulder == null || MaxShoulderAngle <= 0f) return;
            float reach = ((lower.position - upper.position).magnitude + (hand.position - lower.position).magnitude) * 0.995f;
            Vector3 s = shoulder.position, u = upper.position - s, w = want - s;
            if ((want - upper.position).magnitude <= reach || w.sqrMagnitude < 1e-6f) return;
            Vector3 axis = Vector3.Cross(u, w);
            if (axis.sqrMagnitude < 1e-8f) return;
            axis.Normalize();
            float angle = 0f;
            for (float a = 1f; a <= MaxShoulderAngle; a += 1f)
            {
                angle = a;
                if ((s + Quaternion.AngleAxis(a, axis) * u - want).magnitude <= reach) break;
            }
            Quaternion q = Quaternion.AngleAxis(angle * _weight, axis);
            Vector3 elbowOff = hint - shoulder.position;
            shoulder.rotation = q * shoulder.rotation;
            hint = shoulder.position + q * elbowOff;
        }

        // Läuft in einem Aktions-Layer ein State, in dem die Hand frei ist? (im Übergang zählt das Ziel)
        private bool Free(Animator anim)
        {
            if (FreeStates == null || FreeStates.Length == 0) return false;
            if (_layerIdx == null)
            {
                _layerIdx = new int[ActionLayers.Length];
                for (int i = 0; i < ActionLayers.Length; i++) _layerIdx[i] = anim.GetLayerIndex(ActionLayers[i]);
            }
            foreach (int l in _layerIdx)
            {
                if (l < 0) continue;
                var cur = anim.IsInTransition(l) ? anim.GetNextAnimatorStateInfo(l) : anim.GetCurrentAnimatorStateInfo(l);
                foreach (var s in FreeStates)
                    if (!string.IsNullOrEmpty(s) && cur.IsName(s)) return true;
            }
            return false;
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = FindDeep(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
