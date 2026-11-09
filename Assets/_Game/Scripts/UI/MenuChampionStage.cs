using UnityEngine;

namespace ElementalBuddies
{
    // 3D-Bühne im Hauptmenü: Sockel auf einer Drehscheibe (Karussell) mit den Champion-Vorschauen
    // (ChampionDefinitionSO.PreviewPrefab). Der gewählte Champion bekommt Spotlicht und Funken und pendelt langsam hin
    // und her; die Kamera schwebt sanft.
    // Seit 2026-11-09 (Auswahl-Karte mit Pfeilen) im Play Mode „Einzelfigur“: Alle Sockel stehen zur Laufzeit am
    // vorderen Platz der Drehscheibe, vergrößert (SingleScale), und nur der gewählte ist aktiv. Beim Wechsel schiebt sich
    // die alte Figur in Blätterrichtung hinaus (dreht weg, wird kleiner) und die neue von der anderen Seite herein
    // (SwapDuration). Die Szene bleibt unverändert; im Edit-Modus (Vorschau-Renders des Builders) gilt das Karussell.
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
            [HideInInspector] public bool Locked;
            [HideInInspector] public float LockBrightness;
            [HideInInspector] public bool Trying; // Anprobe aktiv (PreviewSkin) – Shop-Änderungen überschreiben sie nicht
            // Einzelfigur: Wechsel-Animation (0 = keine, +1 = schiebt herein, −1 = schiebt hinaus)
            [HideInInspector] public int Swap;
            [HideInInspector] public int SwapDir;
            [HideInInspector] public float SwapT;
            [HideInInspector] public float SwapYaw;
            [HideInInspector] public Vector3 BaseScale = Vector3.one;
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

        [Header("Einzelfigur (Auswahl-Karte, 2026-11-09)")]
        [Tooltip("Play Mode: nur der gewählte Champion steht (vergrößert) vorne auf der Bühne, Wechsel mit Schiebe-Animation.")]
        public bool SingleFigure = true;
        [Tooltip("Vergrößerung des Sockels samt Figur im Einzelfigur-Modus.")]
        public float SingleScale = 1.2f;
        [Tooltip("Dauer des Wechsels (s).")]
        public float SwapDuration = 0.35f;
        [Tooltip("So weit (m, quer zur Kamera) schiebt sich die Figur hinaus bzw. kommt herein.")]
        public float SwapDistance = 2.4f;
        [Tooltip("Drehung (Grad) der Figur am Rand des Wechsels.")]
        public float SwapTurn = 70f;
        [Tooltip("Größe der Figur am Rand des Wechsels (1 = unverändert).")]
        public float SwapShrink = 0.6f;

        private Vector3 _camBasePos;
        private Vector3 _lookBase;
        private bool _baseCached;
        private int _selected = -1;
        private float _carouselYaw;
        private float _carouselTarget;
        private float _time;
        private bool _singleLaidOut;

        public int SelectedIndex { get { return _selected; } }

        // Einzelfigur-Modus aktiv (nur im Play Mode, damit der Builder im Edit-Modus das Karussell rendert)
        public bool Single => SingleFigure && Application.isPlaying && Carousel != null && Pedestals.Length > 0;

        void Awake()
        {
            if (Cam == null) Cam = Camera.main;
            CacheBase();
        }

        void OnEnable()
        {
            ShopInventory.Changed -= HandleShopChanged;
            ShopInventory.Changed += HandleShopChanged;
        }

        void OnDisable()
        {
            ShopInventory.Changed -= HandleShopChanged;
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
            EnsurePedestals(defs);
            if (Single) LayoutSingle();
            foreach (var p in Pedestals)
            {
                if (p == null || p.Anchor == null) continue;
                if (p.Instance != null) DestroySafe(p.Instance);
                p.Instance = null;

                ChampionDefinitionSO def = Find(defs, p.Class);
                if (def == null || def.PreviewPrefab == null) continue;

                p.Turn = 0f;
                p.Trying = false;
                p.Instance = Instantiate(def.PreviewPrefab, p.Anchor.position, Quaternion.identity, p.Anchor);
                p.Instance.name = def.PreviewPrefab.name;
                p.BaseScale = p.Instance.transform.localScale;
                p.Swap = 0;
                p.SwapYaw = 0f;
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
            ApplyEquippedSkins();
        }

        // Fehlende Sockel (z. B. Schamane, 2026-11-09) zur Laufzeit als Kopie des letzten Sockels anlegen und alle Sockel
        // gleichmäßig auf der Drehscheibe verteilen (5 Sockel: 72°-Schritte, Reihenfolge = defs). Szene bleibt unverändert.
        private void EnsurePedestals(ChampionDefinitionSO[] defs)
        {
            if (!Application.isPlaying || defs == null || Carousel == null || Pedestals.Length == 0) return;
            var template = Pedestals[Pedestals.Length - 1];
            if (template == null || template.Anchor == null) return;
            var list = new System.Collections.Generic.List<Pedestal>(Pedestals);
            foreach (var d in defs)
            {
                if (d == null || FindPedestal(list, d.Class) != null) continue;
                var src = template.Anchor.parent;
                var go = Instantiate(src.gameObject, src.parent);
                go.name = "Pedestal_" + d.Class;
                var p = new Pedestal { Class = d.Class };
                p.Anchor = go.transform.Find(template.Anchor.name);
                if (template.Spot != null) { var t = go.transform.Find(template.Spot.name); if (t != null) p.Spot = t.GetComponent<Light>(); }
                if (template.Motes != null) { var t = go.transform.Find(template.Motes.name); if (t != null) p.Motes = t.GetComponent<ParticleSystem>(); }
                // Vorschau der Vorlage nicht mitkopieren
                if (p.Anchor != null) for (int i = p.Anchor.childCount - 1; i >= 0; i--) Destroy(p.Anchor.GetChild(i).gameObject);
                list.Add(p);
            }
            if (list.Count == Pedestals.Length) return;

            // Reihenfolge wie defs, Winkel 180° = vorne, im Uhrzeigersinn (wie MainMenuBuilder)
            var ordered = new System.Collections.Generic.List<Pedestal>();
            foreach (var d in defs) { var p = d != null ? FindPedestal(list, d.Class) : null; if (p != null && !ordered.Contains(p)) ordered.Add(p); }
            foreach (var p in list) if (!ordered.Contains(p)) ordered.Add(p);
            Vector3 first = template.Anchor.parent.localPosition;
            float radius = new Vector2(first.x, first.z).magnitude;
            if (radius < 0.01f) radius = 1.45f;
            float step = 360f / ordered.Count;
            for (int i = 0; i < ordered.Count; i++)
            {
                var ped = ordered[i].Anchor != null ? ordered[i].Anchor.parent : null;
                if (ped == null) continue;
                float a = 180f + i * step;
                ped.localPosition = new Vector3(Mathf.Sin(a * Mathf.Deg2Rad), 0f, Mathf.Cos(a * Mathf.Deg2Rad)) * radius + Vector3.up * first.y;
                ped.localRotation = Quaternion.Euler(0f, a + 180f, 0f);
            }
            Pedestals = ordered.ToArray();
        }

        // Einzelfigur: alle Sockel an den vorderen Platz der Drehscheibe (zur Kamera hin), vergrößert; sichtbar ist nur der
        // gewählte (Focus). Der Platz ist derselbe, an dem vorher der gewählte Sockel stand (ShopUI.FrontPoint gilt weiter).
        private void LayoutSingle()
        {
            if (_singleLaidOut || Carousel == null) return;
            _singleLaidOut = true;
            Vector3 first = Vector3.zero;
            float radius = 0f;
            foreach (var p in Pedestals)
            {
                var ped = p != null && p.Anchor != null ? p.Anchor.parent : null;
                if (ped == null || ped.parent != Carousel) continue;
                Vector3 lp = ped.localPosition;
                if (radius <= 0f) first = lp;
                radius = Mathf.Max(radius, new Vector2(lp.x, lp.z).magnitude);
            }
            if (radius < 0.01f) radius = 1.45f;
            Vector3 toCam = Cam != null ? _camBasePos - Carousel.position : Vector3.back;
            toCam.y = 0f;
            if (toCam.sqrMagnitude < 0.0001f) toCam = Vector3.back;
            Vector3 dir = Carousel.InverseTransformDirection(toCam.normalized);
            dir.y = 0f;
            dir.Normalize();
            float a = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            foreach (var p in Pedestals)
            {
                var ped = p != null && p.Anchor != null ? p.Anchor.parent : null;
                if (ped == null || ped.parent != Carousel) continue;
                ped.localPosition = dir * radius + Vector3.up * first.y;
                ped.localRotation = Quaternion.Euler(0f, a + 180f, 0f); // wie EnsurePedestals: Sockel schaut zur Mitte
                ped.localScale = Vector3.one * SingleScale;
                ped.gameObject.SetActive(false); // Focus schaltet den gewählten ein
            }
        }

        private static Pedestal FindPedestal(System.Collections.Generic.List<Pedestal> list, ChampionClass c)
        {
            foreach (var p in list) if (p != null && p.Class == c) return p;
            return null;
        }

        // ---------------- Skins (Shop) ----------------

        // Ausgerüstete Skins (ShopInventory) auf alle Vorschauen ohne laufende Anprobe
        public void ApplyEquippedSkins()
        {
            if (!Application.isPlaying) return;
            foreach (var p in Pedestals)
                if (p != null && p.Instance != null && !p.Trying) ApplySkin(p, ShopInventory.GetEquipped(p.Class, ShopSlot.Weapon), ShopInventory.GetEquipped(p.Class, ShopSlot.Armor));
        }

        // Anprobe: Items live auf der Vorschau der Klasse zeigen (null = Standard für diesen Slot)
        public void PreviewSkin(ChampionClass cls, ShopItemSO weapon, ShopItemSO armor)
        {
            if (!Application.isPlaying) return;
            foreach (var p in Pedestals)
            {
                if (p == null || p.Class != cls || p.Instance == null) continue;
                p.Trying = true;
                ApplySkin(p, weapon, armor);
            }
        }

        // Anprobe beenden: zurück zur ausgerüsteten Ausrüstung
        public void ResetPreview(ChampionClass cls)
        {
            if (!Application.isPlaying) return;
            foreach (var p in Pedestals)
            {
                if (p == null || p.Class != cls || p.Instance == null) continue;
                p.Trying = false;
                ApplySkin(p, ShopInventory.GetEquipped(cls, ShopSlot.Weapon), ShopInventory.GetEquipped(cls, ShopSlot.Armor));
            }
        }

        private void ApplySkin(Pedestal p, ShopItemSO weapon, ShopItemSO armor)
        {
            ChampionSkin.ApplyTo(p.Instance, p.Class, weapon, armor);
            // Abdunkelung gesperrter Champions hängt an den Materialien → nach dem Tausch neu setzen
            if (p.Locked) SetLocked(p.Class, true, p.LockBrightness);
        }

        private void HandleShopChanged() => ApplyEquippedSkins();

        // Animator auf Idle stellen (Parameter nur setzen, wenn vorhanden – Controller der neuen Modelle können abweichen)
        private static void PrepareAnimators(GameObject root)
        {
            foreach (var anim in root.GetComponentsInChildren<Animator>(true))
            {
                anim.applyRootMotion = false;
                anim.keepAnimatorStateOnDisable = true; // Einzelfigur: Sockel werden aus- und eingeschaltet
                anim.updateMode = AnimatorUpdateMode.UnscaledTime;
                // Parameter nur an aktiven Animatoren setzen (Einzelfigur: Sockel inaktiv → beim Einschalten erneut)
                if (anim.runtimeAnimatorController == null || !Application.isPlaying || !anim.isActiveAndEnabled) continue;
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
        public void Focus(ChampionClass c, bool instant) => Focus(c, instant, 0);

        // dir = Blätterrichtung für die Wechsel-Animation (+1 nächste, −1 vorige, 0 = kürzerer Weg)
        public void Focus(ChampionClass c, bool instant, int dir)
        {
            CacheBase();
            int prev = _selected;
            _selected = -1;
            for (int i = 0; i < Pedestals.Length; i++)
                if (Pedestals[i] != null && Pedestals[i].Class == c) _selected = i;

            if (Single) FocusSingle(prev, instant, dir);
            // Drehscheibe so drehen, dass der gewählte Sockel zur Kamera zeigt
            else if (Carousel != null && _selected >= 0 && Pedestals[_selected].Anchor != null)
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

        // ---------------- Einzelfigur ----------------

        private void FocusSingle(int prev, bool instant, int dir)
        {
            LayoutSingle();
            bool animate = !instant && prev >= 0 && prev < Pedestals.Length && prev != _selected && _selected >= 0
                           && SwapDuration > 0.01f && Pedestals[prev] != null && Pedestals[prev].Instance != null
                           && Pedestals[prev].Anchor != null && Pedestals[prev].Anchor.parent.gameObject.activeSelf;
            if (prev == _selected && _selected >= 0)
            {
                SetPedestalActive(Pedestals[_selected], true); // gleiche Wahl (Lobby, Shop schließen): nichts animieren
                return;
            }
            if (dir == 0) dir = ChampionCarouselRules.Direction(prev, _selected, Pedestals.Length);
            if (dir == 0) dir = 1;
            for (int i = 0; i < Pedestals.Length; i++)
            {
                var p = Pedestals[i];
                if (p == null) continue;
                if (i == _selected)
                {
                    SetPedestalActive(p, true);
                    SetBaseVisible(p, true);
                    p.Swap = animate ? 1 : 0;
                    p.SwapDir = dir;
                    p.SwapT = 0f;
                }
                else if (animate && i == prev)
                {
                    // Sockel bleibt (der neue steht an derselben Stelle), nur die Figur schiebt sich hinaus
                    SetBaseVisible(p, false);
                    p.Swap = -1;
                    p.SwapDir = dir;
                    p.SwapT = 0f;
                }
                else
                {
                    p.Swap = 0;
                    SetPedestalActive(p, false);
                }
                ApplySwapPose(p);
            }
        }

        private void SetPedestalActive(Pedestal p, bool on)
        {
            var ped = p != null && p.Anchor != null ? p.Anchor.parent : null;
            if (ped == null) return;
            if (!on)
            {
                p.Swap = 0;
                p.SwapYaw = 0f;
                ResetFigure(p);
                SetBaseVisible(p, true);
            }
            if (ped.gameObject.activeSelf == on) return;
            ped.gameObject.SetActive(on);
            if (on && p.Instance != null) PrepareAnimators(p.Instance);
        }

        // Steinsockel (alles außer Figur und Funken) ein-/ausblenden – zwei gleiche Sockel am selben Platz würden flackern
        private static void SetBaseVisible(Pedestal p, bool on)
        {
            var ped = p != null && p.Anchor != null ? p.Anchor.parent : null;
            if (ped == null) return;
            foreach (var r in ped.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                if (p.Instance != null && r.transform.IsChildOf(p.Instance.transform)) continue;
                r.enabled = on;
            }
        }

        private static void ResetFigure(Pedestal p)
        {
            if (p.Instance == null || p.Anchor == null) return;
            p.Instance.transform.position = p.Anchor.position;
            p.Instance.transform.localScale = p.BaseScale;
        }

        // Figur während des Wechsels: seitlich versetzt (quer zur Kamera), gedreht und verkleinert
        private void ApplySwapPose(Pedestal p)
        {
            if (p == null || p.Instance == null || p.Anchor == null) return;
            if (p.Swap == 0)
            {
                p.SwapYaw = 0f;
                ResetFigure(p);
                return;
            }
            // Neue Figur: von der Seite der Blätterrichtung (+1 = von rechts) zur Mitte; alte: zur anderen Seite hinaus
            float e = ChampionCarouselRules.EaseOut(p.SwapT);
            float away = p.Swap > 0 ? 1f - e : e;          // 0 = in der Mitte, 1 = am Rand
            float side = p.Swap > 0 ? p.SwapDir : -p.SwapDir;
            Vector3 right = Cam != null ? Cam.transform.right : Vector3.right;
            right.y = 0f;
            if (right.sqrMagnitude < 0.0001f) right = Vector3.right;
            right.Normalize();
            p.Instance.transform.position = p.Anchor.position + right * (side * SwapDistance * away * SingleScale);
            p.Instance.transform.localScale = p.BaseScale * Mathf.Lerp(1f, SwapShrink, away);
            p.SwapYaw = side * SwapTurn * away;
        }

        private void UpdateSwaps(float dt)
        {
            for (int i = 0; i < Pedestals.Length; i++)
            {
                var p = Pedestals[i];
                if (p == null || p.Swap == 0) continue;
                p.SwapT += dt / Mathf.Max(0.01f, SwapDuration);
                if (p.SwapT >= 1f)
                {
                    if (p.Swap < 0) SetPedestalActive(p, false); // alte Figur ist draußen
                    p.Swap = 0;
                }
                ApplySwapPose(p);
            }
        }

        // Gesperrte Champions abgedunkelt zeigen (MaterialPropertyBlock auf _BaseColor/_Color, Materialien bleiben unverändert)
        public void SetLocked(ChampionClass c, bool locked, float brightness = 0.28f)
        {
            foreach (var p in Pedestals)
            {
                if (p == null || p.Class != c || p.Instance == null) continue;
                p.Locked = locked;
                p.LockBrightness = brightness;
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
            p.Instance.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up) * Quaternion.Euler(0f, p.Turn + p.SwapYaw, 0f);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _time += dt;

            if (Carousel != null && !Single)
                SetCarousel(Mathf.LerpAngle(_carouselYaw, _carouselTarget, 1f - Mathf.Exp(-CarouselSmooth * dt)));
            if (Single) UpdateSwaps(dt);

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
