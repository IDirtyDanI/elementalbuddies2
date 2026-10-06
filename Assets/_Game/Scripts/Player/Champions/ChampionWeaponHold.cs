using UnityEngine;

namespace ElementalBuddies
{
    // Hält die Waffe eines Champions sauber in der Hand – läuft nach dem Animator (LateUpdate), also über jeder Animation:
    // 1) Tragehaltung: Während Locomotion/Sprung führt eine Zwei-Knochen-IK die Waffenhand zu einem Punkt relativ zur
    //    Hüfte und richtet die Waffenachse aus (Magier: Stab senkrecht rechts vor dem Körper, unten außerhalb der Robe).
    //    Spielt ein Aktions-Layer (Cast, Slash …) einen State ≠ „Empty“, wird die IK ausgeblendet – die Animation führt.
    // 2) Freiraum: Kommt die Waffe Kopf/Hut oder Beinen/Robe zu nahe (Kapseln um Knochen), dreht das Handgelenk sie heraus.
    // Eingerichtet von ChampionPlayerSetup; die Finger hält der Animator-Layer „Hands“ geschlossen.
    [DefaultExecutionOrder(100)]
    public class ChampionWeaponHold : MonoBehaviour
    {
        public ChampionVisual Visual;
        public bool RightHand = true;

        [Header("Tragehaltung (IK)")]
        public bool Carry = true;
        [Tooltip("Tragehaltung auch ohne Aktion (Locomotion/Sprung); false = IK nur in den gelisteten States (z. B. Zughand beim Schuss).")]
        public bool IdleCarry = true;
        [Tooltip("Hand ohne Waffe führen (nur Position, z. B. Zughand an die Sehne).")]
        public bool HandOnly;
        [Tooltip("Griffmitte relativ zum Modell-Ursprung (Füße): x seitlich (zur Waffenhand positiv), y Höhe, z nach vorne – Meter in Modell-Achsen.")]
        public Vector3 GripOffset = new Vector3(0.32f, 1.1f, 0.33f);
        [Tooltip("Knochen, dem die Tragehaltung folgt (Hüfte; Oberkörper z. B. UpperChest, wenn sich der Rumpf beugt).")]
        public HumanBodyBones FollowBone = HumanBodyBones.Hips;
        [Tooltip("Position des FollowBone in der Ruhepose (Modell-Achsen, Meter) – Bezug für HipsFollow.")]
        public Vector3 HipsRest = new Vector3(0f, 0.95f, 0f);
        [Tooltip("Wie stark der Griff Hüftbewegungen folgt (x seitlich, y Höhe, z vor/zurück). 0 = fest am Modell, 1 = fest an der Hüfte. " +
                 "Höhe voll mitnehmen (Ducken/Wippen), horizontal nur teilweise (sonst wandert der Griff beim Ducken in den Körper).")]
        public Vector3 HipsFollow = new Vector3(0.4f, 1f, 0.4f);
        [Tooltip("Waffenfuß bleibt so hoch über dem Boden (Modell-Ursprung); sonst kippt die Waffe nach vorne/außen (Ducken, Landung).")]
        public float GroundClearance = 0.03f;
        [Tooltip("Richtung, in die der Waffenfuß ausweicht (Modell-Achsen, x zur Waffenhand positiv).")]
        public Vector3 GroundEscape = new Vector3(0.6f, 0f, 1f);
        [Tooltip("Gewünschte Waffenachse (+Y der Waffe) in Modell-Achsen, x zur Waffenhand positiv.")]
        public Vector3 WeaponAxis = new Vector3(-0.05f, 1f, 0f);
        [Tooltip("Ellenbogen-Richtung relativ zur Schulter (Modell-Achsen, x zur Waffenhand positiv).")]
        public Vector3 ElbowHint = new Vector3(0.3f, -0.3f, -0.25f);
        [Tooltip("Gewünschte Richtung der Waffen-+Z-Achse (Modell-Achsen, x zur Waffenhand positiv); null = Drehung um die Waffenachse bleibt aus der Animation. " +
                 "Nötig bei flachen Waffen (Schild: Vorderseite = −Z) oder Bögen.")]
        public Vector3 WeaponForward;
        [Tooltip("Tragehaltung dreht mit der Hüfte um die Hochachse (Drehschlag, Rolle …) statt fest an den Modell-Achsen.")]
        public bool FollowHipsYaw;
        [Tooltip("Modell-Vorwärtsrichtung im Raum des Hüftknochens (Ruhepose) – Bezug für FollowHipsYaw.")]
        public Vector3 HipsRestForward = Vector3.forward;
        [Tooltip("Drehung des FollowBone relativ zum Modell in der Ruhepose (Euler) – Bezug für StateTarget.FullFollow.")]
        public Vector3 FollowRestEuler;
        [Tooltip("Aktions-Layer, die die Tragehaltung ausblenden.")]
        public string[] ActionLayers = { "UpperBody", "FullBody" };
        public float FadeSpeed = 8f;

        // Haltung während eines bestimmten Aktions-States (z. B. Schild bleibt beim Schwerthieb an der Seite, beim Block vor dem Körper).
        // Nicht gelistete Aktions-States: die Animation führt (IK aus).
        [System.Serializable]
        public class StateTarget
        {
            public string State;
            [Tooltip("true: die normale Tragehaltung beibehalten; false: eigene Werte unten.")]
            public bool UseCarry = true;
            public Vector3 GripOffset, WeaponAxis = Vector3.up, WeaponForward, ElbowHint = new Vector3(0.3f, -0.3f, -0.25f);
            [Tooltip("Ziel dreht vollständig mit dem FollowBone (Rolle, Salto …); Werte bleiben Modell-Achsen der Ruhepose.")]
            public bool FullFollow;
            [Tooltip("Nur in diesem Zeitfenster des States (normierte Zeit, 0..1) – z. B. Ausklang nach einem Hieb; außerhalb führt die Animation.")]
            public Vector2 Window = new Vector2(0f, 1f);
            [Tooltip("Überblendbreite am Fensteranfang/-ende (normierte Zeit).")]
            public float WindowBlend = 0.08f;

            public float Weight(float t)
            {
                if (Window.x <= 0f && Window.y >= 1f) return 1f;
                float b = Mathf.Max(1e-4f, WindowBlend);
                float w = Mathf.Min(Mathf.Clamp01((t - Window.x) / b + (Window.x <= 0f ? 1f : 0f)), Mathf.Clamp01((Window.y - t) / b + (Window.y >= 1f ? 1f : 0f)));
                return w * w * (3f - 2f * w);
            }
        }
        public StateTarget[] States = new StateTarget[0];

        [System.Serializable]
        public class Clearance
        {
            public HumanBodyBones From = HumanBodyBones.Head;
            [Tooltip("Ende der Kapsel; LastBone = From + Height in Knochenrichtung (Eltern → From), z. B. Kopf → Hutspitze.")]
            public HumanBodyBones To = HumanBodyBones.LastBone;
            public float Height = 0.5f;
            public float Radius = 0.3f;
            [Tooltip("Kapsel-Ende um so viele Meter zum Anfang hin kürzen (z. B. eigener Unterarm ohne Handgelenk).")]
            public float Shorten;
            [Tooltip("true: Hand per IK verschieben (Waffe bleibt ausgerichtet, z. B. Beine/Robe); false: Handgelenk drehen (z. B. Kopf/Hut).")]
            public bool MoveHand;
        }

        [Header("Handgelenk")]
        [Tooltip("Erlaubter Winkel zwischen Waffenachse und Unterarm (Grad). Zu klein = Waffe kippt Richtung Ellenbogen und schneidet Ärmel/Manschette.")]
        public Vector2 WristAngle = new Vector2(70f, 125f);

        [Header("Freiraum (Kapseln um Kopf/Hut, Beine/Robe …)")]
        public Clearance[] Clearances = { new Clearance() };
        [Tooltip("Waffenlänge über / unter der Griffmitte (Meter).")]
        public float WeaponTop = 0.65f;
        public float WeaponBottom = 1.1f;
        public float WeaponRadius = 0.04f;
        [Tooltip("Optional: Waffe als Strecken-Paare (Waffen-Achsen, Meter, je zwei Punkte) statt der Achse WeaponBottom..WeaponTop – z. B. Schildfläche.")]
        public Vector3[] Segments = new Vector3[0];

        private float _carry = 1f;
        private int[] _layerIdx;
        // geglättetes Ziel (Modell-Achsen): Wechsel Tragehaltung ↔ Block ohne Sprung
        private bool _hasTarget;
        private Vector3 _grip, _axis, _fwd, _hint;

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
            var weapon = RightHand ? Visual.RightHandInstance : Visual.LeftHandInstance;
            var hand = anim.GetBoneTransform(RightHand ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            if (hand == null) return;
            if (weapon == null)
            {
                if (!HandOnly) return;
                weapon = hand;
            }

            StateTarget st;
            float stTime;
            bool action = ActionPlaying(anim, out st, out stTime);
            float stWeight = st != null ? st.Weight(stTime) : 1f;
            bool hold = Carry && (action ? st != null && stWeight > 0f : IdleCarry);
            float target = hold ? stWeight : 0f;
            _carry = immediate ? target : Mathf.MoveTowards(_carry, target, FadeSpeed * dt);
            if (hold || !_hasTarget)
            {
                bool own = st != null && !st.UseCarry;
                Vector3 g = own ? st.GripOffset : GripOffset, ax = own ? st.WeaponAxis : WeaponAxis;
                Vector3 f = own ? st.WeaponForward : WeaponForward, eh = own ? st.ElbowHint : ElbowHint;
                _full = st != null && st.FullFollow;
                // aus der Animation heraus (Gewicht ~0): Ziel direkt übernehmen statt vom alten Ziel herüberzugleiten
                if (immediate || !_hasTarget || _carry < 0.02f) { _grip = g; _axis = ax; _fwd = f; _hint = eh; _hasTarget = true; }
                else
                {
                    float k = 1f - Mathf.Exp(-FadeSpeed * 1.5f * dt);
                    _grip = Vector3.Lerp(_grip, g, k);
                    _axis = Vector3.Slerp(_axis, ax, k);
                    _fwd = f.sqrMagnitude < 1e-6f || _fwd.sqrMagnitude < 1e-6f ? f : Vector3.Slerp(_fwd, f, k);
                    _hint = Vector3.Lerp(_hint, eh, k);
                }
            }
            _frame = Frame(anim);
            if (_carry > 0.001f) CarryIK(anim, hand, weapon, _carry);
            if (weapon == hand) return;
            LimitWrist(anim, hand, weapon);
            if (Clearances != null && Clearances.Length > 0)
            {
                KeepClear(anim, hand, weapon);
                LimitWrist(anim, hand, weapon);
            }
        }

        // Läuft in einem Aktions-Layer ein State ≠ „Empty“? target = passender StateTarget-Eintrag (null = Animation führt)
        private bool ActionPlaying(Animator anim, out StateTarget target, out float time)
        {
            target = null;
            time = 0f;
            if (_layerIdx == null)
            {
                _layerIdx = new int[ActionLayers.Length];
                for (int i = 0; i < ActionLayers.Length; i++) _layerIdx[i] = anim.GetLayerIndex(ActionLayers[i]);
            }
            bool playing = false;
            foreach (int l in _layerIdx)
            {
                if (l < 0) continue;
                var cur = anim.GetCurrentAnimatorStateInfo(l);
                // im Übergang zählt das Ziel (Block → Empty: schon zurück zur Tragehaltung)
                if (anim.IsInTransition(l)) cur = anim.GetNextAnimatorStateInfo(l);
                if (cur.IsName("Empty")) continue;
                playing = true;
                var st = Find(cur);
                if (st == null) { target = null; return true; } // ein State ohne Eintrag gewinnt: Animation führt
                target = st;
                time = cur.normalizedTime;
            }
            return playing;
        }

        private StateTarget Find(AnimatorStateInfo info)
        {
            if (States == null) return null;
            foreach (var s in States) if (s != null && !string.IsNullOrEmpty(s.State) && info.IsName(s.State)) return s;
            return null;
        }

        // Bezugsrahmen der Tragehaltung: Modell, optional mit der Hüfte um die Hochachse gedreht
        private Quaternion Frame(Animator anim)
        {
            if (_full)
            {
                var b = anim.GetBoneTransform(FollowBone);
                if (b != null) return b.rotation * Quaternion.Inverse(Quaternion.Euler(FollowRestEuler));
            }
            if (!FollowHipsYaw) return transform.rotation;
            var hips = anim.GetBoneTransform(FollowBone);
            if (hips == null) return transform.rotation;
            Vector3 f = Vector3.ProjectOnPlane(hips.TransformDirection(HipsRestForward), transform.up);
            if (f.sqrMagnitude < 1e-4f) return transform.rotation;
            return Quaternion.LookRotation(f, transform.up);
        }

        private Quaternion _frame = Quaternion.identity;
        private bool _full;

        // Modell-Achsen; x zeigt zur Waffenhand (linke Hand: gespiegelt)
        private Vector3 ModelDir(Vector3 v)
        {
            if (!RightHand) v.x = -v.x;
            return _frame * v;
        }

        private void CarryIK(Animator anim, Transform hand, Transform weapon, float w)
        {
            var upper = anim.GetBoneTransform(RightHand ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
            var lower = anim.GetBoneTransform(RightHand ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
            var hips = anim.GetBoneTransform(FollowBone);
            if (upper == null || lower == null || hips == null) return;
            float scale = transform.lossyScale.y;

            // Zielrotation der Hand: kleinste Drehung, die die animierte Waffenachse auf die Wunschachse bringt;
            // mit WeaponForward die volle Ausrichtung (Schild-Vorderseite, Bogen-Sehne)
            Vector3 axisNow = weapon.up;
            Vector3 axisWant = ModelDir(_axis).normalized;
            Quaternion handRot;
            if (weapon == hand) handRot = hand.rotation;
            else if (_fwd.sqrMagnitude > 1e-6f)
            {
                Vector3 fw = Vector3.ProjectOnPlane(ModelDir(_fwd), axisWant);
                Quaternion weaponWant = Quaternion.LookRotation(fw.sqrMagnitude > 1e-6f ? fw.normalized : weapon.forward, axisWant);
                Quaternion local = Quaternion.Inverse(hand.rotation) * weapon.rotation;
                handRot = Quaternion.Slerp(hand.rotation, weaponWant * Quaternion.Inverse(local), w);
            }
            else handRot = Quaternion.FromToRotation(axisNow, Vector3.Slerp(axisNow, axisWant, w)) * hand.rotation;
            // Griffmitte → Handgelenk
            Vector3 gripLocal = Quaternion.Inverse(hand.rotation) * (weapon.position - hand.position);
            Vector3 hipsDelta = hips.position - (transform.position + _frame * HipsRest * scale);
            Vector3 hl = Quaternion.Inverse(_frame) * hipsDelta;
            Vector3 gripWant = transform.position + ModelDir(_grip) * scale + _frame * Vector3.Scale(hl, HipsFollow);
            if (_full)
            {
                // relativ zum Bezugsknochen (dreht vollständig mit)
                Vector3 rest = HipsRest;
                if (!RightHand) rest.x = -rest.x;
                gripWant = hips.position + ModelDir(_grip - rest) * scale;
            }
            Vector3 handWant = gripWant - handRot * gripLocal;
            handWant = Vector3.Lerp(hand.position, handWant, w);

            TwoBoneIK(upper, lower, hand, handWant, upper.position + ModelDir(_hint) * scale);
            hand.rotation = handRot;
            if (weapon != hand) KeepAboveGround(hand, weapon, w);
        }

        // Waffenfuß über dem Boden halten: Waffe nach vorne/außen kippen (Drehpunkt Handgelenk, ~8 cm neben der Griffmitte)
        private void KeepAboveGround(Transform hand, Transform weapon, float w)
        {
            float scale = transform.lossyScale.y;
            float len = WeaponBottom * scale;
            Vector3 grip = weapon.position;
            Vector3 down = -weapon.up;
            float groundY = transform.position.y + GroundClearance * scale;
            float drop = grip.y - groundY;
            if (grip.y + down.y * len >= groundY) return;
            Vector3 horiz = Vector3.ProjectOnPlane(down, transform.up);
            Vector3 escape = ModelDir(GroundEscape).normalized;
            horiz = horiz.sqrMagnitude > 0.01f ? Vector3.Lerp(horiz.normalized, escape, 0.5f).normalized : escape;
            float dy = Mathf.Clamp(drop, 0f, len * 0.98f);
            Vector3 want = (-transform.up * dy + horiz * Mathf.Sqrt(len * len - dy * dy)).normalized;
            Quaternion r = Quaternion.FromToRotation(down, Vector3.Slerp(down, want, w));
            hand.rotation = r * hand.rotation;
        }

        // Winkel Waffenachse ↔ Unterarm in WristAngle halten (Drehung des Handgelenks)
        private void LimitWrist(Animator anim, Transform hand, Transform weapon)
        {
            var lower = anim.GetBoneTransform(RightHand ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
            if (lower == null) return;
            Vector3 f = (hand.position - lower.position).normalized;
            Vector3 u = weapon.up;
            float th = Vector3.Angle(u, f);
            float want = Mathf.Clamp(th, WristAngle.x, WristAngle.y);
            if (Mathf.Abs(want - th) < 0.01f) return;
            Vector3 axis = Vector3.Cross(f, u);
            if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(f, transform.right);
            hand.rotation = Quaternion.AngleAxis(want - th, axis.normalized) * hand.rotation;
        }

        // Analytische Zwei-Knochen-IK (Oberarm, Unterarm, Hand) mit Ellenbogen-Hinweis
        public static void TwoBoneIK(Transform a, Transform b, Transform c, Vector3 target, Vector3 hint)
        {
            Vector3 ab = b.position - a.position, bc = c.position - b.position, ac = c.position - a.position, at = target - a.position;
            float abLen = ab.magnitude, bcLen = bc.magnitude, acLen = ac.magnitude;
            float atLen = Mathf.Clamp(at.magnitude, Mathf.Abs(abLen - bcLen) + 1e-3f, abLen + bcLen - 1e-3f);
            float oldAngle = TriangleAngle(acLen, abLen, bcLen);
            float newAngle = TriangleAngle(atLen, abLen, bcLen);
            Vector3 axis = Vector3.Cross(ab, bc);
            if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(hint - a.position, bc);
            if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(ab, Vector3.up);
            axis.Normalize();
            float half = 0.5f * (oldAngle - newAngle);
            float sin = Mathf.Sin(half);
            Quaternion bend = new Quaternion(axis.x * sin, axis.y * sin, axis.z * sin, Mathf.Cos(half));
            Quaternion cRot = c.rotation;
            b.rotation = bend * b.rotation;

            a.rotation = Quaternion.FromToRotation(c.position - a.position, at) * a.rotation;

            // Ellenbogen um die Schulter-Hand-Achse zum Hinweis drehen
            Vector3 acN = (c.position - a.position).normalized;
            Vector3 abP = Vector3.ProjectOnPlane(b.position - a.position, acN);
            Vector3 ahP = Vector3.ProjectOnPlane(hint - a.position, acN);
            if (abP.sqrMagnitude > 1e-6f && ahP.sqrMagnitude > 1e-6f)
                a.rotation = Quaternion.FromToRotation(abP, ahP) * a.rotation;
            c.rotation = cRot;
        }

        private static float TriangleAngle(float aLen, float aLen1, float aLen2)
        {
            float c = Mathf.Clamp((aLen1 * aLen1 + aLen2 * aLen2 - aLen * aLen) / (2f * aLen1 * aLen2), -1f, 1f);
            return Mathf.Acos(c);
        }

        // Waffe (als Strecke) aus den Freiraum-Kapseln herausdrehen (Drehpunkt Handgelenk)
        private void KeepClear(Animator anim, Transform hand, Transform weapon)
        {
            float scale = transform.lossyScale.y;
            for (int it = 0; it < 10; it++)
            {
                bool moved = false;
                foreach (var c in Clearances)
                {
                    var from = anim.GetBoneTransform(c.From);
                    if (from == null) continue;
                    Transform to = c.To != HumanBodyBones.LastBone ? anim.GetBoneTransform(c.To) : null;
                    Vector3 dir = from.parent != null ? (from.position - from.parent.position).normalized : transform.up;
                    Vector3 h0 = from.position, h1 = to != null ? to.position : from.position + dir * c.Height * scale;
                    if (c.Shorten > 0f) h1 = Vector3.MoveTowards(h1, h0, c.Shorten * scale);
                    float r = (c.Radius + WeaponRadius) * scale;
                    // nächstes Strecken-Paar der Waffe zur Kapsel
                    Vector3 pw = Vector3.zero, ph = Vector3.zero;
                    float dist = float.MaxValue;
                    int segCount = Segments != null && Segments.Length >= 2 ? Segments.Length / 2 : 1;
                    for (int sIdx = 0; sIdx < segCount; sIdx++)
                    {
                        Vector3 w0, w1;
                        if (segCount == 1 && (Segments == null || Segments.Length < 2))
                        {
                            w0 = weapon.position - weapon.up * WeaponBottom * scale;
                            w1 = weapon.position + weapon.up * WeaponTop * scale;
                        }
                        else
                        {
                            w0 = weapon.position + weapon.rotation * Segments[sIdx * 2] * scale;
                            w1 = weapon.position + weapon.rotation * Segments[sIdx * 2 + 1] * scale;
                        }
                        Vector3 a1, b1;
                        ClosestSegments(w0, w1, h0, h1, out a1, out b1);
                        float dd = (a1 - b1).magnitude;
                        if (dd < dist) { dist = dd; pw = a1; ph = b1; }
                    }
                    Vector3 d = pw - ph;
                    if (dist >= r) continue;
                    Vector3 lever = pw - hand.position;
                    Vector3 n = dist > 1e-4f ? d / dist : Vector3.ProjectOnPlane(lever, h1 - h0).normalized;
                    Vector3 push = n * (r - dist + 0.005f * scale);
                    if (c.MoveHand)
                    {
                        // ganze Waffe verschieben: Arm per IK nachführen, Handrotation bleibt
                        var upper = anim.GetBoneTransform(RightHand ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
                        var lower = anim.GetBoneTransform(RightHand ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
                        if (upper == null || lower == null) continue;
                        Quaternion rot = hand.rotation;
                        TwoBoneIK(upper, lower, hand, hand.position + push, upper.position + ModelDir(_hasTarget ? _hint : ElbowHint) * scale);
                        hand.rotation = rot;
                    }
                    else
                    {
                        if (lever.sqrMagnitude < 0.0025f * scale * scale) continue; // Kontakt am Handgelenk: Drehen hilft nicht
                        hand.rotation = Quaternion.FromToRotation(lever, lever + push) * hand.rotation;
                    }
                    moved = true;
                }
                if (!moved) return;
            }
        }

        // Nächste Punkte zweier Strecken (Ericson)
        public static void ClosestSegments(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out Vector3 c1, out Vector3 c2)
        {
            Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
            float s, t;
            if (a <= 1e-8f && e <= 1e-8f) { c1 = p1; c2 = p2; return; }
            if (a <= 1e-8f) { s = 0f; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= 1e-8f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float b = Vector3.Dot(d1, d2), denom = a * e - b * b;
                    s = denom != 0f ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            c1 = p1 + d1 * s;
            c2 = p2 + d2 * t;
        }
    }
}
