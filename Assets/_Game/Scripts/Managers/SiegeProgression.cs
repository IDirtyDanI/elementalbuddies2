using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Belagerung: Die Stadt verändert sich mit den abgeschlossenen Wellen – Häuser brennen und werden zu Ruinen
    // (von den Toren nach innen), Zäune kippen/fallen um, und die Stimmung geht von Tag zu Nacht (SiegeAtmosphere).
    // "Abgeschlossene Wellen" = WaveManager.CurrentWaveIndex (wird in EndWave vor OnWaveEnd erhöht; DevSetStartWave(n) → n-1).
    // Ab MaxWave bleibt alles auf Maximalschaden/Nacht (Endlos-Modus).
    // Zeitplan wird vom Editor-Tool BuddyTD → Belagerung → Einrichten berechnet (SiegeSetup).
    public class SiegeProgression : MonoBehaviour
    {
        public static SiegeProgression Instance { get; private set; }

        [System.Serializable]
        public class BuildingStage
        {
            public BuildingDamage Building;
            [Tooltip("Ab dieser Zahl abgeschlossener Wellen brennt das Gebäude (0 = nie).")]
            public int BurnWave;
            [Tooltip("Ab dieser Zahl abgeschlossener Wellen ist es eine Ruine (0 = nie).")]
            public int RuinWave;
        }

        public enum FenceBreak { Tilt, Fall }

        [System.Serializable]
        public class FenceStage
        {
            public Transform Fence;
            [Tooltip("Ab dieser Zahl abgeschlossener Wellen ist der Zaun schief/umgefallen (0 = nie).")]
            public int BreakWave;
            public FenceBreak Mode;
            // Lokale Posen (relativ zum Parent): intakt = Szenenzustand, kaputt = vorberechnet
            public Vector3 IntactPos;
            public Quaternion IntactRot = Quaternion.identity;
            public Vector3 BrokenPos;
            public Quaternion BrokenRot = Quaternion.identity;
        }

        [Tooltip("Ab dieser Zahl abgeschlossener Wellen ist der Endzustand erreicht (Nacht, maximaler Schaden).")]
        public int MaxWave = 14;
        public SiegeAtmosphere Atmosphere;
        public List<BuildingStage> Buildings = new List<BuildingStage>();
        public List<FenceStage> Fences = new List<FenceStage>();

        [Header("Übergänge (unskalierte Zeit)")]
        [Tooltip("Verzögerung nach Wellenende, bevor sich die Stadt verändert.")]
        public float TransitionDelay = 1.5f;
        [Tooltip("Zeitversatz zwischen mehreren Änderungen derselben Welle.")]
        public float Stagger = 0.8f;
        public float FenceTiltTime = 0.7f;
        public float FenceFallTime = 0.55f;

        // Zuletzt angewendete Zahl abgeschlossener Wellen (-1 = noch nichts angewendet)
        public int AppliedWaves { get; private set; } = -1;

        // Edit-Vorschau aktiv (wird vor dem Speichern der Szene automatisch zurückgesetzt)
        [HideInInspector] public bool PreviewActive;

        public static int CompletedWaves => WaveManager.Instance != null ? WaveManager.Instance.CurrentWaveIndex : 0;

        void Awake()
        {
            Instance = this;
        }

        void Start()
        {
            ApplyInstant(CompletedWaves);
        }

        void Update()
        {
            int waves = CompletedWaves;
            if (waves == AppliedWaves) return;
            // Normales Wellenende (+1) animiert, Sprünge (Dev-Startwelle) sofort
            if (waves == AppliedWaves + 1) ApplyAnimated(waves);
            else ApplyInstant(waves);
        }

        // ---------------- API ----------------

        // Zustand für n abgeschlossene Wellen sofort setzen (Spielstart, Dev-Sprung, Edit-Vorschau)
        public void ApplyInstant(int completedWaves)
        {
            StopAllCoroutines();
            AppliedWaves = completedWaves;
            int w = Effective(completedWaves);
            foreach (var b in Buildings)
                if (b != null && b.Building != null) b.Building.SetState(TargetState(b, w), true);
            BuildingDamage.RefreshLights();
            foreach (var f in Fences)
                if (f != null && f.Fence != null) SetFencePose(f, IsBroken(f, w) ? 1f : 0f);
            if (Atmosphere != null) Atmosphere.SetProgress(Progress(completedWaves), true);
        }

        // Übergang mit Animation (Wellenende)
        public void ApplyAnimated(int completedWaves)
        {
            if (!Application.isPlaying) { ApplyInstant(completedWaves); return; }
            int prev = Effective(AppliedWaves);
            AppliedWaves = completedWaves;
            int w = Effective(completedWaves);
            if (Atmosphere != null) Atmosphere.SetProgress(Progress(completedWaves), false);
            if (w == prev) return;

            float delay = TransitionDelay;
            foreach (var b in Buildings)
            {
                if (b == null || b.Building == null) continue;
                var target = TargetState(b, w);
                if (target == b.Building.State) continue;
                StartCoroutine(BuildingRoutine(b.Building, target, delay));
                delay += Stagger;
            }
            float fenceDelay = TransitionDelay + 0.3f;
            foreach (var f in Fences)
            {
                if (f == null || f.Fence == null) continue;
                if (IsBroken(f, w) == IsBroken(f, prev)) continue;
                StartCoroutine(FenceRoutine(f, IsBroken(f, w), fenceDelay));
                fenceDelay += Stagger * 0.35f;
            }
        }

        // Fortschritt der Tageszeit (0 = Tag, 1 = Nacht)
        public float Progress(int completedWaves) => MaxWave <= 0 ? 1f : Mathf.Clamp01(completedWaves / (float)MaxWave);

        public int Effective(int completedWaves) => Mathf.Clamp(completedWaves, 0, Mathf.Max(0, MaxWave));

        public static DamageState TargetState(BuildingStage b, int w)
        {
            if (b.RuinWave > 0 && w >= b.RuinWave) return DamageState.Ruined;
            if (b.BurnWave > 0 && w >= b.BurnWave) return DamageState.Burning;
            return DamageState.Intact;
        }

        public static bool IsBroken(FenceStage f, int w) => f.BreakWave > 0 && w >= f.BreakWave;

        // ---------------- Übergänge ----------------

        private IEnumerator BuildingRoutine(BuildingDamage b, DamageState state, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            if (b != null) b.SetState(state, false);
        }

        private IEnumerator FenceRoutine(FenceStage f, bool broken, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            bool fall = f.Mode == FenceBreak.Fall;
            float dur = fall ? FenceFallTime : FenceTiltTime;
            var col = f.Fence.GetComponent<Collider>();
            if (col != null && fall && broken) col.enabled = false;
            float t = 0f;
            while (t < 1f)
            {
                t = Mathf.Min(1f, t + Time.unscaledDeltaTime / Mathf.Max(0.05f, dur));
                // Umfallen beschleunigt (wie unter Schwerkraft), Kippen weich
                float e = fall ? t * t : Mathf.SmoothStep(0f, 1f, t);
                SetFencePose(f, broken ? e : 1f - e, false);
                yield return null;
            }
            SetFencePose(f, broken ? 1f : 0f);
        }

        // 0 = intakt, 1 = kaputt; Collider nur bei umgefallenen Zäunen aus (NavMesh bleibt unverändert)
        private static void SetFencePose(FenceStage f, float k, bool setCollider = true)
        {
            var tr = f.Fence;
            tr.localPosition = Vector3.LerpUnclamped(f.IntactPos, f.BrokenPos, k);
            tr.localRotation = Quaternion.SlerpUnclamped(f.IntactRot, f.BrokenRot, k);
            if (!setCollider) return;
            var col = tr.GetComponent<Collider>();
            if (col != null) col.enabled = !(f.Mode == FenceBreak.Fall && k >= 0.5f);
        }
    }
}
