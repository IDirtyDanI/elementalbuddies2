using UnityEngine;

namespace ElementalBuddies
{
    // Zweihand-Griff (Berserker-Axt): führt die freie linke Faust nach dem Animator und nach ChampionWeaponHold
    // (Freiraum/Handgelenk der Waffenhand) per Zwei-Knochen-IK an den Stiel beim zweiten Griffpunkt (Kind-Empty "Grip2").
    // Die Clips (art-src/heroes2/20_berserker_anims.py) haben die Faust schon am Stiel und das Handgelenk natürlich
    // gehalten – die IK gleicht nur die Retargeting-Abweichung (~4–14 cm) aus, ohne das Handgelenk zu verbiegen:
    // Hand relativ zum Unterarm bleibt wie im Clip, die Faust-Röhre wird mit kleinster Drehung auf den Stiel gelegt,
    // der Ellenbogen folgt passend, Rest-Verdrehung teilen Unterarm und Handgelenk. Die Faust darf wie beim echten
    // Zweihand-Griff am Stiel gleiten (SlideMin/SlideMax; die Clips nutzen −0,18…+0,12 m, hier etwas mehr Spiel für das
    // Retargeting) und wird nur quer zum Stiel nachgezogen. In FreeStates (Kampfschrei: Hand lässt los) wird die IK
    // weich ausgeblendet.
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
        [Tooltip("Gleitbereich der Faust am Stiel relativ zum Griffpunkt (m, entlang der Waffenachse; 0/0 = fester Punkt).")]
        public float SlideMin = -0.2f;
        public float SlideMax = 0.12f;
        [Tooltip("Anteil der durch die IK zusätzlich entstehenden Handgelenk-Verdrehung, den der Unterarm übernimmt (0..1).")]
        [Range(0f, 1f)] public float TwistToForearm = 0.5f;
        [Tooltip("Ellenbogen so legen, dass der Unterarm zum Handgelenk-wie-im-Clip passt (sonst animierte Ellenbogen-Lage).")]
        public bool ElbowFromWrist = true;
        [Tooltip("IK-Durchläufe (Hand folgt dem korrigierten Unterarm).")]
        public int Iterations = 3;

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

            // Ausgangspose (Animation) merken: Gewicht < 1 blendet am Ende je Knochen zwischen Animation und Lösung
            var shoulder = anim.GetBoneTransform(HumanBodyBones.LeftShoulder);
            Quaternion shA = shoulder != null ? shoulder.localRotation : Quaternion.identity;
            Quaternion upA = upper.localRotation, loA = lower.localRotation, haA = hand.localRotation;

            // Handgelenk wie im Clip (dort natürlich gehalten: Beugung/Abknickung relativ zum Unterarm) – die IK soll es
            // nicht verbiegen. Bis 2026-10-14 behielt sie die Welt-Drehung der Hand bei und drehte nur den Arm: jede
            // Korrektur des Arms landete als Knick im Handgelenk.
            Quaternion relAnim = Quaternion.Inverse(lower.rotation) * hand.rotation;
            Vector3 foreLocal = Quaternion.Inverse(lower.rotation) * (hand.position - lower.position).normalized;
            float foreLen = (hand.position - lower.position).magnitude;
            Vector3 palmLocal = Vector3.Scale(PalmOffset, hand.lossyScale);
            Vector3 shaftDir = weapon.up;
            for (int it = 0; it < Mathf.Max(1, Iterations); it++)
            {
                // natürliche Hand zum aktuellen Unterarm, Faust-Röhre mit kleinster Drehung auf den Stiel
                Quaternion rot = lower.rotation * relAnim;
                if (PalmAxis.sqrMagnitude > 1e-8f)
                {
                    // Daumenseite immer zum Axtkopf (+Achse) wie in den Clips – kein Umklappen, wenn die Röhre
                    // zwischendurch quer steht
                    rot = Quaternion.FromToRotation(rot * PalmAxis.normalized, shaftDir) * rot;
                }
                // Faustmitte auf den nächsten Punkt des Griffbereichs (gleitet längs, wird nur quer nachgezogen)
                Vector3 palmOff = rot * palmLocal;
                float slide = Mathf.Clamp(Vector3.Dot(hand.position + palmOff - _grip.position, shaftDir), SlideMin, SlideMax);
                Vector3 want = _grip.position + shaftDir * slide - palmOff;
                // Ellenbogen so um die Schulter-Hand-Achse legen, dass der Unterarm dorthin zeigt, wo er bei
                // Handgelenk-wie-im-Clip läge
                Vector3 foreWant = (rot * Quaternion.Inverse(relAnim)) * foreLocal;
                Vector3 hint = ElbowFromWrist ? want - foreWant * foreLen
                                              : lower.position + (lower.position - (upper.position + hand.position) * 0.5f);
                if (it == 0) ReachWithShoulder(anim, upper, lower, hand, want, ref hint);
                ChampionWeaponHold.TwoBoneIK(upper, lower, hand, want, hint);
                hand.rotation = rot;
            }
            // Verdrehung, die die Ausrichtung am Stiel zusätzlich ins Handgelenk gebracht hat, an den Unterarm abgeben
            // (Unterarm-Drehung statt verdrehtem Handgelenk; die Hand behält ihre Welt-Drehung)
            if (TwistToForearm > 0f)
            {
                Quaternion d = Quaternion.Inverse(relAnim) * (Quaternion.Inverse(lower.rotation) * hand.rotation);
                Vector3 axL = Quaternion.Inverse(relAnim) * foreLocal;   // Unterarmachse im Raum der Clip-Hand
                float tw = TwistAngle(d, axL);
                Quaternion hRot = hand.rotation;
                lower.rotation = lower.rotation * Quaternion.AngleAxis(tw * TwistToForearm, foreLocal);
                hand.rotation = hRot;
            }
            if (_weight < 0.999f)
            {
                if (shoulder != null) shoulder.localRotation = Quaternion.Slerp(shA, shoulder.localRotation, _weight);
                upper.localRotation = Quaternion.Slerp(upA, upper.localRotation, _weight);
                lower.localRotation = Quaternion.Slerp(loA, lower.localRotation, _weight);
                hand.localRotation = Quaternion.Slerp(haA, hand.localRotation, _weight);
            }
        }

        // Drehwinkel (Grad, mit Vorzeichen) der Drehung q um die Achse axis (Swing-Twist-Zerlegung)
        private static float TwistAngle(Quaternion q, Vector3 axis)
        {
            axis.Normalize();
            Vector3 v = new Vector3(q.x, q.y, q.z);
            Vector3 p = Vector3.Dot(v, axis) * axis;
            var t = new Quaternion(p.x, p.y, p.z, q.w);
            float m = Mathf.Sqrt(t.x * t.x + t.y * t.y + t.z * t.z + t.w * t.w);
            if (m < 1e-6f) return 0f;
            float ang = 2f * Mathf.Atan2(Vector3.Dot(new Vector3(t.x, t.y, t.z) / m, axis), t.w / m) * Mathf.Rad2Deg;
            if (ang > 180f) ang -= 360f; else if (ang < -180f) ang += 360f;
            return ang;
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
            Quaternion q = Quaternion.AngleAxis(angle, axis);
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
