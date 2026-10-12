using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Optik der Belagerungsramme (alle Rechner, rein kosmetisch, kein Netz). Sitzt auf dem „Visual“-Kind des Ram-Prefabs
    // (Editor/RamBuilder):
    //  - Räder drehen sich mit der tatsächlichen Bewegung (Positions-Delta der Wurzel → klappt auch auf Clients mit
    //    NetworkTransform; eingefroren = keine Bewegung = kein Drehen)
    //  - der Aufbau schaukelt beim Fahren leicht
    //  - der Rammbalken pendelt bei jedem Angriff (EnemyBrain.OnAttackVisual: Ausholen, Zustoßen, Nachschwingen)
    //  - beim Tod zerfällt die Ramme in Trümmer (EnemyBrain.OnEnemyKilled feuert auf Server und Clients vor dem Despawn)
    // Drehachse von Rädern und Balken ist die Querachse der Ramme (Wurzel-X), unabhängig von den lokalen Achsen im FBX.
    public class RamVisual : MonoBehaviour
    {
        [Header("Teile")]
        [Tooltip("Modell-Wurzel (Pivot am Boden): schaukelt; Trümmer übernehmen Lage und Größe.")]
        public Transform Model;
        [Tooltip("Räder, Pivot in der Radmitte.")]
        public Transform[] Wheels;
        [Tooltip("Rammbalken, Pivot am Scharnier auf dem Dachfirst.")]
        public Transform Beam;
        [Tooltip("Trümmer-Modell (Ramme_Truemmer): Teile = direkte Kinder, an ihrer Stelle am intakten Modell.")]
        public GameObject DebrisPrefab;

        [Header("Räder")]
        [Tooltip("Radius in Einheiten dieses Visual-Objekts (= echte Meter, sobald die Elite-Skalierung greift).")]
        public float WheelRadius = 0.45f;
        [Tooltip("-1 dreht die Räder andersherum.")]
        public float WheelSpinSign = 1f;

        [Header("Schaukeln")]
        [Tooltip("Ab dieser Geschwindigkeit (m/s) schaukelt die Ramme voll.")]
        public float FullSpeed = 2f;
        [Tooltip("Auf und Ab in Einheiten dieses Visual-Objekts.")]
        public float BobHeight = 0.03f;
        [Tooltip("Rollen/Nicken in Grad.")]
        public float RockAngle = 1.2f;
        [Tooltip("Schaukel-Frequenz (Hz) bei voller Fahrt.")]
        public float RockFrequency = 1.6f;

        [Header("Rammstoß")]
        [Tooltip("Ausholen nach hinten (Grad).")]
        public float PullBackAngle = 32f;
        [Tooltip("Zustoßen nach vorn (Grad, negativ = vor die Ruhelage).")]
        public float StrikeAngle = -14f;
        public float PullBackTime = 0.45f;
        public float StrikeTime = 0.12f;
        public float SettleTime = 0.6f;
        [Tooltip("-1 kehrt die Pendelrichtung um.")]
        public float SwingSign = 1f;
        [Tooltip("Kamera-Wackeln beim Einschlag (mit Abstands-Abfall zur eigenen Figur).")]
        public float ImpactTrauma = 0.1f;
        [Tooltip("Optional: Staubwolke am Rammkopf beim Einschlag (z. B. VFX_RollDust).")]
        public GameObject ImpactVfx;
        public float ImpactVfxScale = 1.6f;

        [Header("Trümmer")]
        public float DebrisLifetime = 4f;
        [Tooltip("Am Ende der Lebenszeit so lange einsinken.")]
        public float DebrisSinkTime = 1.2f;
        [Tooltip("Anfangs-Tempo der Teile nach außen/oben (m/s).")]
        public float DebrisImpulse = 4.5f;

        private EnemyBrain _brain;
        private Transform _root;

        private Vector3 _lastPos;
        private bool _hasLastPos;
        private float _speed;      // geglättet, m/s
        private float _rockPhase;
        private float _wheelAngle;

        private Quaternion[] _wheelRest;
        private Vector3[] _wheelAxis;  // Querachse im lokalen Raum des Rades
        private Vector3 _modelRestPos;
        private Quaternion _modelRestRot;
        private Quaternion _beamRest;
        private Vector3 _beamAxis;
        private Vector3 _headLocal;    // Rammkopf (Vorderkante des Balkens) im Wurzel-Raum

        private float _swingT = -1f;   // < 0: Balken ruht
        private bool _impactDone;
        private bool _shattered;

        void Awake()
        {
            _brain = GetComponentInParent<EnemyBrain>();
            _root = _brain != null ? _brain.transform : transform.root;
            if (Model == null) Model = transform.childCount > 0 ? transform.GetChild(0) : transform;
            if (Wheels == null || Wheels.Length == 0) Wheels = FindWheels(Model);
            if (Beam == null) Beam = Find(Model, "Rammbalken");

            // Ruhelagen und Drehachsen (Querachse der Ramme) im Prefab-Zustand merken
            _modelRestPos = Model.localPosition;
            _modelRestRot = Model.localRotation;
            _wheelRest = new Quaternion[Wheels.Length];
            _wheelAxis = new Vector3[Wheels.Length];
            for (int i = 0; i < Wheels.Length; i++)
            {
                if (Wheels[i] == null) continue;
                _wheelRest[i] = Wheels[i].localRotation;
                _wheelAxis[i] = Wheels[i].InverseTransformDirection(_root.right).normalized;
            }
            if (Beam != null)
            {
                _beamRest = Beam.localRotation;
                _beamAxis = Beam.InverseTransformDirection(_root.right).normalized;
                _headLocal = FrontPoint(Beam, _root);
            }
        }

        void OnEnable()
        {
            if (_brain != null) _brain.OnAttackVisual += HandleAttack;
            EnemyBrain.OnEnemyKilled += HandleKilled;
            _hasLastPos = false;
        }

        void OnDisable()
        {
            if (_brain != null) _brain.OnAttackVisual -= HandleAttack;
            EnemyBrain.OnEnemyKilled -= HandleKilled;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || _shattered) return;
            bool frozen = _brain != null && _brain.IsFrozen; // eingefroren: alles steht (auch ein laufender Stoß)

            // Bewegung aus dem Positions-Delta (Teleport/Warp ignorieren)
            Vector3 pos = _root.position;
            Vector3 delta = _hasLastPos ? pos - _lastPos : Vector3.zero;
            _lastPos = pos;
            _hasLastPos = true;
            if (delta.sqrMagnitude > 9f) delta = Vector3.zero;
            delta.y = 0f;
            float instSpeed = frozen ? 0f : delta.magnitude / dt;
            _speed = Mathf.Lerp(_speed, instSpeed, 1f - Mathf.Exp(-8f * dt));

            UpdateWheels(frozen ? 0f : Vector3.Dot(delta, _root.forward));
            if (!frozen) UpdateRock(dt);
            if (!frozen) UpdateSwing(dt);
        }

        // ---------------- Räder & Schaukeln ----------------

        private void UpdateWheels(float forwardDistance)
        {
            if (Wheels.Length == 0 || Mathf.Approximately(forwardDistance, 0f)) return;
            float scale = Mathf.Max(0.01f, transform.lossyScale.y);
            float radius = Mathf.Max(0.05f, WheelRadius) * scale;
            _wheelAngle = Mathf.Repeat(_wheelAngle + forwardDistance / radius * Mathf.Rad2Deg * WheelSpinSign, 360f);
            for (int i = 0; i < Wheels.Length; i++)
                if (Wheels[i] != null) Wheels[i].localRotation = _wheelRest[i] * Quaternion.AngleAxis(_wheelAngle, _wheelAxis[i]);
        }

        private void UpdateRock(float dt)
        {
            if (Model == null || Model == transform) return;
            float k = Mathf.Clamp01(_speed / Mathf.Max(0.1f, FullSpeed));
            if (k < 0.01f)
            {
                // im Stand weich in die Ruhelage
                float s = 1f - Mathf.Exp(-6f * dt);
                Model.localPosition = Vector3.Lerp(Model.localPosition, _modelRestPos, s);
                Model.localRotation = Quaternion.Slerp(Model.localRotation, _modelRestRot, s);
                return;
            }
            _rockPhase += dt * RockFrequency * (0.5f + 0.5f * k) * Mathf.PI * 2f;
            float bob = Mathf.Abs(Mathf.Sin(_rockPhase)) * BobHeight * k;
            float roll = Mathf.Sin(_rockPhase) * RockAngle * k;
            float pitch = Mathf.Sin(_rockPhase * 2f + 0.7f) * RockAngle * 0.5f * k;
            Model.localPosition = _modelRestPos + Vector3.up * bob;
            Model.localRotation = Quaternion.Euler(pitch, 0f, roll) * _modelRestRot;
        }

        // ---------------- Rammstoß ----------------

        private void HandleAttack()
        {
            if (_shattered || Beam == null) return;
            // Läuft der Stoß noch (Ausholen/Zustoßen), nicht neu beginnen; im Nachschwingen schon
            if (_swingT >= 0f && _swingT < PullBackTime + StrikeTime) return;
            _swingT = 0f;
            _impactDone = false;
        }

        private void UpdateSwing(float dt)
        {
            if (Beam == null || _swingT < 0f) return;
            _swingT += dt;
            float t = _swingT;
            float angle;
            if (t < PullBackTime)
            {
                float u = t / Mathf.Max(0.01f, PullBackTime);
                angle = PullBackAngle * (u * u * (3f - 2f * u)); // weich ausholen
            }
            else if (t < PullBackTime + StrikeTime)
            {
                float u = (t - PullBackTime) / Mathf.Max(0.01f, StrikeTime);
                angle = Mathf.Lerp(PullBackAngle, StrikeAngle, u * u); // beschleunigt zustoßen
            }
            else
            {
                if (!_impactDone) { _impactDone = true; Impact(); }
                float u = (t - PullBackTime - StrikeTime) / Mathf.Max(0.01f, SettleTime);
                if (u >= 1f) { angle = 0f; _swingT = -1f; }
                else angle = StrikeAngle * Mathf.Cos(u * Mathf.PI * 2.5f) * (1f - u) * (1f - u); // gedämpft nachschwingen
            }
            Beam.localRotation = _beamRest * Quaternion.AngleAxis(angle * SwingSign, _beamAxis);
        }

        private void Impact()
        {
            Vector3 head = _root.TransformPoint(_headLocal);
            if (ImpactVfx != null) CombatUtil.SpawnFx(ImpactVfx, head, Quaternion.LookRotation(_root.forward), 2f, ImpactVfxScale);
            GameAudio.PlayPitched(SfxId.HeavyImpact, head, 0.7f);
            if (ImpactTrauma > 0f) CameraFollow.AddTraumaAt(ImpactTrauma, head, 18f);
        }

        // ---------------- Tod: Trümmer ----------------

        private void HandleKilled(EnemyBrain e)
        {
            if (_shattered || e == null || e != _brain) return;
            _shattered = true;
            if (DebrisPrefab != null && Model != null)
            {
                // wie ein Geschwister des Modells einsetzen (gleiche Import-Transformation), dann von der Ramme lösen
                var go = Instantiate(DebrisPrefab, Model.parent != null ? Model.parent : transform, false);
                go.name = "RamDebris";
                go.transform.SetParent(null, true);
                RamDebris.Launch(go, Model.position, DebrisImpulse, DebrisLifetime, DebrisSinkTime);
                if (ImpactVfx != null) CombatUtil.SpawnFx(ImpactVfx, Model.position, Quaternion.identity, 2.5f, ImpactVfxScale * 1.8f);
            }
            // intakte Ramme sofort ausblenden (Despawn folgt auf Clients erst mit der Netz-Nachricht)
            if (Model != null && Model != transform) Model.gameObject.SetActive(false);
        }

        // ---------------- Hilfen ----------------

        private static Transform[] FindWheels(Transform model)
        {
            var list = new List<Transform>();
            foreach (var n in new[] { "Rad_VL", "Rad_VR", "Rad_HL", "Rad_HR" })
            {
                var t = Find(model, n);
                if (t != null) list.Add(t);
            }
            return list.ToArray();
        }

        // Name exakt oder mit Blender-Suffix („Rad_VL.001“)
        public static Transform Find(Transform root, string name)
        {
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name || t.name.StartsWith(name + ".")) return t;
            return null;
        }

        // Vorderster Punkt (max. Z) der Meshes unter part, im lokalen Raum von space (Mitte in X/Y)
        private static Vector3 FrontPoint(Transform part, Transform space)
        {
            bool any = false;
            Vector3 best = Vector3.zero;
            float sumX = 0f, sumY = 0f;
            int n = 0;
            foreach (var mf in part.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Bounds b = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = space.InverseTransformPoint(mf.transform.TransformPoint(c));
                    if (!any || p.z > best.z) best = p;
                    any = true;
                    sumX += p.x; sumY += p.y; n++;
                }
            }
            if (!any) return space.InverseTransformPoint(part.position);
            return new Vector3(sumX / n, sumY / n, best.z);
        }
    }

    // Trümmer der Ramme (lokal, kein Netz): Teile fliegen nach außen/oben, rollen aus, sinken ein und verschwinden.
    // Layer „Ignore Raycast“ (keine Bau-/Boden-/Hindernis-Strahlen) und keine Kollision mit Spieler, Gegnern, Buddies.
    public class RamDebris : MonoBehaviour
    {
        private readonly List<Transform> _pieces = new List<Transform>();
        private readonly List<Vector3> _sinkFrom = new List<Vector3>();
        private readonly List<Vector3> _scaleFrom = new List<Vector3>();
        private float _age, _lifetime, _sinkTime;
        private bool _sinking;

        public static RamDebris Launch(GameObject go, Vector3 center, float impulse, float lifetime, float sinkTime)
        {
            var d = go.AddComponent<RamDebris>();
            d._lifetime = Mathf.Max(0.5f, lifetime);
            d._sinkTime = Mathf.Clamp(sinkTime, 0.1f, d._lifetime);
            d.Setup(center, impulse);
            return d;
        }

        private void Setup(Vector3 center, float impulse)
        {
            int layer = LayerMask.NameToLayer("Ignore Raycast");
            int exclude = LayerMask.GetMask("Player", "Enemy", "Buddy");
            foreach (Transform piece in transform)
                if (piece.GetComponentInChildren<Renderer>() != null) _pieces.Add(piece);
            if (_pieces.Count == 0) _pieces.Add(transform);

            foreach (var piece in _pieces)
            {
                foreach (var t in piece.GetComponentsInChildren<Transform>(true)) if (layer >= 0) t.gameObject.layer = layer;
                // Box statt konvexem MeshCollider: braucht kein lesbares Mesh
                foreach (var mf in piece.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null || mf.GetComponent<Collider>() != null) continue;
                    var box = mf.gameObject.AddComponent<BoxCollider>();
                    box.center = mf.sharedMesh.bounds.center;
                    box.size = Vector3.Max(mf.sharedMesh.bounds.size, Vector3.one * 0.05f);
                    box.excludeLayers = exclude;
                }
                var rb = piece.gameObject.AddComponent<Rigidbody>();
                var rend = piece.GetComponentInChildren<Renderer>();
                Vector3 size = rend != null ? rend.bounds.size : Vector3.one * 0.5f;
                rb.mass = Mathf.Clamp(size.x * size.y * size.z * 60f, 2f, 60f);
                rb.excludeLayers = exclude;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.linearDamping = 0.2f;
                rb.angularDamping = 0.6f;

                Vector3 pc = rend != null ? rend.bounds.center : piece.position;
                Vector3 dir = pc - center;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.01f) dir = Random.insideUnitSphere;
                dir.y = 0f;
                dir = dir.normalized * Random.Range(0.6f, 1.1f) + Vector3.up * Random.Range(0.8f, 1.4f);
                rb.AddForce(dir * impulse, ForceMode.VelocityChange);
                rb.AddTorque(Random.insideUnitSphere * 6f, ForceMode.VelocityChange);
            }
        }

        void Update()
        {
            _age += Time.deltaTime;
            if (_age >= _lifetime) { Destroy(gameObject); return; }
            float sinkStart = _lifetime - _sinkTime;
            if (_age < sinkStart) return;

            if (!_sinking)
            {
                // Physik aus, dann gleichmäßig einsinken und schrumpfen
                _sinking = true;
                foreach (var p in _pieces)
                {
                    var rb = p.GetComponent<Rigidbody>();
                    if (rb != null) { rb.interpolation = RigidbodyInterpolation.None; rb.isKinematic = true; }
                    foreach (var c in p.GetComponentsInChildren<Collider>()) c.enabled = false;
                    _sinkFrom.Add(p.position);
                    _scaleFrom.Add(p.localScale);
                }
            }
            float u = Mathf.Clamp01((_age - sinkStart) / _sinkTime);
            for (int i = 0; i < _pieces.Count; i++)
            {
                if (_pieces[i] == null) continue;
                _pieces[i].position = _sinkFrom[i] + Vector3.down * (0.8f * u * u);
                _pieces[i].localScale = _scaleFrom[i] * (1f - 0.5f * u);
            }
        }
    }
}
