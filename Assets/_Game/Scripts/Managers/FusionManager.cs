using System;
using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Buddy-Fusion: zwei Buddies unterschiedlicher Basis-Elemente (beide zwischen MinLevel und MaxFusionLevel, höchstens FusionRange auseinander)
    // verschmelzen in der Bauphase gegen Seelensplitter zu einem Fusions-Buddy in der Mitte. Rezepte sind reine Daten (Recipes).
    // Liegt auf dem Managers-Objekt.
    public class FusionManager : MonoBehaviour
    {
        [Serializable]
        public class FusionRecipe
        {
            public int ElementA;              // Basis-Element-Index 0 Feuer, 1 Eis, 2 Erde, 3 Licht
            public int ElementB;
            public FusionElement Result;
            public UnitConfigSO ResultConfig; // Prefab + Werte des Fusions-Buddys; Kosten = CostOutCombat
            [TextArea] public string Description;
            public Sprite Icon;

            public bool Matches(int a, int b) => (ElementA == a && ElementB == b) || (ElementA == b && ElementB == a);
        }

        public struct FusionOption
        {
            public ElementalBuddy Partner;
            public FusionRecipe Recipe;
            public float Cost;
            public float Distance;
            public bool Affordable;
        }

        public static FusionManager Instance { get; private set; }

        public List<FusionRecipe> Recipes = new List<FusionRecipe>
        {
            new FusionRecipe { ElementA = 0, ElementB = 3, Result = FusionElement.Lightning,
                Description = "Kettenblitz: springt auf bis zu 3 weitere Gegner (Schaden nimmt pro Sprung ab). Nasse Gegner nehmen doppelten Schaden und verlängern die Kette." },
            new FusionRecipe { ElementA = 1, ElementB = 3, Result = FusionElement.Water,
                Description = "Wasserstrahl über die volle Reichweite: trifft alle Gegner im Strahl, macht sie nass und verlangsamt sie. Jeder 4. Strahl erzeugt einen Strudel, der Gegner festhält." },
            new FusionRecipe { ElementA = 0, ElementB = 1, Result = FusionElement.Air,
                Description = "Rückenwind: Buddies in Reichweite feuern 25 % schneller. Böe stößt Gegner den Weg zurück und trägt Brände auf Gegner in der Nähe weiter." },
            new FusionRecipe { ElementA = 2, ElementB = 3, Result = FusionElement.Shadow,
                Description = "Verflucht bis zu 2 Gegner: Schaden über Zeit, und verfluchte Gegner nehmen 25 % mehr Schaden aus allen Quellen." },
            new FusionRecipe { ElementA = 0, ElementB = 2, Result = FusionElement.Magma,
                Description = "Lavakugel auf die dichteste Gegnergruppe: Flächenschaden und Brand beim Einschlag. Hinterlässt eine Lavapfütze, die 4 s lang Schaden macht und leicht verlangsamt." },
            new FusionRecipe { ElementA = 1, ElementB = 2, Result = FusionElement.Crystal,
                Description = "Super-Tank: viel Leben, 35 % weniger Schaden. Frost-Aura verlangsamt Gegner in Reichweite, verspottet sie regelmäßig. Erlittener Schaden lädt eine Splitter-Nova, die ihn an alle Gegner zurückgibt." },
        };

        [Header("Regeln")]
        public float FusionRange = 5f;
        public int MinLevel = 2;
        [Tooltip("Höchste Stufe, auf der noch verschmolzen werden kann (Stufe 3 = ausentwickelt, keine Fusion mehr).")]
        public int MaxFusionLevel = 2;

        [Header("Optik")]
        public GameObject FusionBurstPrefab; // optional, am neuen Buddy
        public Material LinkMaterial;        // optional; leer = Laufzeit-Material
        public Color LinkColor = new Color(0.85f, 0.7f, 1f, 0.9f);
        public float LinkArcHeight = 1.5f;

        public event Action<FusionBuddy> OnFused;

        public bool CanFuseNow => ElementalBuddy.IsUpgradePhase;

        private readonly List<FusionOption> _options = new List<FusionOption>();

        // Link-Hervorhebung (Laufzeit-Objekte)
        private LineRenderer _linkArc;
        private LineRenderer _linkRing;
        private ElementalBuddy _linkA, _linkB;
        private const int ArcPoints = 24;
        private const int RingPoints = 40;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ---------------- Abfragen ----------------

        public FusionRecipe FindRecipe(int a, int b)
        {
            if (a == b || Recipes == null) return null;
            foreach (var r in Recipes)
                if (r != null && r.Matches(a, b)) return r;
            return null;
        }

        public FusionRecipe FindRecipe(FusionElement result)
        {
            if (Recipes == null) return null;
            foreach (var r in Recipes)
                if (r != null && r.Result == result) return r;
            return null;
        }

        public float GetCost(FusionRecipe recipe) => recipe != null && recipe.ResultConfig != null ? Mathf.Ceil(recipe.ResultConfig.CostOutCombat) : 0f;

        private bool IsFusable(ElementalBuddy b) =>
            b != null && b.isActiveAndEnabled && !b.IsFusion && b.Config != null && b.Level >= MinLevel && b.Level <= MaxFusionLevel;

        private static float HorizontalDistance(ElementalBuddy a, ElementalBuddy b)
        {
            Vector3 d = a.transform.position - b.transform.position;
            d.y = 0f;
            return d.magnitude;
        }

        // Mögliche Partner (unabhängig von Bezahlbarkeit), nach Distanz sortiert. Liste wird wiederverwendet.
        public List<FusionOption> GetOptions(ElementalBuddy buddy)
        {
            _options.Clear();
            if (!IsFusable(buddy)) return _options;

            var eco = EconomyManager.Instance;
            foreach (var other in ElementalBuddy.Active)
            {
                if (other == buddy || !IsFusable(other) || other.ElementIndex == buddy.ElementIndex) continue;
                float dist = HorizontalDistance(buddy, other);
                if (dist > FusionRange) continue;
                var recipe = FindRecipe(buddy.ElementIndex, other.ElementIndex);
                if (recipe == null || recipe.ResultConfig == null || recipe.ResultConfig.Prefab == null) continue;

                float cost = GetCost(recipe);
                _options.Add(new FusionOption
                {
                    Partner = other,
                    Recipe = recipe,
                    Cost = cost,
                    Distance = dist,
                    Affordable = eco == null || eco.CanAfford(cost),
                });
            }
            _options.Sort((x, y) => x.Distance.CompareTo(y.Distance));
            return _options;
        }

        // Warum (gerade) keine Fusion möglich ist; null, wenn sofort verschmolzen werden kann.
        // Gibt es Partner, aber es läuft eine Welle: "Nur zwischen den Wellen" (Optionen trotzdem über GetOptions anzeigbar)
        public string GetBlockReason(ElementalBuddy buddy)
        {
            if (buddy == null) return null;
            if (buddy.IsFusion) return "Fusionen können nicht weiter verschmolzen werden";
            if (buddy.Level < MinLevel) return $"Ab Stufe {MinLevel} verschmelzbar";
            if (buddy.Level > MaxFusionLevel) return $"Stufe {buddy.Level}: voll entwickelt – keine Fusion mehr";
            if (GetOptions(buddy).Count == 0) return $"Kein Partner in {FusionRange:0.#} m (anderes Element, Stufe {(MinLevel == MaxFusionLevel ? MinLevel.ToString() : MinLevel + "–" + MaxFusionLevel)})";
            if (!CanFuseNow) return "Nur zwischen den Wellen";
            return null;
        }

        // ---------------- Fusion ----------------

        public FusionBuddy TryFuse(ElementalBuddy a, ElementalBuddy b)
        {
            if (!CanFuseNow || !IsFusable(a) || !IsFusable(b) || a == b) return null;
            int ea = a.ElementIndex, eb = b.ElementIndex;
            if (ea == eb || HorizontalDistance(a, b) > FusionRange) return null;

            var recipe = FindRecipe(ea, eb);
            if (recipe == null || recipe.ResultConfig == null || recipe.ResultConfig.Prefab == null)
            {
                Debug.LogWarning($"FusionManager: Kein Rezept/Config/Prefab für {ElementInfo.Name(ea)} + {ElementInfo.Name(eb)}.");
                return null;
            }

            float cost = GetCost(recipe);
            var eco = EconomyManager.Instance;
            if (eco != null && cost > 0f && !eco.TrySpendShards(cost)) return null;

            Vector3 pos = Midpoint(a.transform.position, b.transform.position);
            float paid = ParentPaid(a) + ParentPaid(b) + cost;

            HideLink();
            var im = InteractionManager.Instance;
            if (im != null && (im.SelectedBuddy == a || im.SelectedBuddy == b)) im.DeselectBuddy();

            // Eltern sofort deaktivieren (Slot wird frei, bevor der neue Buddy sich registriert), dann zerstören
            a.gameObject.SetActive(false);
            b.gameObject.SetActive(false);
            Destroy(a.gameObject);
            Destroy(b.gameObject);

            var go = Instantiate(recipe.ResultConfig.Prefab, pos, Quaternion.identity);
            var fusion = go.GetComponentInChildren<FusionBuddy>();
            if (fusion == null)
            {
                Debug.LogError($"FusionManager: Prefab '{recipe.ResultConfig.Prefab.name}' hat keinen FusionBuddy.");
                var plain = go.GetComponentInChildren<ElementalBuddy>();
                if (plain != null) plain.PaidCost = paid;
                return null;
            }

            fusion.Config = recipe.ResultConfig;
            fusion.Element = recipe.Result;
            // Eltern in Rezept-Reihenfolge (ElementA zuerst) -> konsistente Anzeige
            bool ordered = recipe.ElementA == ea;
            fusion.ParentElementA = ordered ? ea : eb;
            fusion.ParentElementB = ordered ? eb : ea;
            fusion.PaidCost = paid;

            if (FusionBurstPrefab != null) Destroy(Instantiate(FusionBurstPrefab, pos, Quaternion.identity), 4f);
            GameAudio.Play(SfxId.Fusion, pos);

            if (im != null) im.SelectBuddy(fusion);
            OnFused?.Invoke(fusion);
            return fusion;
        }

        private static float ParentPaid(ElementalBuddy b)
        {
            if (b.PaidCost > 0f) return b.PaidCost;
            return b.Config != null ? b.Config.CostOutCombat : 0f;
        }

        // Mitte auf Bodenhöhe (Raycast auf FloorLayer), sonst mittlere Höhe
        private static Vector3 Midpoint(Vector3 a, Vector3 b)
        {
            Vector3 mid = (a + b) * 0.5f;
            var im = InteractionManager.Instance;
            if (im != null && im.FloorLayer.value != 0
                && Physics.Raycast(mid + Vector3.up * 20f, Vector3.down, out RaycastHit hit, 50f, im.FloorLayer.value, QueryTriggerInteraction.Ignore))
                mid.y = hit.point.y;
            return mid;
        }

        // ---------------- Hervorhebung ----------------

        public void ShowLink(ElementalBuddy a, ElementalBuddy b)
        {
            if (a == null || b == null)
            {
                HideLink();
                return;
            }
            EnsureLinkObjects();
            _linkA = a;
            _linkB = b;
            _linkArc.gameObject.SetActive(true);
            _linkRing.gameObject.SetActive(true);
            UpdateLink();
        }

        public void HideLink()
        {
            _linkA = null;
            _linkB = null;
            if (_linkArc != null) _linkArc.gameObject.SetActive(false);
            if (_linkRing != null) _linkRing.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (_linkArc == null || !_linkArc.gameObject.activeSelf) return;
            if (_linkA == null || _linkB == null) HideLink();
            else UpdateLink();
        }

        private void EnsureLinkObjects()
        {
            if (_linkArc != null) return;
            Material mat = LinkMaterial != null ? LinkMaterial : RangeIndicator.DefaultMaterial;

            _linkArc = new GameObject("FusionLink (Arc)").AddComponent<LineRenderer>();
            _linkArc.transform.SetParent(transform, false);
            _linkArc.useWorldSpace = true;
            _linkArc.positionCount = ArcPoints;
            _linkArc.numCapVertices = 4;
            SetupLine(_linkArc, mat);

            _linkRing = new GameObject("FusionLink (Ring)").AddComponent<LineRenderer>();
            _linkRing.transform.SetParent(transform, false);
            _linkRing.useWorldSpace = false;
            _linkRing.loop = true;
            _linkRing.alignment = LineAlignment.TransformZ;
            _linkRing.positionCount = RingPoints;
            for (int i = 0; i < RingPoints; i++)
            {
                float ang = i * Mathf.PI * 2f / RingPoints;
                _linkRing.SetPosition(i, new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f));
            }
            _linkRing.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            SetupLine(_linkRing, mat);
        }

        private static void SetupLine(LineRenderer line, Material mat)
        {
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = mat;
            line.gameObject.SetActive(false);
        }

        private void UpdateLink()
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f);
            Color c = LinkColor;
            c.a = LinkColor.a * Mathf.Lerp(0.55f, 1f, pulse);

            // Bogen zwischen beiden Buddies
            Vector3 p0 = _linkA.transform.position + Vector3.up * 1f;
            Vector3 p1 = _linkB.transform.position + Vector3.up * 1f;
            for (int i = 0; i < ArcPoints; i++)
            {
                float t = i / (float)(ArcPoints - 1);
                _linkArc.SetPosition(i, Vector3.Lerp(p0, p1, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * LinkArcHeight));
            }
            _linkArc.startWidth = _linkArc.endWidth = Mathf.Lerp(0.08f, 0.14f, pulse);
            _linkArc.startColor = c;
            _linkArc.endColor = c;

            // Pulsierender Ring unter dem Partner
            _linkRing.transform.position = _linkB.transform.position + Vector3.up * 0.5f;
            float radius = Mathf.Lerp(0.8f, 1.1f, pulse);
            _linkRing.transform.localScale = new Vector3(radius, radius, 1f);
            _linkRing.startWidth = _linkRing.endWidth = 0.1f;
            _linkRing.startColor = c;
            _linkRing.endColor = c;
        }
    }
}
