using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // Eine abzuspielende Pose des Champion-Controllers: Base-State (+ Speed) und optional ein Aktions-State in einem Layer.
    public class PoseSample
    {
        public string Label;
        public string BaseState = "Locomotion";
        public float Speed;
        public bool Grounded = true;
        public int Layer;           // 0 = nur Base Layer
        public string State;        // Aktions-State im Layer
        public float Length = 1f;   // Dauer in Sekunden (Clip-Länge / State-Speed)
    }

    // Spielt den Animator-Controller eines ChampionVisual im Editor ab (alle Layer inkl. Masken, Hands/HoldArm),
    // auf einer versteckten Kopie mit angehängten Waffen. Grundlage für Griff-Sockel und Clipping-Prüfung.
    public class ChampionPoseSampler : System.IDisposable
    {
        public GameObject Root { get; private set; }
        public ChampionVisual Visual { get; private set; }
        public Animator Animator { get; private set; }

        // holds = false: ohne ChampionWeaponHold (Vergleich „nur Griff/Animation“ in der Clipping-Prüfung)
        public ChampionPoseSampler(ChampionVisual source, Vector3 position, bool attachWeapons = true, bool holds = true)
        {
            Root = Object.Instantiate(source.gameObject);
            Root.name = source.name + "_Sampler";
            Root.hideFlags = HideFlags.HideAndDontSave;
            Root.transform.SetPositionAndRotation(position, Quaternion.identity);
            Root.transform.localScale = source.transform.lossyScale;
            Root.SetActive(true);
            if (!holds) foreach (var h in Root.GetComponentsInChildren<ChampionWeaponHold>(true)) Object.DestroyImmediate(h);
            Visual = Root.GetComponent<ChampionVisual>();
            Animator = Visual.Animator != null ? Visual.Animator : Root.GetComponentInChildren<Animator>(true);
            Visual.Animator = Animator;
            Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Animator.applyRootMotion = false;
            Animator.fireEvents = false;
            Animator.Rebind();
            // mehrere Samples/Renders pro Editor-Frame: Skinning bei jedem Render neu (sonst zeigen Kontaktbilder die vorige Pose)
            foreach (var smr in Root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.forceMatrixRecalculationPerRender = true;
            if (attachWeapons) Visual.AttachForPreview();
        }

        public void Dispose()
        {
            if (Root != null) Object.DestroyImmediate(Root);
            Root = null;
        }

        // Pose zum normierten Zeitpunkt t (0..1) auswerten
        public void Sample(PoseSample s, float t)
        {
            var a = Animator;
            a.Rebind();
            SetParam(a, "Speed", s.Speed);
            SetBool(a, "Grounded", s.Grounded);
            a.Play(s.BaseState, 0, s.Layer == 0 ? t : Mathf.Repeat(t * 0.999f, 1f));
            var ac = ChampionPoseSampler.ControllerOf(a);
            for (int i = 1; i < a.layerCount; i++)
            {
                if (i == s.Layer) a.Play(s.State, i, t);
                else if (ac != null && HasState(ac.layers[i].stateMachine, "Empty")) a.Play("Empty", i, 0f);
            }
            a.Update(0f);
            // Laufzeit-Korrekturen nach dem Animator (IK-Tragehaltung, Freiraum) wie im Spiel anwenden
            foreach (var h in Root.GetComponentsInChildren<ChampionWeaponHold>()) h.Apply(true);
        }

        // AnimatorController hinter einem (Override-)Controller (Händler-Figuren nutzen Override-Controller)
        public static AnimatorController ControllerOf(Animator a)
        {
            var rc = a != null ? a.runtimeAnimatorController : null;
            var ov = rc as AnimatorOverrideController;
            while (ov != null) { rc = ov.runtimeAnimatorController; ov = rc as AnimatorOverrideController; }
            return rc as AnimatorController;
        }

        private static bool HasState(AnimatorStateMachine sm, string name)
        {
            foreach (var cs in sm.states) if (cs.state.name == name) return true;
            return false;
        }

        private static void SetParam(Animator a, string n, float v)
        {
            foreach (var p in a.parameters) if (p.name == n && p.type == AnimatorControllerParameterType.Float) a.SetFloat(n, v);
        }

        private static void SetBool(Animator a, string n, bool v)
        {
            foreach (var p in a.parameters) if (p.name == n && p.type == AnimatorControllerParameterType.Bool) a.SetBool(n, v);
        }

        // Alle relevanten Posen eines Controllers: Locomotion je Blend-Schwelle (Idle/Walk/Run), Sprung-States,
        // jeder Aktions-State (im Stand; maskierte Oberkörper-Aktionen zusätzlich im Lauf).
        public static List<PoseSample> Enumerate(AnimatorController ac)
        {
            var list = new List<PoseSample>();
            if (ac == null) return list;
            var layers = ac.layers;
            float runSpeed = 6f;
            foreach (var cs in layers[0].stateMachine.states)
            {
                var st = cs.state;
                var bt = st.motion as BlendTree;
                if (bt != null)
                {
                    foreach (var ch in bt.children)
                    {
                        if (ch.motion == null) continue;
                        string label = ch.threshold <= 0.01f ? "Idle" : ch.threshold < 4f ? "Walk" : "Run";
                        runSpeed = Mathf.Max(runSpeed, ch.threshold);
                        list.Add(new PoseSample { Label = label, BaseState = st.name, Speed = ch.threshold, Length = ch.motion.averageDuration / Mathf.Max(0.01f, st.speed) });
                    }
                }
                else if (st.motion != null)
                {
                    bool grounded = st.name.Contains("Land");
                    list.Add(new PoseSample { Label = st.name, BaseState = st.name, Grounded = grounded, Length = st.motion.averageDuration / Mathf.Max(0.01f, st.speed) });
                }
            }
            for (int i = 1; i < layers.Length; i++)
            {
                if (layers[i].name == ChampionGrip.HandsLayer || layers[i].name == ChampionGrip.HoldLayer) continue;
                foreach (var cs in layers[i].stateMachine.states)
                {
                    var st = cs.state;
                    if (st.motion == null) continue;
                    float len = st.motion.averageDuration / Mathf.Max(0.01f, st.speed);
                    list.Add(new PoseSample { Label = st.name, Layer = i, State = st.name, Length = len });
                    if (layers[i].avatarMask != null)
                        list.Add(new PoseSample { Label = st.name + "+Run", Layer = i, State = st.name, Speed = runSpeed, Length = len });
                }
            }
            return list;
        }
    }
}
