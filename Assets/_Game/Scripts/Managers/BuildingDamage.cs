using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    public enum DamageState { Intact, Burning, Ruined }

    // Auf Gebäude-Roots: Zustand Intakt / Brennend / Ruine. Wird von SiegeProgression gesetzt.
    // Fehlt ein Beschädigt-/Ruinen-Modell, bleibt das intakte Modell sichtbar und wird verkohlt eingefärbt.
    // Eingerichtet von BuddyTD → Belagerung → Einrichten (SiegeSetup).
    public class BuildingDamage : MonoBehaviour
    {
        [Header("Modelle")]
        public GameObject IntactModel;
        [Tooltip("Optional – sonst intaktes Modell mit BurningTint.")]
        public GameObject DamagedModel;
        [Tooltip("Optional – sonst Beschädigt-/Intakt-Modell mit RuinedTint.")]
        public GameObject RuinedModel;

        [Header("Effekte")]
        [Tooltip("Container mit VFX_HouseFire-Instanzen (an den Fire_*-Ankern).")]
        public GameObject FireFx;
        [Tooltip("Container mit VFX_Smolder-Instanzen (an den Smoke_*-Ankern).")]
        public GameObject SmokeFx;
        public Light FireLight;
        public float FireLightIntensity = 4f;

        [Header("Fallback-Tönung")]
        public Color BurningTint = new Color(0.6f, 0.52f, 0.46f);
        public Color RuinedTint = new Color(0.22f, 0.19f, 0.17f);

        [Tooltip("Sekunden, bis das Feuer voll aufgelodert bzw. erloschen ist.")]
        public float RampTime = 3f;

        // Höchstzahl gleichzeitig aktiver Feuer-Punktlichter (die übrigen Brände nur mit Partikeln)
        public static int MaxFireLights = 6;

        public DamageState State { get; private set; } = DamageState.Intact;

        private static readonly List<BuildingDamage> Burning = new List<BuildingDamage>();
        private static float _nextBudget;

        private ParticleSystem[] _fire, _smoke;
        private float[] _fireRates, _smokeRates;
        private float _fireLevel, _smokeLevel;    // 0..1, aktuelle Stärke
        private float _fireTarget, _smokeTarget;
        private float _lightLevel;
        private bool _lightAllowed;
        private float _flickerSeed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Burning.Clear(); _nextBudget = 0f; }

        void Awake()
        {
            _flickerSeed = Random.value * 100f;
            if (FireLight != null) { FireLight.enabled = false; FireLight.intensity = 0f; }
        }

        void OnDisable() => Burning.Remove(this);

        // ---------------- Zustand ----------------

        public void SetState(DamageState state, bool instant)
        {
            CacheSystems();
            if (state == State && !instant) return;
            State = state;

            // Modelle: Fallback-Kette Ruine → Beschädigt → Intakt
            GameObject show = IntactModel;
            Color tint = Color.white;
            if (state == DamageState.Burning)
            {
                if (DamagedModel != null) show = DamagedModel; else tint = BurningTint;
            }
            else if (state == DamageState.Ruined)
            {
                if (RuinedModel != null) show = RuinedModel;
                else { show = DamagedModel != null ? DamagedModel : IntactModel; tint = RuinedTint; }
            }
            SetActive(IntactModel, show == IntactModel);
            SetActive(DamagedModel, show == DamagedModel);
            SetActive(RuinedModel, show == RuinedModel);
            ApplyTint(tint);

            _fireTarget = state == DamageState.Burning ? 1f : 0f;
            _smokeTarget = state == DamageState.Ruined ? 1f : 0f;
            if (state == DamageState.Burning) { if (!Burning.Contains(this)) Burning.Add(this); }
            else Burning.Remove(this);

            if (instant || !Application.isPlaying)
            {
                _fireLevel = _fireTarget;
                _smokeLevel = _smokeTarget;
                _lightLevel = _fireTarget;
                SetActive(FireFx, _fireTarget > 0f);
                SetActive(SmokeFx, _smokeTarget > 0f);
                ApplyRates();
                if (_fireTarget > 0f) Prewarm(_fire);
                if (_smokeTarget > 0f) Prewarm(_smoke);
                RefreshLights();
            }
            else
            {
                // Feuer/Glut lodern langsam auf (Raten ab 0), statt aufzupoppen
                if (_fireTarget > 0f && FireFx != null && !FireFx.activeSelf) { _fireLevel = 0f; ApplyRates(); SetActive(FireFx, true); Play(_fire); }
                if (_smokeTarget > 0f && SmokeFx != null && !SmokeFx.activeSelf) { _smokeLevel = 0f; ApplyRates(); SetActive(SmokeFx, true); Play(_smoke); }
            }
        }

        void Update()
        {
            if (!Application.isPlaying) return;
            float step = Time.unscaledDeltaTime / Mathf.Max(0.05f, RampTime);
            bool changed = false;
            if (!Mathf.Approximately(_fireLevel, _fireTarget)) { _fireLevel = Mathf.MoveTowards(_fireLevel, _fireTarget, step); changed = true; }
            if (!Mathf.Approximately(_smokeLevel, _smokeTarget)) { _smokeLevel = Mathf.MoveTowards(_smokeLevel, _smokeTarget, step); changed = true; }
            if (changed)
            {
                ApplyRates();
                // Erloschenes Feuer erst abschalten, wenn die letzten Partikel verglüht sind
                if (_fireLevel <= 0f && FireFx != null && FireFx.activeSelf && !AnyAlive(_fire)) SetActive(FireFx, false);
                if (_smokeLevel <= 0f && SmokeFx != null && SmokeFx.activeSelf && !AnyAlive(_smoke)) SetActive(SmokeFx, false);
            }
            else
            {
                if (_fireLevel <= 0f && FireFx != null && FireFx.activeSelf && !AnyAlive(_fire)) SetActive(FireFx, false);
                if (_smokeLevel <= 0f && SmokeFx != null && SmokeFx.activeSelf && !AnyAlive(_smoke)) SetActive(SmokeFx, false);
            }

            if (Time.unscaledTime >= _nextBudget) UpdateLightBudget(false);
            float lightTarget = _lightAllowed ? _fireLevel : 0f;
            _lightLevel = Mathf.MoveTowards(_lightLevel, lightTarget, Time.unscaledDeltaTime * 1.5f);
            UpdateLight();
        }

        // ---------------- Licht ----------------

        // Wählt die brennenden Gebäude, die ein Punktlicht bekommen: die nächsten zum Kamera-Fokus
        private static void UpdateLightBudget(bool force)
        {
            if (!force && Time.unscaledTime < _nextBudget) return;
            _nextBudget = Time.unscaledTime + 0.5f;
            Burning.RemoveAll(b => b == null);
            Vector3 focus = Vector3.zero;
            var cam = Camera.main;
            if (cam != null)
            {
                // Schnittpunkt Blickrichtung/Boden ≈ Spielerposition
                var ray = new Ray(cam.transform.position, cam.transform.forward);
                focus = ray.direction.y < -0.05f ? ray.GetPoint(-ray.origin.y / ray.direction.y) : cam.transform.position;
            }
            Burning.Sort((a, b) => (a.transform.position - focus).sqrMagnitude.CompareTo((b.transform.position - focus).sqrMagnitude));
            for (int i = 0; i < Burning.Count; i++) Burning[i]._lightAllowed = i < MaxFireLights;
        }

        // Lichtbudget sofort neu verteilen und alle Feuerlichter aktualisieren (nach Sofort-Zuständen / Vorschau)
        public static void RefreshLights()
        {
            UpdateLightBudget(true);
            foreach (var b in FindObjectsByType<BuildingDamage>(FindObjectsSortMode.None))
            {
                if (b.State != DamageState.Burning) b._lightAllowed = false;
                b._lightLevel = b._lightAllowed ? b._fireLevel : 0f;
                b.UpdateLight();
            }
        }

        private void UpdateLight()
        {
            if (FireLight == null) return;
            if (!Application.isPlaying) _lightLevel = _lightAllowed ? _fireLevel : 0f;
            bool on = _lightLevel > 0.01f;
            if (FireLight.enabled != on) FireLight.enabled = on;
            if (!on) return;
            float t = Application.isPlaying ? Time.unscaledTime * 7f : 0f;
            float flicker = 0.78f + 0.3f * Mathf.PerlinNoise(_flickerSeed, t) + 0.08f * Mathf.Sin(t * 2.3f + _flickerSeed);
            FireLight.intensity = FireLightIntensity * _lightLevel * flicker;
        }

        // ---------------- Partikel ----------------

        private void CacheSystems()
        {
            if (!Application.isPlaying) _fire = null; // Edit-Modus: Prefab kann neu eingerichtet worden sein
            if (_fire != null) return;
            _fire = FireFx != null ? FireFx.GetComponentsInChildren<ParticleSystem>(true) : new ParticleSystem[0];
            _smoke = SmokeFx != null ? SmokeFx.GetComponentsInChildren<ParticleSystem>(true) : new ParticleSystem[0];
            _fireRates = BaseRates(_fire);
            _smokeRates = BaseRates(_smoke);
        }

        private static float[] BaseRates(ParticleSystem[] systems)
        {
            var r = new float[systems.Length];
            for (int i = 0; i < systems.Length; i++) r[i] = systems[i].emission.rateOverTimeMultiplier;
            return r;
        }

        private void ApplyRates()
        {
            if (!Application.isPlaying) return; // Edit-Vorschau: Prefab-Werte nicht überschreiben
            for (int i = 0; i < _fire.Length; i++) { var e = _fire[i].emission; e.rateOverTimeMultiplier = _fireRates[i] * _fireLevel; }
            for (int i = 0; i < _smoke.Length; i++) { var e = _smoke[i].emission; e.rateOverTimeMultiplier = _smokeRates[i] * _smokeLevel; }
        }

        // Sofort-Zustand: Partikel vorsimulieren (Rauchsäule steht schon), dann weiterlaufen lassen
        private static void Prewarm(ParticleSystem[] systems)
        {
            foreach (var ps in systems)
            {
                if (ps == null || ps.transform.parent == null) continue;
                // Nur Wurzelsysteme simulieren (Kinder werden mitgenommen)
                if (ps.transform.parent.GetComponent<ParticleSystem>() != null) continue;
                ps.Simulate(6f, true, true);
                if (Application.isPlaying) ps.Play(true);
            }
        }

        private static void Play(ParticleSystem[] systems)
        {
            foreach (var ps in systems) if (ps != null) ps.Play(false);
        }

        private static bool AnyAlive(ParticleSystem[] systems)
        {
            foreach (var ps in systems) if (ps != null && ps.particleCount > 0) return true;
            return false;
        }

        // ---------------- Hilfen ----------------

        private void ApplyTint(Color tint)
        {
            var occ = GetComponent<FadeOccluder>();
            if (occ != null) { occ.SetTint(tint); return; }
            var block = new MaterialPropertyBlock();
            foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
            {
                if (tint == Color.white) { r.SetPropertyBlock(null); continue; }
                var mats = r.sharedMaterials;
                for (int k = 0; k < mats.Length; k++)
                {
                    if (mats[k] == null || !mats[k].HasProperty("_BaseColor")) continue;
                    block.SetColor("_BaseColor", mats[k].GetColor("_BaseColor") * tint);
                    r.SetPropertyBlock(block, k);
                }
            }
        }

        private static void SetActive(GameObject go, bool on)
        {
            if (go != null && go.activeSelf != on) go.SetActive(on);
        }
    }
}
