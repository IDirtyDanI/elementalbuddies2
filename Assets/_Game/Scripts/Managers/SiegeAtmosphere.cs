using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ElementalBuddies
{
    // Ein Keyframe der Belagerungs-Stimmung (Sonne/Mond, Umgebungslicht, Nebel, Himmel, Laternen, Fenster)
    [System.Serializable]
    public struct AtmosphereKey
    {
        public string Name;
        [Range(0f, 1f)] public float Time;
        [Header("Sonne / Mond")]
        public Vector3 SunEuler;
        public Color SunColor;
        public float SunIntensity;
        [Range(0f, 1f)] public float ShadowStrength;
        [Header("Umgebungslicht (Trilight)")]
        public Color AmbientSky;
        public Color AmbientEquator;
        public Color AmbientGround;
        [Header("Nebel (Exponential²)")]
        public Color FogColor;
        public float FogDensity;
        [Header("Himmel (Skybox/Procedural)")]
        public float SkyExposure;
        public Color SkyTint;
        public Color SkyGround;
        public float SkyAtmosphere;
        [Header("Lichter")]
        [Tooltip("Faktor auf die Grundintensität der Laternen.")]
        public float LanternFactor;
        [Tooltip("Faktor auf die Emission des Fenster-Materials.")]
        public float WindowGlow;

        public static AtmosphereKey Lerp(AtmosphereKey a, AtmosphereKey b, float t)
        {
            var k = new AtmosphereKey();
            k.SunEuler = (Quaternion.Slerp(Quaternion.Euler(a.SunEuler), Quaternion.Euler(b.SunEuler), t)).eulerAngles;
            k.SunColor = Color.Lerp(a.SunColor, b.SunColor, t);
            k.SunIntensity = Mathf.Lerp(a.SunIntensity, b.SunIntensity, t);
            k.ShadowStrength = Mathf.Lerp(a.ShadowStrength, b.ShadowStrength, t);
            k.AmbientSky = Color.Lerp(a.AmbientSky, b.AmbientSky, t);
            k.AmbientEquator = Color.Lerp(a.AmbientEquator, b.AmbientEquator, t);
            k.AmbientGround = Color.Lerp(a.AmbientGround, b.AmbientGround, t);
            k.FogColor = Color.Lerp(a.FogColor, b.FogColor, t);
            k.FogDensity = Mathf.Lerp(a.FogDensity, b.FogDensity, t);
            k.SkyExposure = Mathf.Lerp(a.SkyExposure, b.SkyExposure, t);
            k.SkyTint = Color.Lerp(a.SkyTint, b.SkyTint, t);
            k.SkyGround = Color.Lerp(a.SkyGround, b.SkyGround, t);
            k.SkyAtmosphere = Mathf.Lerp(a.SkyAtmosphere, b.SkyAtmosphere, t);
            k.LanternFactor = Mathf.Lerp(a.LanternFactor, b.LanternFactor, t);
            k.WindowGlow = Mathf.Lerp(a.WindowGlow, b.WindowGlow, t);
            return k;
        }
    }

    // Tageszeit der Belagerung: Tag → Nachmittag → Dämmerung → Nacht, gesteuert über t (0..1) von SiegeProgression.
    // Bei t = 0 wird exakt der in der Szene gespeicherte Look wiederhergestellt. Assets werden nie verändert
    // (Skybox und Fenster-Material laufen über Laufzeit-Klone).
    public class SiegeAtmosphere : MonoBehaviour
    {
        public Light Sun;
        [Tooltip("Punktlichter der Laternen (Grundintensität = Wert in der Szene).")]
        public List<Light> Lanterns = new List<Light>();
        [HideInInspector] public List<float> LanternBase = new List<float>();
        [Tooltip("Emissives Fenster-Material der Gebäude (Asset bleibt unverändert).")]
        public Material WindowMaterial;
        [Tooltip("Dauer eines Stimmungswechsels (unskalierte Zeit).")]
        public float TransitionTime = 5f;

        [Header("Keyframes (Time 0 = Tag, wie in der Szene)")]
        public AtmosphereKey Day;
        public AtmosphereKey Afternoon;
        public AtmosphereKey Dusk;
        public AtmosphereKey Night;

        // Original-Szenenwerte für t = 0 (vom Setup erfasst)
        [HideInInspector] public bool DayCaptured;
        [HideInInspector] public AmbientMode OrigAmbientMode;
        [HideInInspector] public Color OrigAmbientSky, OrigAmbientEquator, OrigAmbientGround;
        [HideInInspector] public bool OrigFog;
        [HideInInspector] public FogMode OrigFogMode;
        [HideInInspector] public Color OrigFogColor;
        [HideInInspector] public float OrigFogDensity;
        [HideInInspector] public Material OrigSkybox;
        [HideInInspector] public LightShadows OrigSunShadows;

        public float Progress { get; private set; }

        private float _from, _to, _blend = 1f;
        private Material _skyClone, _windowClone;
        private Color _windowEmission;
        private const string CloneSuffix = " (Belagerung)";
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        // ---------------- API ----------------

        // Ziel-Fortschritt setzen (0 = Tag, 1 = Nacht); instant = ohne Überblendung
        public void SetProgress(float t, bool instant)
        {
            t = Mathf.Clamp01(t);
            if (instant || !Application.isPlaying)
            {
                _from = _to = Progress = t;
                _blend = 1f;
                Apply(t);
                return;
            }
            if (Mathf.Approximately(t, _to)) return;
            _from = Progress;
            _to = t;
            _blend = 0f;
        }

        void Update()
        {
            if (_blend >= 1f) return;
            _blend = Mathf.Min(1f, _blend + Time.unscaledDeltaTime / Mathf.Max(0.05f, TransitionTime));
            float s = Mathf.SmoothStep(0f, 1f, _blend);
            Progress = Mathf.Lerp(_from, _to, s);
            Apply(Progress);
        }

        void OnDestroy()
        {
            // Laufzeit-Klone entsorgen (Szene wird beim Neustart ohnehin neu geladen)
            if (Application.isPlaying)
            {
                if (_skyClone != null) Destroy(_skyClone);
                if (_windowClone != null) Destroy(_windowClone);
            }
        }

        // Liefert den interpolierten Keyframe für t
        public AtmosphereKey Evaluate(float t)
        {
            var keys = new[] { Day, Afternoon, Dusk, Night };
            if (t <= keys[0].Time) return keys[0];
            for (int i = 1; i < keys.Length; i++)
            {
                if (t <= keys[i].Time)
                {
                    float f = Mathf.InverseLerp(keys[i - 1].Time, keys[i].Time, t);
                    return AtmosphereKey.Lerp(keys[i - 1], keys[i], f);
                }
            }
            return keys[keys.Length - 1];
        }

        // ---------------- Anwenden ----------------

        public void Apply(float t)
        {
            if (!DayCaptured) return;
            var k = Evaluate(t);
            bool day = t <= 0.0001f;

            if (Sun != null)
            {
                Sun.transform.rotation = Quaternion.Euler(k.SunEuler);
                Sun.color = k.SunColor;
                Sun.intensity = k.SunIntensity;
                Sun.shadowStrength = k.ShadowStrength;
            }

            if (day)
            {
                // Exakt der Szenen-Look
                RenderSettings.ambientMode = OrigAmbientMode;
                RenderSettings.ambientSkyColor = OrigAmbientSky;
                RenderSettings.ambientEquatorColor = OrigAmbientEquator;
                RenderSettings.ambientGroundColor = OrigAmbientGround;
                RenderSettings.fog = OrigFog;
                RenderSettings.fogMode = OrigFogMode;
                RenderSettings.fogColor = OrigFogColor;
                RenderSettings.fogDensity = OrigFogDensity;
                if (OrigSkybox != null && RenderSettings.skybox != OrigSkybox) RenderSettings.skybox = OrigSkybox;
            }
            else
            {
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = k.AmbientSky;
                RenderSettings.ambientEquatorColor = k.AmbientEquator;
                RenderSettings.ambientGroundColor = k.AmbientGround;
                RenderSettings.fog = k.FogDensity > 0.0001f;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = k.FogColor;
                RenderSettings.fogDensity = k.FogDensity;
                var sky = SkyClone();
                if (sky != null)
                {
                    SetIf(sky, "_Exposure", k.SkyExposure);
                    SetIf(sky, "_AtmosphereThickness", k.SkyAtmosphere);
                    if (sky.HasProperty("_SkyTint")) sky.SetColor("_SkyTint", k.SkyTint);
                    if (sky.HasProperty("_GroundColor")) sky.SetColor("_GroundColor", k.SkyGround);
                    if (RenderSettings.skybox != sky) RenderSettings.skybox = sky;
                }
            }

            for (int i = 0; i < Lanterns.Count && i < LanternBase.Count; i++)
                if (Lanterns[i] != null) Lanterns[i].intensity = LanternBase[i] * k.LanternFactor;

            ApplyWindows(day ? 1f : k.WindowGlow, day);
        }

        // Stellt den Szenen-Look wieder her und gibt Klone frei (Edit-Vorschau zurücksetzen)
        public void RestoreScene()
        {
            if (!DayCaptured) return;
            Apply(0f);
            // Robust auch nach Domain-Reload: alle Klone anhand des Namens finden und durch die Assets ersetzen
            if (RenderSettings.skybox != null && RenderSettings.skybox.name.EndsWith(CloneSuffix)) RenderSettings.skybox = OrigSkybox;
            if (WindowMaterial != null)
            {
                foreach (var occ in FindObjectsByType<FadeOccluder>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    foreach (var r in occ.GetComponentsInChildren<MeshRenderer>(true))
                        foreach (var m in r.sharedMaterials)
                            if (m != null && m != WindowMaterial && m.name.EndsWith(CloneSuffix)) occ.ReplaceMaterial(m, WindowMaterial);
                foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    var mats = r.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < mats.Length; i++)
                        if (mats[i] != null && mats[i].name == WindowMaterial.name + CloneSuffix) { mats[i] = WindowMaterial; changed = true; }
                    if (changed) r.sharedMaterials = mats;
                }
            }
            _windowSwapped = false;
            if (_skyClone != null) { DestroyImmediate(_skyClone); _skyClone = null; }
            if (_windowClone != null) { DestroyImmediate(_windowClone); _windowClone = null; }
            _from = _to = Progress = 0f;
            _blend = 1f;
        }

        // ---------------- Klone ----------------

        private Material SkyClone()
        {
            if (_skyClone != null) return _skyClone;
            if (OrigSkybox == null) return null;
            _skyClone = new Material(OrigSkybox) { name = OrigSkybox.name + CloneSuffix, hideFlags = HideFlags.DontSave };
            return _skyClone;
        }

        // Fenster-Glühen: bei t = 0 wieder das Asset, sonst ein Klon mit verstärkter Emission
        private void ApplyWindows(float factor, bool restore)
        {
            if (WindowMaterial == null) return;
            if (restore)
            {
                if (_windowClone != null) SwapWindowMaterial(_windowClone, WindowMaterial);
                return;
            }
            if (_windowClone == null)
            {
                _windowClone = new Material(WindowMaterial) { name = WindowMaterial.name + CloneSuffix, hideFlags = HideFlags.DontSave };
                _windowEmission = WindowMaterial.GetColor(EmissionId);
            }
            _windowClone.SetColor(EmissionId, _windowEmission * factor);
            SwapWindowMaterial(WindowMaterial, _windowClone);
        }

        private bool _windowSwapped;

        private void SwapWindowMaterial(Material from, Material to)
        {
            bool toClone = to == _windowClone;
            if (_windowSwapped == toClone) return;
            _windowSwapped = toClone;
            foreach (var occ in FindObjectsByType<FadeOccluder>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                occ.ReplaceMaterial(from, to);
            // Renderer außerhalb von Gebäuden (Requisiten)
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (r.GetComponentInParent<FadeOccluder>(true) != null) continue;
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++) if (mats[i] == from) { mats[i] = to; changed = true; }
                if (changed) r.sharedMaterials = mats;
            }
        }

        private static void SetIf(Material m, string prop, float v)
        {
            if (m.HasProperty(prop)) m.SetFloat(prop, v);
        }
    }
}
