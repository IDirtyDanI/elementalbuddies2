using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace ElementalBuddies
{
    // Kampfplatz des Dimensionsrisses. Wird zur Laufzeit auf JEDEM Rechner lokal bei DimensionEventRules.ArenaCenter
    // (400, 0, 0) angelegt – kein Netz-Objekt, keine Szenen-Änderung – und bekommt ein eigenes NavMesh (NavMeshSurface,
    // nur Kinder, Collider als Geometrie): Der Boss läuft darauf, und die Figur des Besitzers braucht es für Zielen,
    // Blinken und das Sturz-Sicherheitsnetz.
    //
    // ── Vertrag für das Arena-Prefab (MapDefinitionSO.DimensionArenaPrefab) ──────────────────────────────────────────
    //  • Wurzel im Ursprung, Boden bei y = 0; das Prefab wird unverändert an die Arena-Mitte gesetzt (keine Skalierung).
    //  • Kind-Transforms (Name exakt, beliebig tief):
    //      „Entry“        – hier erscheinen die Spieler; +Z zeigt in die Arena (mehrere Spieler stehen quer dazu, 1,6 m Abstand)
    //      „BossSpawn“    – hier erscheint der Boss und hierhin kehrt er zurück, wenn niemand drin ist (Arena-Mitte)
    //      „ReturnPortal“ – Standort des Rückportals (Gruft-Tor wird zur Laufzeit hingestellt), +Z = Öffnung zur Arena;
    //                       der Auslösekreis (r 2,5 m) liegt 2 m davor. Mindestens 5 m Abstand zu „Entry“.
    //      optional „Lights“, „VFX“ – beliebiger Inhalt, bleibt wie er ist.
    //  • Boden: Collider auf Layer „Floor“ (8) mit Tag „Floor“ (Zielen, Blinken, Warnflächen) – nur dort, wo man stehen darf.
    //  • Begrenzung: geschlossener Ring aus Collidern (≥ 3 m hoch, ≥ 1 m dick, lückenlos bis zum Bodenrand) auf Layer
    //    „Default“ – hält Figuren auf und schneidet das NavMesh. Außerhalb des Rings darf KEIN Boden-Collider liegen.
    //  • Alles statisch, keine Rigidbodies/NetworkObjects. Collider, aus denen das NavMesh entsteht, müssen Grundkörper
    //    (Box/Sphere/Capsule) sein oder MeshCollider mit lesbarem Mesh (FBX-Import „Read/Write“ an) – sonst fehlt das
    //    NavMesh im Build. Eigene NavMeshSurface im Prefab ist erlaubt (wird dann benutzt statt der automatischen).
    //  • Begehbarer Radius ≈ 16 m um „BossSpawn“ (Leine des Bosses 40 m, Minimap-Ausschnitt 22 m).
    // Ohne Prefab entsteht eine Platzhalter-Arena aus Grundkörpern (Scheibe r 18 m, Mauerring innen 16,5 m, Pfosten).
    public class DimensionArena : MonoBehaviour
    {
        public const string EntryName = "Entry";
        public const string BossSpawnName = "BossSpawn";
        public const string ReturnPortalName = "ReturnPortal";
        public const int FloorLayer = 8;
        public const float ReturnTriggerForward = 2f;

        public Transform Anchor { get; private set; }        // Arena-Mitte = Leine des Bosses
        public Transform Entry { get; private set; }
        public Transform BossSpawn { get; private set; }
        public Transform ReturnPortal { get; private set; }
        public bool IsPlaceholder { get; private set; }
        public bool ReturnPortalOpen { get; private set; }
        public bool HasNavMesh { get; private set; }

        // Mittelpunkt des Auslösekreises vor dem Rückportal
        public Vector3 ReturnTriggerPosition =>
            ReturnPortal != null ? ReturnPortal.position + ReturnPortal.forward * ReturnTriggerForward : transform.position;

        // Markierung vor dem Rückportal (Halte-Fortschritt der eigenen Figur)
        public RiftMarkerPulse ReturnMarker => _returnMarker != null ? _returnMarker.GetComponent<RiftMarkerPulse>() : null;
        // Tests: Collider des Rückportal-Tors (Pfeiler)
        public int GateColliderCount { get; private set; }

        private NavMeshSurface _surface;
        private bool _ownSurface;
        private GameObject _returnActive, _returnMarker;
        private readonly List<Object> _owned = new List<Object>(); // Laufzeit-Meshes/-Materialien

        // ---------------- Anlegen ----------------

        public static DimensionArena Create(MapDefinitionSO map, Scene scene)
        {
            var root = new GameObject("DimensionArena");
            if (scene.IsValid()) SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.SetPositionAndRotation(DimensionEventRules.ArenaCenter, Quaternion.identity);
            var arena = root.AddComponent<DimensionArena>();
            arena.Build(map);
            return arena;
        }

        private void Build(MapDefinitionSO map)
        {
            Color color = map != null ? map.DimensionColor : WaveEvents.Color(WaveEvent.Dimension);
            var source = FindScenePortal();
            Material glowTemplate = PortalSurfaceMaterial(source);

            if (map != null && map.DimensionArenaPrefab != null)
            {
                var content = Instantiate(map.DimensionArenaPrefab, transform);
                content.name = map.DimensionArenaPrefab.name;
                content.transform.localPosition = Vector3.zero;
                content.transform.localRotation = Quaternion.identity;
            }
            else
            {
                IsPlaceholder = true;
                BuildPlaceholder(color, glowTemplate);
            }

            Anchor = NewAnchor("Anchor", Vector3.zero, Quaternion.identity);
            BossSpawn = FindDeep(transform, BossSpawnName);
            if (BossSpawn == null) BossSpawn = NewAnchor(BossSpawnName, Vector3.zero, Quaternion.identity, !IsPlaceholder);
            Entry = FindDeep(transform, EntryName);
            if (Entry == null) Entry = NewAnchor(EntryName, new Vector3(0f, 0f, -9.5f), Quaternion.identity, !IsPlaceholder);
            ReturnPortal = FindDeep(transform, ReturnPortalName);
            if (ReturnPortal == null) ReturnPortal = NewAnchor(ReturnPortalName, new Vector3(0f, 0f, -15.2f), Quaternion.identity, !IsPlaceholder);
            Anchor.position = new Vector3(BossSpawn.position.x, transform.position.y, BossSpawn.position.z);

            BuildReturnPortal(source, color, glowTemplate);
            if (!IsPlaceholder) CheckContract();
            BuildNavMesh();
        }

        private Transform NewAnchor(string anchorName, Vector3 localPos, Quaternion localRot, bool warn = false)
        {
            if (warn) Debug.LogWarning($"DimensionArena: Kind „{anchorName}“ fehlt im Arena-Prefab – Standardlage {localPos} wird benutzt.");
            var t = new GameObject(anchorName).transform;
            t.SetParent(transform, false);
            t.localPosition = localPos;
            t.localRotation = localRot;
            return t;
        }

        private static Transform FindDeep(Transform root, string childName)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t != root && t.name == childName) return t;
            return null;
        }

        private void CheckContract()
        {
            bool floor = false;
            foreach (var c in GetComponentsInChildren<Collider>(true))
                if (c.gameObject.layer == FloorLayer) { floor = true; break; }
            if (!floor) Debug.LogError("DimensionArena: Das Arena-Prefab hat keinen Collider auf Layer „Floor“ (8) – Zielen, Blinken und Warnflächen brauchen ihn.");
        }

        // NavMesh nur aus den eigenen Kindern (Physik-Collider), Agent „Humanoid“ (Id 0) – reine Laufzeit-API (auch IL2CPP)
        private void BuildNavMesh()
        {
            Physics.SyncTransforms();
            _surface = GetComponentInChildren<NavMeshSurface>(true);
            if (_surface == null)
            {
                _surface = gameObject.AddComponent<NavMeshSurface>();
                _ownSurface = true;
                _surface.collectObjects = CollectObjects.Children;
                _surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
                _surface.agentTypeID = NavMesh.GetSettingsByIndex(0).agentTypeID;
                _surface.layerMask = ~0;
                _surface.defaultArea = 0;
            }
            _surface.BuildNavMesh();
            Vector3 probe = BossSpawn != null ? BossSpawn.position : transform.position;
            HasNavMesh = NavMesh.SamplePosition(probe, out _, 3f, NavMesh.AllAreas);
            if (!HasNavMesh) Debug.LogError("DimensionArena: Kein NavMesh am Boss-Platz – Collider der Arena prüfen (Grundkörper oder lesbare Meshes).");
        }

        void OnDestroy()
        {
            if (_surface != null)
            {
                var data = _surface.navMeshData;
                _surface.RemoveData();
                if (_ownSurface) _surface.navMeshData = null;
                if (data != null && _ownSurface) Destroy(data);
            }
            foreach (var o in _owned) if (o != null) Destroy(o);
            _owned.Clear();
        }

        // ---------------- Rückportal ----------------

        public void SetReturnPortalOpen(bool open)
        {
            ReturnPortalOpen = open;
            if (_returnActive != null) _returnActive.SetActive(open);
            if (_returnMarker != null) _returnMarker.SetActive(open);
        }

        private static SpawnPortal FindScenePortal()
        {
            SpawnPortal any = null;
            foreach (var p in SpawnPortal.All)
            {
                if (p == null) continue;
                if (p.IsOpen) return p;
                if (any == null) any = p;
            }
            return any;
        }

        private static Material PortalSurfaceMaterial(SpawnPortal portal)
        {
            if (portal == null || portal.ActiveVisuals == null) return null;
            foreach (var r in portal.ActiveVisuals.GetComponentsInChildren<MeshRenderer>(true))
                if (r.sharedMaterial != null) return r.sharedMaterial;
            return null;
        }

        // Das Gruft-Tor der Karte wird kopiert (ohne SpawnPortal-Logik) – so braucht es kein eigenes Prefab, und jede Map
        // bekommt ihr eigenes Tor. Die Kopie entsteht unter einem inaktiven Elternobjekt, damit das SpawnPortal der Kopie
        // sich nie in die Portal-Liste einträgt. Die Grundkörper-Collider des Tors (Pfeiler) bleiben: Sie halten Figuren
        // und Boss aus dem Modell heraus und schneiden das Laufzeit-NavMesh (das Tor steht vor BuildNavMesh; #576).
        private void BuildReturnPortal(SpawnPortal source, Color color, Material glowTemplate)
        {
            if (source != null)
            {
                var holder = new GameObject("ReturnPortalVisual");
                holder.SetActive(false);
                holder.transform.SetParent(ReturnPortal, false);
                var copy = Instantiate(source.gameObject, holder.transform);
                copy.name = "Gate";
                copy.transform.localPosition = Vector3.zero;
                copy.transform.localRotation = Quaternion.identity;
                copy.transform.localScale = source.transform.localScale;
                var sp = copy.GetComponent<SpawnPortal>();
                if (sp != null)
                {
                    _returnActive = sp.ActiveVisuals;
                    if (sp.InactiveVisuals != null) sp.InactiveVisuals.SetActive(false);
                    DestroyImmediate(sp);
                }
                foreach (var o in copy.GetComponentsInChildren<NavMeshObstacle>(true)) DestroyImmediate(o);
                foreach (var m in copy.GetComponentsInChildren<NavMeshModifier>(true)) DestroyImmediate(m);
                GateColliderCount = 0;
                foreach (var c in copy.GetComponentsInChildren<Collider>(true))
                {
                    // nur feste Grundkörper (sicher für das Laufzeit-NavMesh im Build); Auslöser und Mesh-Collider entfallen
                    bool keep = !c.isTrigger && (c is BoxCollider || c is CapsuleCollider || c is SphereCollider);
                    if (keep) { c.gameObject.layer = 0; GateColliderCount++; }
                    else DestroyImmediate(c);
                }
                if (GateColliderCount == 0) GateColliderCount = AddGatePillars(copy, sp);
                // Durchgang zwischen den Pfeilern schließen: Im Tor steht niemand (der Auslösekreis liegt 2 m davor)
                if (AddGateFill(copy)) GateColliderCount++;
                // Tor im NavMesh als „nicht begehbar“ (keine Inseln auf den Pfeilern)
                var mod = copy.AddComponent<NavMeshModifier>();
                mod.overrideArea = true;
                mod.area = 1;
                mod.applyToChildren = true;
                foreach (var a in copy.GetComponentsInChildren<AudioSource>(true)) DestroyImmediate(a);
                foreach (var p in copy.GetComponentsInChildren<RiftMarkerPulse>(true)) DestroyImmediate(p.gameObject);
                if (_returnActive != null) _returnActive.SetActive(false);
                holder.SetActive(true);
            }
            _returnMarker = RiftVisuals.CreateMarker(ReturnPortal, ReturnTriggerPosition, WaveEvents.Color(WaveEvent.Dimension), glowTemplate, _owned, RiftVisuals.AdditiveTemplate(source));
            SetReturnPortalOpen(false);
        }

        // Ein Quader über die ganze Breite der Pfeiler-Quader (Tor-Raum) – schließt die Öffnung
        private static bool AddGateFill(GameObject gate)
        {
            bool has = false;
            Bounds b = new Bounds();
            foreach (var box in gate.GetComponentsInChildren<BoxCollider>(true))
            {
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = box.center + Vector3.Scale(box.size * 0.5f, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 local = gate.transform.InverseTransformPoint(box.transform.TransformPoint(corner));
                    if (!has) { b = new Bounds(local, Vector3.zero); has = true; }
                    else b.Encapsulate(local);
                }
            }
            if (!has) return false;
            var fill = gate.AddComponent<BoxCollider>();
            fill.center = b.center;
            fill.size = b.size;
            return true;
        }

        // Tor ohne eigene Grundkörper-Collider: zwei Pfeiler-Quader aus den Maßen der Tor-Optik (links/rechts der Öffnung)
        private static int AddGatePillars(GameObject gate, SpawnPortal sp)
        {
            bool has = false;
            Bounds b = new Bounds();
            Transform t = gate.transform;
            foreach (var r in gate.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (sp != null && sp.ActiveVisuals != null && r.transform.IsChildOf(sp.ActiveVisuals.transform)) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                // Mesh-Grenzen in den Raum des Tors
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 local = t.InverseTransformPoint(r.transform.TransformPoint(corner));
                    if (!has) { b = new Bounds(local, Vector3.zero); has = true; }
                    else b.Encapsulate(local);
                }
            }
            if (!has || b.size.x < 0.5f) return 0;
            float pillar = Mathf.Clamp(b.size.x * 0.28f, 0.4f, 1.6f);
            float depth = Mathf.Clamp(b.size.z, 0.5f, 1.4f);
            float height = Mathf.Max(2f, b.size.y);
            for (int side = -1; side <= 1; side += 2)
            {
                var box = gate.AddComponent<BoxCollider>();
                box.center = new Vector3(b.center.x + side * (b.extents.x - pillar * 0.5f), b.min.y + height * 0.5f, b.center.z);
                box.size = new Vector3(pillar, height, depth);
            }
            return 2;
        }

        // ---------------- Platzhalter-Arena ----------------

        private void BuildPlaceholder(Color color, Material glowTemplate)
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null && glowTemplate != null) lit = glowTemplate.shader;
            if (lit == null) lit = Shader.Find("Universal Render Pipeline/Simple Lit");
            Material floorMat = NewMaterial(lit, new Color(0.34f, 0.33f, 0.38f), Color.black);
            Material wallMat = NewMaterial(lit, new Color(0.46f, 0.45f, 0.5f), Color.black);
            Material glowMat = RiftVisuals.GlowMaterial(glowTemplate, lit, color, 1.1f, _owned);

            // Boden: geschlossene Scheibe (Oberseite y = 0), Layer/Tag Floor
            var floor = new GameObject("Floor");
            floor.transform.SetParent(transform, false);
            floor.layer = FloorLayer;
            floor.tag = "Floor";
            var disc = RiftVisuals.Cylinder(DimensionEventRules.FloorRadius, 0f, -1f, 64);
            _owned.Add(disc);
            floor.AddComponent<MeshFilter>().sharedMesh = disc;
            floor.AddComponent<MeshRenderer>().sharedMaterial = floorMat;
            floor.AddComponent<MeshCollider>().sharedMesh = disc;

            // Zierring um die Mitte (nur Optik)
            var rune = RiftVisuals.MeshObject("RuneRing", transform, RiftVisuals.Annulus(6.8f, 7f, 64), glowMat, _owned);
            rune.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            var edge = RiftVisuals.MeshObject("EdgeRing", transform, RiftVisuals.Annulus(DimensionEventRules.WalkRadius - 0.25f, DimensionEventRules.WalkRadius - 0.1f, 64), glowMat, _owned);
            edge.transform.localPosition = new Vector3(0f, 0.02f, 0f);

            // Begrenzung: geschlossener Ring aus Quadern, Innenkante bei WalkRadius, 2 m dick → reicht über den Bodenrand hinaus
            const int segments = 24;
            const float thick = 2f, height = 4f;
            float mid = DimensionEventRules.WalkRadius + thick * 0.5f;
            float len = 2f * mid * Mathf.Tan(Mathf.PI / segments) + 0.6f;
            var walls = new GameObject("Walls").transform;
            walls.SetParent(transform, false);
            for (int i = 0; i < segments; i++)
            {
                float a = (i + 0.5f) / segments * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var seg = new GameObject("Wall_" + i);
                seg.transform.SetParent(walls, false);
                seg.transform.localPosition = dir * mid;
                seg.transform.localRotation = Quaternion.LookRotation(dir);
                var box = seg.AddComponent<BoxCollider>();
                box.size = new Vector3(len, height, thick);
                box.center = new Vector3(0f, height * 0.5f, 0f);
                var vis = GameObject.CreatePrimitive(PrimitiveType.Cube);
                DestroyImmediate(vis.GetComponent<Collider>());
                vis.name = "Visual";
                vis.transform.SetParent(seg.transform, false);
                vis.transform.localPosition = new Vector3(0f, 0.6f, -thick * 0.5f + 0.4f);
                vis.transform.localScale = new Vector3(len, 1.2f, 0.8f);
                vis.GetComponent<MeshRenderer>().sharedMaterial = wallMat;
            }

            // Pfosten mit leuchtender Kuppe (nur Optik)
            var posts = new GameObject("Posts").transform;
            posts.SetParent(transform, false);
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f + Mathf.PI / 8f;
                Vector3 p = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * (DimensionEventRules.WalkRadius + 0.5f);
                var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                DestroyImmediate(post.GetComponent<Collider>());
                post.name = "Post_" + i;
                post.transform.SetParent(posts, false);
                post.transform.localPosition = p + Vector3.up * 1.7f;
                post.transform.localScale = new Vector3(0.7f, 1.7f, 0.7f);
                post.GetComponent<MeshRenderer>().sharedMaterial = wallMat;
                var cap = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                DestroyImmediate(cap.GetComponent<Collider>());
                cap.name = "Cap";
                cap.transform.SetParent(posts, false);
                cap.transform.localPosition = p + Vector3.up * 3.6f;
                cap.transform.localScale = Vector3.one * 0.8f;
                cap.GetComponent<MeshRenderer>().sharedMaterial = glowMat;
            }
        }

        private Material NewMaterial(Shader shader, Color baseColor, Color emission)
        {
            var m = new Material(shader) { name = "DimensionArena (Laufzeit)", hideFlags = HideFlags.DontSave };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", baseColor);
            if (m.HasProperty("_Color")) m.SetColor("_Color", baseColor);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
            if (emission.maxColorComponent > 0f && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission);
            }
            _owned.Add(m);
            return m;
        }
    }

    // Leuchtende Markierung eines begehbaren Portals: Bodenring im Auslöse-Radius und Lichtsäule (pulsierend). Reine
    // Optik aus Laufzeit-Meshes; das Material ist eine Kopie der Portal-Fläche (Shader ist damit sicher im Build).
    public static class RiftVisuals
    {
        public static GameObject CreateMarker(Transform parent, Vector3 worldPos, Color color, Material glowTemplate, List<Object> owned,
            Material additiveTemplate = null)
        {
            var root = new GameObject("RiftMarker");
            root.transform.SetParent(parent, false);
            root.transform.position = worldPos;
            root.transform.rotation = Quaternion.identity;
            // Markierung nie verzerrt (Portale können skaliert sein)
            Vector3 ls = parent != null ? parent.lossyScale : Vector3.one;
            root.transform.localScale = new Vector3(1f / Mathf.Max(0.01f, ls.x), 1f / Mathf.Max(0.01f, ls.y), 1f / Mathf.Max(0.01f, ls.z));
            Shader lit = glowTemplate != null ? glowTemplate.shader : Shader.Find("Universal Render Pipeline/Lit");
            Material mat = GlowMaterial(glowTemplate, lit, color, 1.5f, owned);

            float r = DimensionEventRules.EnterRadius;
            var ring = MeshObject("Ring", root.transform, Annulus(r - 0.2f, r, 48), mat, owned);
            ring.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            var inner = MeshObject("InnerRing", root.transform, Annulus(r * 0.45f, r * 0.45f + 0.08f, 40), mat, owned);
            inner.transform.localPosition = new Vector3(0f, 0.06f, 0f);

            // Lichtsäule: weicher additiver Strahl (zwei Mäntel, nach oben ausblendend) – ohne additives Material der alte Stab
            GameObject beam;
            if (additiveTemplate != null)
            {
                Material add = AdditiveMaterial(additiveTemplate, color, owned);
                beam = new GameObject("Beam");
                beam.transform.SetParent(root.transform, false);
                MeshObject("Core", beam.transform, SoftBeam(0.16f, 0.05f, 9f, 0.85f, 14), add, owned);
                MeshObject("Halo", beam.transform, SoftBeam(0.42f, 0.22f, 6.5f, 0.2f, 18), add, owned);
            }
            else beam = MeshObject("Beam", root.transform, Cylinder(0.09f, 8f, 0f, 10), mat, owned);
            beam.transform.localPosition = Vector3.zero;

            // Halte-Fortschritt (#569): helle Scheibe, die sich vom Mittelpunkt bis zum Ring füllt
            Color bright = Color.Lerp(color, Color.white, 0.55f);
            Material fillMat = additiveTemplate != null
                ? AdditiveMaterial(additiveTemplate, new Color(bright.r, bright.g, bright.b, 0.7f), owned)
                : GlowMaterial(glowTemplate, lit, bright, 2.2f, owned);
            var fill = MeshObject("HoldFill", root.transform, Disc(r - 0.2f, 40), fillMat, owned);
            fill.transform.localPosition = new Vector3(0f, 0.09f, 0f);
            fill.SetActive(false);
            var fillEdge = MeshObject("HoldEdge", fill.transform, Annulus(r - 0.42f, r - 0.2f, 48), GlowMaterial(glowTemplate, lit, bright, 2.6f, owned), owned);
            fillEdge.transform.localPosition = new Vector3(0f, 0.01f, 0f);

            var pulse = root.AddComponent<RiftMarkerPulse>();
            pulse.Ring = ring.transform;
            pulse.Inner = inner.transform;
            pulse.Beam = beam.transform;
            pulse.Fill = fill.transform;
            return root;
        }

        // Additives Partikel-Material eines Portals (Wirbel) als Vorlage für Strahl/Füllung – Shader ist damit im Build
        public static Material AdditiveTemplate(SpawnPortal portal)
        {
            if (portal == null || portal.ActiveVisuals == null) return null;
            Material any = null;
            foreach (var r in portal.ActiveVisuals.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                var m = r.sharedMaterial;
                if (m == null) continue;
                if (m.name.Contains("SoftDot")) return m;
                if (any == null) any = m;
            }
            return any;
        }

        // Kopie des additiven Materials in der Farbe; beidseitig, ohne Tiefenschreiben
        public static Material AdditiveMaterial(Material template, Color color, List<Object> owned)
        {
            var m = new Material(template) { name = "Rift-Strahl (Laufzeit)", hideFlags = HideFlags.DontSave };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", color);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            if (owned != null) owned.Add(m);
            return m;
        }

        // Mantel eines Kegelstumpfs um die Y-Achse (ohne Deckel) mit Vertex-Alpha: unten alpha, oben 0. Alle UVs liegen
        // in der Texturmitte (weicher Punkt → volle Deckkraft), die Form macht allein das Vertex-Alpha.
        public static Mesh SoftBeam(float radiusBottom, float radiusTop, float height, float alpha, int segments)
        {
            segments = Mathf.Max(6, segments);
            const int rows = 4;
            var v = new List<Vector3>();
            var c = new List<Color>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            for (int row = 0; row <= rows; row++)
            {
                float k = row / (float)rows;
                float rad = Mathf.Lerp(radiusBottom, radiusTop, k);
                // unten kurz einblenden (kein harter Rand am Boden), dann weich nach oben aus
                float a = alpha * (row == 0 ? 0.55f : (1f - k) * (1f - k * 0.35f));
                for (int i = 0; i <= segments; i++)
                {
                    float ang = i / (float)segments * Mathf.PI * 2f;
                    v.Add(new Vector3(Mathf.Sin(ang) * rad, k * height, Mathf.Cos(ang) * rad));
                    c.Add(new Color(1f, 1f, 1f, a));
                    uv.Add(new Vector2(0.5f, 0.5f));
                }
            }
            int stride = segments + 1;
            for (int row = 0; row < rows; row++)
                for (int i = 0; i < segments; i++)
                {
                    int a0 = row * stride + i, a1 = a0 + 1, b0 = a0 + stride, b1 = b0 + 1;
                    t.Add(a0); t.Add(b0); t.Add(b1);
                    t.Add(a0); t.Add(b1); t.Add(a1);
                }
            var m = new Mesh { name = "Rift-Strahl", hideFlags = HideFlags.DontSave };
            m.SetVertices(v);
            m.SetColors(c);
            m.SetUVs(0, uv);
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // Volle Scheibe in der XZ-Ebene (Normale +Y), UVs in der Texturmitte, Vertex-Farbe weiß
        public static Mesh Disc(float radius, int segments)
        {
            segments = Mathf.Max(8, segments);
            var v = new Vector3[segments + 1];
            var n = new Vector3[segments + 1];
            var uv = new Vector2[segments + 1];
            var c = new Color[segments + 1];
            var t = new int[segments * 3];
            v[0] = Vector3.zero;
            for (int i = 0; i <= segments; i++) { n[i] = Vector3.up; uv[i] = new Vector2(0.5f, 0.5f); c[i] = Color.white; }
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                v[i + 1] = new Vector3(Mathf.Sin(a) * radius, 0f, Mathf.Cos(a) * radius);
                t[i * 3] = 0; t[i * 3 + 1] = i + 1; t[i * 3 + 2] = (i + 1) % segments + 1;
            }
            var m = new Mesh { name = "Rift-Scheibe", hideFlags = HideFlags.DontSave };
            m.vertices = v;
            m.normals = n;
            m.uv = uv;
            m.colors = c;
            m.triangles = t;
            m.RecalculateBounds();
            return m;
        }

        // ---------------- Portal-Partikel umfärben ----------------

        // Gemerkter Zustand eines umgefärbten Partikelsystems (Restore stellt ihn wieder her)
        public class ParticleTint
        {
            public ParticleSystem System;
            public ParticleSystem.MinMaxGradient StartColor, OverLifetime;
            public bool HadOverLifetime;

            public void Restore()
            {
                if (System == null) return;
                var main = System.main;
                main.startColor = StartColor;
                if (HadOverLifetime)
                {
                    var col = System.colorOverLifetime;
                    col.color = OverLifetime;
                }
            }
        }

        // Farbton aller Farben des Systems durch den der Zielfarbe ersetzen (Sättigung mindestens die halbe der Zielfarbe,
        // weiße Kerne bleiben hell; Helligkeit und Alpha bleiben)
        public static ParticleTint TintParticles(ParticleSystem ps, Color target)
        {
            var tint = new ParticleTint { System = ps };
            UnifyVelocityCurves(ps);
            var main = ps.main;
            tint.StartColor = main.startColor;
            var col = ps.colorOverLifetime;
            tint.HadOverLifetime = col.enabled;
            tint.OverLifetime = col.color;
            Color.RGBToHSV(target, out float hue, out float sat, out _);
            main.startColor = Shift(tint.StartColor, hue, sat);
            if (col.enabled) col.color = Shift(tint.OverLifetime, hue, sat);
            return tint;
        }

        private static Color Shift(Color c, float hue, float targetSat)
        {
            Color.RGBToHSV(c, out _, out float s, out float v);
            // farbige Töne nehmen Farbton und (mindestens) Sättigung der Zielfarbe an; fast weiße bleiben fast weiß
            float ns = s < 0.12f ? s : Mathf.Max(s, targetSat * 0.8f);
            Color o = Color.HSVToRGB(hue, ns, v);
            o.a = c.a;
            return o;
        }

        private static Gradient Shift(Gradient g, float hue, float sat)
        {
            if (g == null) return null;
            var keys = g.colorKeys;
            for (int i = 0; i < keys.Length; i++) keys[i].color = Shift(keys[i].color, hue, sat);
            var o = new Gradient { mode = g.mode };
            o.SetKeys(keys, g.alphaKeys);
            return o;
        }

        private static ParticleSystem.MinMaxGradient Shift(ParticleSystem.MinMaxGradient g, float hue, float sat)
        {
            switch (g.mode)
            {
                case ParticleSystemGradientMode.Color:
                    return new ParticleSystem.MinMaxGradient(Shift(g.color, hue, sat));
                case ParticleSystemGradientMode.TwoColors:
                    return new ParticleSystem.MinMaxGradient(Shift(g.colorMin, hue, sat), Shift(g.colorMax, hue, sat));
                case ParticleSystemGradientMode.Gradient:
                    return new ParticleSystem.MinMaxGradient(Shift(g.gradient, hue, sat));
                case ParticleSystemGradientMode.TwoGradients:
                    return new ParticleSystem.MinMaxGradient(Shift(g.gradientMin, hue, sat), Shift(g.gradientMax, hue, sat));
                default:
                {
                    var o = new ParticleSystem.MinMaxGradient(Shift(g.gradient, hue, sat));
                    o.mode = g.mode;
                    return o;
                }
            }
        }

        // Die Engine verlangt, dass die Kurven einer Dreiergruppe (Orbital X/Y/Z, Versatz X/Y/Z, linear X/Y/Z) denselben
        // Modus haben, und warnt sonst bei jeder Skript-Änderung am System. Gemischte Gruppen eines AUSGESCHALTETEN Moduls
        // (so stehen sie in den Walddorf-Portalen) werden auf Konstanten gesetzt – ohne sichtbare Wirkung.
        public static void UnifyVelocityCurves(ParticleSystem ps)
        {
            var vel = ps.velocityOverLifetime;
            bool mixedOrbital = vel.orbitalX.mode != vel.orbitalY.mode || vel.orbitalY.mode != vel.orbitalZ.mode;
            bool mixedOffset = vel.orbitalOffsetX.mode != vel.orbitalOffsetY.mode || vel.orbitalOffsetY.mode != vel.orbitalOffsetZ.mode;
            bool mixedLinear = vel.x.mode != vel.y.mode || vel.y.mode != vel.z.mode;
            if (!mixedOrbital && !mixedOffset && !mixedLinear) return;
            if (mixedOrbital)
            {
                var m = Widest(vel.orbitalX, vel.orbitalY, vel.orbitalZ);
                vel.orbitalX = As(vel.orbitalX, m); vel.orbitalY = As(vel.orbitalY, m); vel.orbitalZ = As(vel.orbitalZ, m);
            }
            if (mixedOffset)
            {
                var m = Widest(vel.orbitalOffsetX, vel.orbitalOffsetY, vel.orbitalOffsetZ);
                vel.orbitalOffsetX = As(vel.orbitalOffsetX, m); vel.orbitalOffsetY = As(vel.orbitalOffsetY, m); vel.orbitalOffsetZ = As(vel.orbitalOffsetZ, m);
            }
            if (mixedLinear)
            {
                var m = Widest(vel.x, vel.y, vel.z);
                vel.x = As(vel.x, m); vel.y = As(vel.y, m); vel.z = As(vel.z, m);
            }
        }

        private static ParticleSystemCurveMode Widest(ParticleSystem.MinMaxCurve a, ParticleSystem.MinMaxCurve b, ParticleSystem.MinMaxCurve c)
        {
            // Konstante < zwei Konstanten; Kurven-Modi bleiben, wie sie sind (kommen in den Portalen nicht gemischt vor)
            if (a.mode == ParticleSystemCurveMode.TwoConstants || b.mode == ParticleSystemCurveMode.TwoConstants || c.mode == ParticleSystemCurveMode.TwoConstants)
                return ParticleSystemCurveMode.TwoConstants;
            return ParticleSystemCurveMode.Constant;
        }

        private static ParticleSystem.MinMaxCurve As(ParticleSystem.MinMaxCurve c, ParticleSystemCurveMode mode)
        {
            if (c.mode == mode) return c;
            if (c.mode == ParticleSystemCurveMode.Constant && mode == ParticleSystemCurveMode.TwoConstants)
                return new ParticleSystem.MinMaxCurve(c.constant, c.constant);
            if (c.mode == ParticleSystemCurveMode.TwoConstants && mode == ParticleSystemCurveMode.Constant)
                return new ParticleSystem.MinMaxCurve(c.constantMax);
            // Kurve → Konstante (Endwert × Faktor) – nur als Notnagel
            float k = c.mode == ParticleSystemCurveMode.Curve && c.curve != null ? c.curve.Evaluate(1f) * c.curveMultiplier : c.constantMax;
            return mode == ParticleSystemCurveMode.TwoConstants ? new ParticleSystem.MinMaxCurve(k, k) : new ParticleSystem.MinMaxCurve(k);
        }

        public static Material GlowMaterial(Material template, Shader fallbackShader, Color color, float intensity, List<Object> owned)
        {
            Material m = template != null ? new Material(template) : new Material(fallbackShader);
            m.name = "Rift-Leuchten (Laufzeit)";
            m.hideFlags = HideFlags.DontSave;
            Color baseColor = color * 0.6f;
            baseColor.a = 1f;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", baseColor);
            if (m.HasProperty("_Color")) m.SetColor("_Color", baseColor);
            if (m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", color * intensity);
            }
            if (owned != null) owned.Add(m);
            return m;
        }

        public static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material material, List<Object> owned)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (owned != null) owned.Add(mesh);
            return go;
        }

        // Flacher Ring in der XZ-Ebene (Normale +Y)
        public static Mesh Annulus(float inner, float outer, int segments)
        {
            segments = Mathf.Max(8, segments);
            var v = new Vector3[segments * 2];
            var n = new Vector3[segments * 2];
            var t = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                Vector3 d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                v[i * 2] = d * inner;
                v[i * 2 + 1] = d * outer;
                n[i * 2] = n[i * 2 + 1] = Vector3.up;
                int j = (i + 1) % segments;
                int k = i * 6;
                t[k] = i * 2; t[k + 1] = i * 2 + 1; t[k + 2] = j * 2 + 1;
                t[k + 3] = i * 2; t[k + 4] = j * 2 + 1; t[k + 5] = j * 2;
            }
            var m = new Mesh { name = "Rift-Ring", hideFlags = HideFlags.DontSave };
            m.vertices = v;
            m.normals = n;
            m.triangles = t;
            m.RecalculateBounds();
            return m;
        }

        // Geschlossener Zylinder um die Y-Achse (Deckel bei yTop, Boden bei yBottom)
        public static Mesh Cylinder(float radius, float yTop, float yBottom, int segments)
        {
            segments = Mathf.Max(6, segments);
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var t = new List<int>();
            // Deckel
            int topCenter = v.Count; v.Add(new Vector3(0f, yTop, 0f)); n.Add(Vector3.up);
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                v.Add(new Vector3(Mathf.Sin(a) * radius, yTop, Mathf.Cos(a) * radius)); n.Add(Vector3.up);
            }
            for (int i = 0; i < segments; i++) { t.Add(topCenter); t.Add(topCenter + 1 + i); t.Add(topCenter + 1 + (i + 1) % segments); }
            // Boden
            int botCenter = v.Count; v.Add(new Vector3(0f, yBottom, 0f)); n.Add(Vector3.down);
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                v.Add(new Vector3(Mathf.Sin(a) * radius, yBottom, Mathf.Cos(a) * radius)); n.Add(Vector3.down);
            }
            for (int i = 0; i < segments; i++) { t.Add(botCenter); t.Add(botCenter + 1 + (i + 1) % segments); t.Add(botCenter + 1 + i); }
            // Mantel
            int side = v.Count;
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                Vector3 d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                v.Add(new Vector3(d.x * radius, yTop, d.z * radius)); n.Add(d);
                v.Add(new Vector3(d.x * radius, yBottom, d.z * radius)); n.Add(d);
            }
            for (int i = 0; i < segments; i++)
            {
                int a0 = side + i * 2, a1 = a0 + 1, b0 = side + ((i + 1) % segments) * 2, b1 = b0 + 1;
                t.Add(a0); t.Add(a1); t.Add(b1);
                t.Add(a0); t.Add(b1); t.Add(b0);
            }
            var m = new Mesh { name = "Rift-Zylinder", hideFlags = HideFlags.DontSave };
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }
    }

    // Puls der Markierung (Echtzeit – läuft auch, wenn die Spielzeit steht)
    public class RiftMarkerPulse : MonoBehaviour
    {
        public Transform Ring, Inner, Beam, Fill;
        // Halte-Fortschritt der eigenen Figur im Kreis (0 = aus, 1 = voll) – setzt DimensionEvent
        [System.NonSerialized] public float Hold;

        void Update()
        {
            float t = Time.unscaledTime;
            float p = 0.5f + 0.5f * Mathf.Sin(t * 3.2f);
            if (Ring != null) Ring.localScale = Vector3.one * Mathf.Lerp(0.94f, 1.04f, p);
            if (Inner != null)
            {
                float k = Mathf.Repeat(t * 0.6f, 1f);
                Inner.localScale = Vector3.one * Mathf.Lerp(0.3f, 2.1f, k);
            }
            if (Beam != null)
            {
                float w = Mathf.Lerp(0.8f, 1.25f, p) * (1f + Hold * 0.8f);
                Beam.localScale = new Vector3(w, 1f, w);
            }
            if (Fill != null)
            {
                bool on = Hold > 0f;
                if (Fill.gameObject.activeSelf != on) Fill.gameObject.SetActive(on);
                if (on) Fill.localScale = Vector3.one * Mathf.Max(0.02f, Hold);
            }
        }
    }
}
