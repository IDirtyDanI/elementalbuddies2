using UnityEngine;

namespace ElementalBuddies
{
    // 3D-Bühne im Hauptmenü: drei Sockel auf einer Drehscheibe (Karussell) mit den Champion-Vorschauen
    // (ChampionDefinitionSO.PreviewPrefab). Der gewählte Champion dreht nach vorne, bekommt Spotlicht und Funken
    // und pendelt langsam hin und her; die Kamera schwebt sanft.
    public class MenuChampionStage : MonoBehaviour
    {
        [System.Serializable]
        public class Pedestal
        {
            public ChampionClass Class;
            [Tooltip("Hier wird das PreviewPrefab instanziert (Füße auf Höhe des Ankers).")]
            public Transform Anchor;
            public Light Spot;
            [Tooltip("Partikel-Funken um den Sockel, nur beim gewählten Champion aktiv (Farbe = Akzentfarbe).")]
            public ParticleSystem Motes;
            [HideInInspector] public GameObject Instance;
            [HideInInspector] public float Turn;
        }

        public Pedestal[] Pedestals = new Pedestal[0];

        [Header("Karussell")]
        [Tooltip("Drehscheibe, deren Kinder die Sockel sind. Sie dreht so, dass der gewählte Sockel zur Kamera zeigt.")]
        public Transform Carousel;
        public float CarouselSmooth = 3.5f;

        [Header("Kamera")]
        public Camera Cam;
        [Tooltip("Blickpunkt, um den die Kamera leicht schwebt (Mitte der Bühne).")]
        public Transform LookTarget;
        [Tooltip("Amplitude der Schwebebewegung (m) in X/Y/Z.")]
        public Vector3 SwayAmplitude = new Vector3(0.2f, 0.1f, 0.12f);
        [Tooltip("Geschwindigkeit der Schwebebewegung (Zyklen pro Sekunde, je Achse).")]
        public Vector3 SwayFrequency = new Vector3(0.05f, 0.08f, 0.035f);

        [Header("Vorschau")]
        [Tooltip("Winkel, um den der gewählte Champion langsam hin und her dreht (Grad).")]
        public float TurnAmplitude = 28f;
        public float TurnSpeed = 0.15f;
        public float SpotIntensitySelected = 60f;
        public float SpotIntensityIdle = 3f;
        public float SpotFade = 3f;

        private Vector3 _camBasePos;
        private Vector3 _lookBase;
        private bool _baseCached;
        private int _selected = -1;
        private float _carouselYaw;
        private float _carouselTarget;
        private float _time;

        public int SelectedIndex { get { return _selected; } }

        void Awake()
        {
            if (Cam == null) Cam = Camera.main;
            CacheBase();
        }

        private void CacheBase()
        {
            if (_baseCached) return;
            _baseCached = true;
            if (Cam != null) _camBasePos = Cam.transform.position;
            _lookBase = LookTarget != null ? LookTarget.position : Vector3.zero;
            if (Carousel != null) _carouselYaw = _carouselTarget = Carousel.localEulerAngles.y;
        }

        // Instanziert die Vorschau-Modelle aller Champions (ersetzt vorhandene).
        public void SpawnPreviews(ChampionDefinitionSO[] defs)
        {
            CacheBase();
            foreach (var p in Pedestals)
            {
                if (p == null || p.Anchor == null) continue;
                if (p.Instance != null) DestroySafe(p.Instance);
                p.Instance = null;

                ChampionDefinitionSO def = Find(defs, p.Class);
                if (def == null || def.PreviewPrefab == null) continue;

                p.Turn = 0f;
                p.Instance = Instantiate(def.PreviewPrefab, p.Anchor.position, Quaternion.identity, p.Anchor);
                p.Instance.name = def.PreviewPrefab.name;
                PrepareAnimators(p.Instance);
                FaceCamera(p);

                if (p.Motes != null)
                {
                    var main = p.Motes.main;
                    main.startColor = new ParticleSystem.MinMaxGradient(def.AccentColor, Color.Lerp(def.AccentColor, Color.white, 0.5f));
                }
                // Spotlicht warmweiß, leicht in der Akzentfarbe getönt
                if (p.Spot != null) p.Spot.color = Color.Lerp(new Color(1f, 0.9f, 0.75f), def.AccentColor, 0.3f);
            }
        }

        // Animator auf Idle stellen (Parameter nur setzen, wenn vorhanden – Controller der neuen Modelle können abweichen)
        private static void PrepareAnimators(GameObject root)
        {
            foreach (var anim in root.GetComponentsInChildren<Animator>())
            {
                anim.applyRootMotion = false;
                anim.updateMode = AnimatorUpdateMode.UnscaledTime;
                if (anim.runtimeAnimatorController == null || !Application.isPlaying) continue;
                foreach (var prm in anim.parameters)
                {
                    if (prm.name == "Grounded" && prm.type == AnimatorControllerParameterType.Bool) anim.SetBool("Grounded", true);
                    if (prm.name == "Speed" && prm.type == AnimatorControllerParameterType.Float) anim.SetFloat("Speed", 0f);
                }
            }
        }

        private static ChampionDefinitionSO Find(ChampionDefinitionSO[] defs, ChampionClass c)
        {
            if (defs == null) return null;
            foreach (var d in defs) if (d != null && d.Class == c) return d;
            return null;
        }

        private static void DestroySafe(GameObject go)
        {
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        // Champion hervorheben. instant = ohne Überblendung (Start, Edit-Vorschau).
        public void Focus(ChampionClass c, bool instant)
        {
            CacheBase();
            _selected = -1;
            for (int i = 0; i < Pedestals.Length; i++)
                if (Pedestals[i] != null && Pedestals[i].Class == c) _selected = i;

            // Drehscheibe so drehen, dass der gewählte Sockel zur Kamera zeigt
            if (Carousel != null && _selected >= 0 && Pedestals[_selected].Anchor != null)
            {
                Vector3 local = Carousel.InverseTransformPoint(Pedestals[_selected].Anchor.position);
                float pedAngle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                Vector3 toCam = Cam != null ? _camBasePos - Carousel.position : Vector3.back;
                float camAngle = Mathf.Atan2(toCam.x, toCam.z) * Mathf.Rad2Deg;
                float target = camAngle - pedAngle;
                _carouselTarget = _carouselYaw + Mathf.DeltaAngle(_carouselYaw, target);
                if (instant) SetCarousel(_carouselTarget);
            }

            for (int i = 0; i < Pedestals.Length; i++)
            {
                var p = Pedestals[i];
                if (p == null) continue;
                bool sel = i == _selected;
                if (p.Motes != null)
                {
                    var em = p.Motes.emission;
                    em.enabled = sel;
                    if (sel && !p.Motes.isPlaying) p.Motes.Play();
                }
                if (instant)
                {
                    if (p.Spot != null) p.Spot.intensity = sel ? SpotIntensitySelected : SpotIntensityIdle;
                    p.Turn = 0f;
                    FaceCamera(p);
                }
            }
            if (instant) ApplyCamera();
        }

        // Gesperrte Champions abgedunkelt zeigen (MaterialPropertyBlock auf _BaseColor/_Color, Materialien bleiben unverändert)
        public void SetLocked(ChampionClass c, bool locked, float brightness = 0.28f)
        {
            foreach (var p in Pedestals)
            {
                if (p == null || p.Class != c || p.Instance == null) continue;
                var block = new MaterialPropertyBlock();
                foreach (var r in p.Instance.GetComponentsInChildren<Renderer>(true))
                {
                    if (r is ParticleSystemRenderer) continue;
                    var mats = r.sharedMaterials;
                    for (int m = 0; m < mats.Length; m++)
                    {
                        if (!locked || mats[m] == null)
                        {
                            r.SetPropertyBlock(null, m);
                            continue;
                        }
                        block.Clear();
                        if (mats[m].HasProperty("_BaseColor"))
                            block.SetColor("_BaseColor", Dim(mats[m].GetColor("_BaseColor"), brightness));
                        if (mats[m].HasProperty("_Color"))
                            block.SetColor("_Color", Dim(mats[m].GetColor("_Color"), brightness));
                        if (mats[m].HasProperty("_EmissionColor"))
                            block.SetColor("_EmissionColor", Color.black);
                        r.SetPropertyBlock(block, m);
                    }
                }
            }
        }

        // Abdunkeln und entsättigen
        private static Color Dim(Color c, float brightness)
        {
            float grey = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
            Color d = Color.Lerp(c, new Color(grey, grey, grey), 0.6f) * brightness;
            d.a = c.a;
            return d;
        }

        private void SetCarousel(float yaw)
        {
            _carouselYaw = yaw;
            if (Carousel != null) Carousel.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        // Champion schaut zur Kamera (plus Pendel-Winkel)
        private void FaceCamera(Pedestal p)
        {
            if (p.Instance == null) return;
            Vector3 camPos = Cam != null ? Cam.transform.position : p.Instance.transform.position + Vector3.back;
            Vector3 dir = camPos - p.Instance.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            p.Instance.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up) * Quaternion.Euler(0f, p.Turn, 0f);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _time += dt;

            if (Carousel != null)
                SetCarousel(Mathf.LerpAngle(_carouselYaw, _carouselTarget, 1f - Mathf.Exp(-CarouselSmooth * dt)));

            for (int i = 0; i < Pedestals.Length; i++)
            {
                var p = Pedestals[i];
                if (p == null) continue;
                bool sel = i == _selected;
                if (p.Spot != null)
                    p.Spot.intensity = Mathf.MoveTowards(p.Spot.intensity, sel ? SpotIntensitySelected : SpotIntensityIdle,
                        (SpotIntensitySelected - SpotIntensityIdle) * SpotFade * dt);
                float targetTurn = sel ? Mathf.Sin(_time * TurnSpeed * Mathf.PI * 2f) * TurnAmplitude : 0f;
                p.Turn = Mathf.Lerp(p.Turn, targetTurn, 1f - Mathf.Exp(-3f * dt));
                FaceCamera(p);
            }

            ApplyCamera();
        }

        // Kamera = Grundposition + leichtes Schweben, Blick auf die Bühnenmitte
        private void ApplyCamera()
        {
            if (Cam == null) return;
            CacheBase();
            Vector3 sway = new Vector3(
                Mathf.Sin(_time * SwayFrequency.x * Mathf.PI * 2f) * SwayAmplitude.x,
                Mathf.Sin(_time * SwayFrequency.y * Mathf.PI * 2f + 1.3f) * SwayAmplitude.y,
                Mathf.Sin(_time * SwayFrequency.z * Mathf.PI * 2f + 2.1f) * SwayAmplitude.z);
            Cam.transform.position = _camBasePos + sway;
            Cam.transform.rotation = Quaternion.LookRotation(_lookBase - Cam.transform.position, Vector3.up);
        }
    }
}
