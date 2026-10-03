using System.Collections;
using UnityEngine;

namespace ElementalBuddies
{
    // Visuelle Entwicklungsstufen pro Buddy-Level:
    // Stufe 1 = kleines Element-Wesen (Stage1Visual), Stufe 2 = Humanoid (HumanoidVisual), Stufe 3 = Humanoid + Rüstung/Extras.
    // Auf den Buddy-Prefab-Root legen. Der Prefab muss bereits im Stufe-1-Zustand gespeichert sein
    // (Stage1Visual aktiv, HumanoidVisual/Stage3Extras inaktiv), da Bau-Ghosts ihre Scripts deaktivieren.
    public class BuddyEvolution : MonoBehaviour
    {
        [Header("Visuals")]
        public GameObject Stage1Visual; // Kleines, statisches Wesen (ohne Animator), z. B. mit FloatingCreature
        public GameObject HumanoidVisual; // Bisheriges "Visual"-Kind mit Animator (Stufe 2 und 3)
        public GameObject[] Stage3Extras; // Rüstungsteile (an Knochen) + Zusatz-VFX, nur auf Stufe 3 aktiv

        [Header("Effekte")]
        public GameObject[] StageEffects = new GameObject[3]; // Index = Stufe - 1, nur der aktuelle ist aktiv
        public ParticleSystem LevelUpBurst; // Wird beim Aufwerten abgespielt
        public AudioClip LevelUpSound; // Optional
        [Range(0f, 1f)] public float LevelUpVolume = 0.8f;

        [Header("Feuerpunkte (optional, nur ShooterBuddy)")]
        public Transform[] StageFirePoints = new Transform[3]; // Index = Stufe - 1; leer = FirePoint bleibt unverändert

        [Header("Entwicklungs-Pop")]
        public float PopDuration = 0.35f;
        public float PopStartScale = 0.6f;
        public float PopPeakScale = 1.1f;

        private ElementalBuddy _buddy;
        private int _stage = -1;
        private Vector3 _stage1BaseScale = Vector3.one;
        private Vector3 _humanoidBaseScale = Vector3.one;
        private Coroutine _pop;
        private GameObject _popTarget;

        void Awake()
        {
            _buddy = GetComponent<ElementalBuddy>();
            if (_buddy == null) _buddy = GetComponentInParent<ElementalBuddy>();
            if (_buddy == null) _buddy = GetComponentInChildren<ElementalBuddy>();

            if (Stage1Visual != null) _stage1BaseScale = Stage1Visual.transform.localScale;
            if (HumanoidVisual != null) _humanoidBaseScale = HumanoidVisual.transform.localScale;

            ApplyStage(CurrentStage());
        }

        void OnEnable()
        {
            if (_buddy != null) _buddy.OnLevelChanged += HandleLevelChanged;
            ApplyStage(CurrentStage());
        }

        void OnDisable()
        {
            if (_buddy != null) _buddy.OnLevelChanged -= HandleLevelChanged;
            StopPop();
        }

        private int CurrentStage() => _buddy != null ? _buddy.Stage : 1;

        private void HandleLevelChanged()
        {
            int stage = CurrentStage();
            bool changed = stage != _stage;
            ApplyStage(stage);

            if (changed)
            {
                GameObject target = stage <= 1 ? Stage1Visual : HumanoidVisual;
                if (target != null && isActiveAndEnabled)
                {
                    StopPop();
                    _popTarget = target;
                    _pop = StartCoroutine(PopRoutine(target));
                }
            }

            if (LevelUpBurst != null)
            {
                if (!LevelUpBurst.gameObject.activeSelf) LevelUpBurst.gameObject.SetActive(true);
                LevelUpBurst.Play(true);
            }
            if (LevelUpSound != null)
                AudioSource.PlayClipAtPoint(LevelUpSound, transform.position, LevelUpVolume);
        }

        private void ApplyStage(int stage)
        {
            stage = Mathf.Clamp(stage, 1, 3);

            if (Stage1Visual != null) Stage1Visual.SetActive(stage == 1);
            if (HumanoidVisual != null) HumanoidVisual.SetActive(stage >= 2);

            if (Stage3Extras != null)
                foreach (var extra in Stage3Extras)
                    if (extra != null) extra.SetActive(stage >= 3);

            if (StageEffects != null)
                for (int i = 0; i < StageEffects.Length; i++)
                    if (StageEffects[i] != null) StageEffects[i].SetActive(i == stage - 1);

            if (_buddy is ShooterBuddy shooter && StageFirePoints != null && stage - 1 < StageFirePoints.Length)
            {
                Transform fp = StageFirePoints[stage - 1];
                if (fp != null) shooter.FirePoint = fp;
            }

            _stage = stage;
            if (_buddy != null) _buddy.RefreshVisual();
        }

        // 0.6 -> 1.1 -> 1.0 über PopDuration
        private IEnumerator PopRoutine(GameObject target)
        {
            float half = PopDuration * 0.5f;
            float t = 0f;
            while (t < PopDuration)
            {
                t += Time.deltaTime;
                float m = t < half
                    ? Mathf.Lerp(PopStartScale, PopPeakScale, Mathf.SmoothStep(0f, 1f, t / half))
                    : Mathf.Lerp(PopPeakScale, 1f, Mathf.SmoothStep(0f, 1f, (t - half) / Mathf.Max(0.0001f, half)));
                SetPopScale(target, m);
                yield return null;
            }
            SetPopScale(target, 1f);
            _pop = null;
            _popTarget = null;
        }

        private void StopPop()
        {
            if (_pop != null) StopCoroutine(_pop);
            _pop = null;
            if (_popTarget != null) SetPopScale(_popTarget, 1f);
            _popTarget = null;
        }

        // FloatingCreature schreibt die Skalierung selbst jeden Frame -> dort über ScaleMultiplier
        private void SetPopScale(GameObject target, float m)
        {
            if (target == null) return;
            var creature = target.GetComponent<FloatingCreature>();
            if (creature != null && creature.isActiveAndEnabled)
            {
                creature.ScaleMultiplier = m;
                return;
            }
            Vector3 baseScale = target == Stage1Visual ? _stage1BaseScale : _humanoidBaseScale;
            target.transform.localScale = baseScale * m;
        }
    }
}
