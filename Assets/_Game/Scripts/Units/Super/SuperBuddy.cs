using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ElementalBuddies
{
    // Basis der Super-Elementare (Tri-Fusion aus drei Basis-Elementen): eine Stufe, keine weitere Fusion,
    // Schrein-Bonus = Produkt der drei Eltern-Elemente, optionale Schadensreduktion.
    // Alle Visuals haben Laufzeit-Platzhalter; fehlt der Config ein Prefab, baut CreateRuntime einen Platzhalter-Buddy.
    public abstract class SuperBuddy : FusionBuddy
    {
        [Header("Super-Elementar")]
        [HideInInspector] public int ParentElementC = -1;
        [Range(0f, 0.9f)] public float DamageReduction = 0f;
        [Tooltip("Optional: Kind-Objekt mit dem Modell (leer = Kind \"Visual\").")]
        public Transform Visual;

        public override bool IsSuper => true;
        public int ParentC => ParentElementC >= 0 ? ParentElementC : FusionInfo.GetDefaultParents(Element).z;
        public override int ElementMask => base.ElementMask | (1 << ParentC);

        protected override float ShrineDamageMultiplier =>
            base.ShrineDamageMultiplier * ShrineBonuses.GetDamageMultiplier(Mathf.Clamp(ParentC, 0, ShrineBonuses.ElementCount - 1));

        protected virtual void Awake()
        {
            if (Visual == null) Visual = transform.Find("Visual");
        }

        public override void TakeDamage(float amount)
        {
            if (amount <= 0f || IsDead) return;
            base.TakeDamage(amount * (1f - Mathf.Clamp(DamageReduction, 0f, 0.9f)));
        }

        // ---------------- Helfer ----------------

        private static readonly List<EnemyBrain> _densityBuffer = new List<EnemyBrain>();

        // Gegner (im Suchradius) mit den meisten Nachbarn im clusterRadius – wie MagmaBuddy; null = keiner
        protected static EnemyBrain FindDensestEnemy(Vector3 center, float searchRadius, float clusterRadius, out int count)
        {
            count = 0;
            var candidates = new List<EnemyBrain>(FindEnemies(center, searchRadius));
            if (candidates.Count == 0) return null;
            _densityBuffer.Clear();
            _densityBuffer.AddRange(FindEnemies(center, searchRadius + clusterRadius));
            float r2 = clusterRadius * clusterRadius;
            EnemyBrain best = null;
            int bestCount = -1;
            foreach (var c in candidates)
            {
                if (c == null || c.IsDead) continue;
                Vector3 cp = c.transform.position;
                int n = 0;
                foreach (var o in _densityBuffer)
                {
                    if (o == null) continue;
                    Vector3 d = o.transform.position - cp;
                    d.y = 0f;
                    if (d.sqrMagnitude <= r2) n++;
                }
                if (n > bestCount)
                {
                    bestCount = n;
                    best = c;
                }
            }
            count = Mathf.Max(0, bestCount);
            return best;
        }

        // Festhalten wie die Dornenfalle des Bogenschützen: 100 % Verlangsamung, Agent sofort gestoppt, Angriffe bleiben möglich
        public static void Root(EnemyBrain enemy, float duration)
        {
            if (enemy == null || enemy.IsDead || duration <= 0f) return;
            enemy.ApplySlow(1f, duration);
            var agent = enemy.GetComponent<NavMeshAgent>();
            if (agent != null && agent.isOnNavMesh)
            {
                agent.speed = 0f;
                agent.velocity = Vector3.zero;
            }
        }

        // Boden unter einem Punkt (Gegner, Spieler, Buddies, Trigger ignoriert); Fallback = Punkt
        public static Vector3 GroundPoint(Vector3 point)
        {
            var hits = Physics.RaycastAll(point + Vector3.up * 3f, Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore);
            float bestDist = float.MaxValue;
            Vector3 result = point;
            foreach (var h in hits)
            {
                if (h.collider.CompareTag("Enemy") || h.collider.CompareTag("Player")) continue;
                if (h.collider.GetComponentInParent<ElementalBuddy>() != null) continue;
                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    result = h.point;
                }
            }
            return result;
        }

        private static Material _runtimeMaterial;

        // Unbeleuchtetes Laufzeit-Material in einer Farbe (für Platzhalter-Primitive)
        public static Material RuntimeMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var m = new Material(shader) { name = "SuperFx (Runtime)", color = color };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (color.a < 0.999f && m.HasProperty("_Surface"))
            {
                // URP Unlit transparent
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            return m;
        }

        // Primitive ohne Collider/Schatten
        public static GameObject CreatePrimitive(PrimitiveType type, string name, Color color, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var rend = go.GetComponent<Renderer>();
            rend.sharedMaterial = RuntimeMaterial(color);
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        // ---------------- Platzhalter-Buddy (Config ohne Prefab) ----------------

        // Baut einen spielbaren Super-Buddy ohne Art: Root (Layer/Tag "Buddy", BoxCollider) + Kind "Visual" (Primitive in Element-Farbe)
        public static SuperBuddy CreateRuntime(FusionElement element, Vector3 position)
        {
            var go = new GameObject("Super_" + element + " (Platzhalter)");
            go.SetActive(false); // Komponenten erst konfigurieren, dann aktivieren (OnEnable/Start)
            go.transform.position = position;
            int layer = LayerMask.NameToLayer("Buddy");
            if (layer >= 0) go.layer = layer;
            try { go.tag = "Buddy"; } catch (UnityException) { }

            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(1.4f, 2.4f, 1.4f);
            box.center = new Vector3(0f, 1.2f, 0f);

            var visual = new GameObject("Visual").transform;
            visual.SetParent(go.transform, false);
            Color c = FusionInfo.GetColor(element);
            switch (element)
            {
                case FusionElement.Phoenix:
                    var body = CreatePrimitive(PrimitiveType.Sphere, "Body", c, visual);
                    body.transform.localPosition = new Vector3(0f, 2.2f, 0f);
                    body.transform.localScale = new Vector3(0.7f, 0.6f, 1.1f);
                    var wings = CreatePrimitive(PrimitiveType.Cube, "Wings", new Color(1f, 0.85f, 0.2f), visual);
                    wings.transform.localPosition = new Vector3(0f, 2.3f, 0f);
                    wings.transform.localScale = new Vector3(2.2f, 0.08f, 0.6f);
                    var perch = CreatePrimitive(PrimitiveType.Cylinder, "PerchPost", new Color(0.35f, 0.25f, 0.15f), go.transform);
                    perch.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                    perch.transform.localScale = new Vector3(0.25f, 0.9f, 0.25f);
                    break;
                case FusionElement.WorldTree:
                    var trunk = CreatePrimitive(PrimitiveType.Cylinder, "Trunk", new Color(0.4f, 0.27f, 0.15f), visual);
                    trunk.transform.localPosition = new Vector3(0f, 1.2f, 0f);
                    trunk.transform.localScale = new Vector3(0.8f, 1.2f, 0.8f);
                    var crown = CreatePrimitive(PrimitiveType.Sphere, "Crown", c, visual);
                    crown.transform.localPosition = new Vector3(0f, 3f, 0f);
                    crown.transform.localScale = new Vector3(2.6f, 2f, 2.6f);
                    break;
                case FusionElement.StormLord:
                    var core = CreatePrimitive(PrimitiveType.Capsule, "Body", c, visual);
                    core.transform.localPosition = new Vector3(0f, 1.8f, 0f);
                    core.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);
                    break;
                default:
                    var titan = CreatePrimitive(PrimitiveType.Capsule, "Body", c, visual);
                    titan.transform.localPosition = new Vector3(0f, 1.3f, 0f);
                    titan.transform.localScale = new Vector3(1.4f, 1.3f, 1.4f);
                    break;
            }

            SuperBuddy buddy;
            switch (element)
            {
                case FusionElement.StormLord: buddy = go.AddComponent<StormLordBuddy>(); break;
                case FusionElement.Phoenix: buddy = go.AddComponent<PhoenixBuddy>(); break;
                case FusionElement.WorldTree: buddy = go.AddComponent<WorldTreeBuddy>(); break;
                default: buddy = go.AddComponent<VolcanoTitanBuddy>(); break;
            }
            buddy.Element = element;
            buddy.ApplyClassDefaults();
            buddy.Visual = visual;
            var fp = new GameObject("FirePoint").transform;
            fp.SetParent(go.transform, false);
            fp.localPosition = new Vector3(0f, 2f, 0f);
            buddy.FirePoint = fp;
            return buddy;
        }

        // Klassen-Standards (Reset() läuft nur im Editor)
        protected virtual void ApplyClassDefaults() { }

        protected override void Reset()
        {
            base.Reset();
            ApplyClassDefaults();
        }
    }
}
