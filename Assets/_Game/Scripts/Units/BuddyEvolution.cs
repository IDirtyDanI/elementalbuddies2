using System.Collections;
using UnityEngine;

namespace ElementalBuddies
{
    // Visuelle Entwicklungsstufen pro Buddy-Level:
    // Stufe 1 = kleines Element-Wesen (Stage1Visual), Stufe 2 = Humanoid (HumanoidVisual), Stufe 3 = Humanoid + Rüstung/Extras,
    // Stufe 4 = zusätzlich Stage4Extras + größerer Humanoid (Stage4Scale). Kürzere Effekt-/Feuerpunkt-Arrays (alte Prefabs)
    // fallen auf den letzten vorhandenen Eintrag zurück.
    // Auf den Buddy-Prefab-Root legen. Der Prefab muss bereits im Stufe-1-Zustand gespeichert sein
    // (Stage1Visual aktiv, HumanoidVisual/Stage3Extras inaktiv), da Bau-Ghosts ihre Scripts deaktivieren.
    public class BuddyEvolution : MonoBehaviour
    {
        [Header("Visuals")]
        public GameObject Stage1Visual; // Kleines, statisches Wesen (ohne Animator), z. B. mit FloatingCreature
        public GameObject HumanoidVisual; // Bisheriges "Visual"-Kind mit Animator (Stufe 2 und 3)
        public GameObject[] Stage3Extras; // Rüstungsteile (an Knochen) + Zusatz-VFX, ab Stufe 3 aktiv
        public GameObject[] Stage4Extras; // Krone/Flügel/Aura o. Ä., nur auf Stufe 4 aktiv (Prefab: inaktiv speichern)
        [Tooltip("Skalierung des Humanoid-Visuals auf Stufe 4 (relativ zur Prefab-Skalierung).")]
        public float Stage4Scale = 1.15f;

        [Header("Effekte")]
        public GameObject[] StageEffects = new GameObject[4]; // Index = Stufe - 1, nur der aktuelle ist aktiv (fehlt er: letzter vorhandene)
        public ParticleSystem LevelUpBurst; // Wird beim Aufwerten abgespielt
        public AudioClip LevelUpSound; // Optional
        [Range(0f, 1f)] public float LevelUpVolume = 0.8f;

        [Header("Feuerpunkte (optional, nur ShooterBuddy)")]
        public Transform[] StageFirePoints = new Transform[4]; // Index = Stufe - 1; leer = letzter vorhandene bzw. FirePoint unverändert

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
            if (_buddy != null && _buddy.SuppressLevelFx) return; // Wiedergeburt: Stufe still wiederherstellen

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
            else GameAudio.Play(SfxId.LevelUp, transform.position);
        }

        private void ApplyStage(int stage)
        {
            stage = Mathf.Clamp(stage, 1, 4);

            if (Stage1Visual != null) Stage1Visual.SetActive(stage == 1);
            if (HumanoidVisual != null)
            {
                HumanoidVisual.SetActive(stage >= 2);
                HumanoidVisual.transform.localScale = HumanoidBaseScale(stage);
            }

            if (Stage3Extras != null)
                foreach (var extra in Stage3Extras)
                    if (extra != null) extra.SetActive(stage >= 3);
            if (Stage4Extras != null)
                foreach (var extra in Stage4Extras)
                    if (extra != null) extra.SetActive(stage >= 4);

            if (StageEffects != null)
            {
                int active = LastFilledIndex(StageEffects, stage - 1);
                for (int i = 0; i < StageEffects.Length; i++)
                    if (StageEffects[i] != null) StageEffects[i].SetActive(i == active);
            }

            int fpIndex = StageFirePoints != null ? LastFilledIndex(StageFirePoints, stage - 1) : -1;
            if (fpIndex >= 0)
            {
                Transform fp = StageFirePoints[fpIndex];
                if (_buddy is ShooterBuddy shooter) shooter.FirePoint = fp;
                else if (_buddy is HealerBuddy healer) healer.FirePoint = fp;
            }

            _stage = stage;
            if (_buddy != null) _buddy.RefreshVisual();
        }

        // Index = Stufe - 1. Stufe 4 ohne eigenen Eintrag (alte Prefabs mit 3 Einträgen oder leer) nutzt den letzten belegten darunter
        private static int LastFilledIndex<T>(T[] array, int index) where T : Object
        {
            if (index < array.Length && (array[index] != null || index < 3)) return array[index] != null ? index : -1;
            for (int i = Mathf.Min(index, array.Length - 1); i >= 0; i--)
                if (array[i] != null) return i;
            return -1;
        }

        private Vector3 HumanoidBaseScale(int stage) =>
            stage >= 4 ? _humanoidBaseScale * Mathf.Max(0.01f, Stage4Scale) : _humanoidBaseScale;

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
            Vector3 baseScale = target == Stage1Visual ? _stage1BaseScale : HumanoidBaseScale(_stage);
            target.transform.localScale = baseScale * m;
        }
    }
}
