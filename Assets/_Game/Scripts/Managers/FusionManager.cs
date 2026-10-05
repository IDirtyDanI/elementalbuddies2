using System;
using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Buddy-Fusion: zwei Buddies unterschiedlicher Basis-Elemente (beide zwischen MinLevel und MaxFusionLevel, höchstens FusionRange auseinander)
    // verschmelzen in der Bauphase gegen Seelensplitter zu einem Fusions-Buddy in der Mitte. Rezepte sind reine Daten (Recipes).
    // Tri-Fusion (Super-Elementare, TriRecipes): (a) 2er-Fusion + Basis-Buddy genau auf TriFusionLevel des fehlenden Elements,
    // (b) drei Basis-Buddies genau auf TriFusionLevel mit drei verschiedenen Elementen. Nähe: alle Zutaten paarweise <= FusionRange
    // (damit ist die Option symmetrisch – egal, welche Zutat ausgewählt ist). Ergebnis im Schwerpunkt auf dem Boden.
    // Liegt auf dem Managers-Objekt. Achtung: Die Rezept-Listen in der Szene überschreiben die Code-Defaults.
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

        // Rezept für ein Super-Elementar aus drei Basis-Elementen (Reihenfolge egal)
        [Serializable]
        public class TriFusionRecipe
        {
            public int ElementA;
            public int ElementB;
            public int ElementC;
            public FusionElement Result;
            public UnitConfigSO ResultConfig; // Werte des Super-Buddys (+ Prefab; leer = Laufzeit-Platzhalter); Kosten = CostOutCombat
            [TextArea] public string Description;
            public Sprite Icon;

            public int Mask => (1 << ElementA) | (1 << ElementB) | (1 << ElementC);
        }

        public struct FusionOption
        {
            public ElementalBuddy Partner;
            public ElementalBuddy Partner2;   // nur Tri-Fusion aus drei Basis-Buddies
            public FusionRecipe Recipe;       // 2er-Fusion
            public TriFusionRecipe TriRecipe; // Tri-Fusion
            public float Cost;
            public float Distance;            // größter Abstand zum ausgewählten Buddy
            public bool Affordable;

            public bool IsTri => TriRecipe != null;
            public FusionElement Result => TriRecipe != null ? TriRecipe.Result : Recipe.Result;
            public Sprite Icon => TriRecipe != null ? TriRecipe.Icon : Recipe != null ? Recipe.Icon : null;
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

        public List<TriFusionRecipe> TriRecipes = new List<TriFusionRecipe>
        {
            new TriFusionRecipe { ElementA = 0, ElementB = 1, ElementC = 2, Result = FusionElement.VolcanoTitan,
                Description = "Meteor auf die dichteste Gegnergruppe: großer Flächenschaden, betäubt und stößt zurück. Hinterlässt einen Krater, der verlangsamt und brennt. Nimmt 20 % weniger Schaden." },
            new TriFusionRecipe { ElementA = 0, ElementB = 1, ElementC = 3, Result = FusionElement.StormLord,
                Description = "Ruft eine Sturmwolke über die dichteste Gruppe: macht Gegner nass und schlägt mit Kettenblitzen ein (doppelter Schaden auf Nasse). Aura: Gegner in der Nähe sind 25 % langsamer." },
            new TriFusionRecipe { ElementA = 0, ElementB = 2, ElementC = 3, Result = FusionElement.Phoenix,
                Description = "Sturzflug durch die Gegnerlinie: Schaden, Brand und eine Feuerspur. Aura: +30 % Schaden für Buddies in der Nähe. Wiedergeburt: Einmal pro Welle ersteht ein zerstörter Buddy in der Nähe (oder der Phönix selbst) mit halbem Leben neu." },
            new TriFusionRecipe { ElementA = 1, ElementB = 2, ElementC = 3, Result = FusionElement.WorldTree,
                Description = "Riesiger Wächter (25 % weniger Schaden): Wurzeln halten bis zu 6 Gegner fest, die dem Nexus am nächsten sind. Heilt Buddies und den Nexus in der Nähe. Dornen werfen Nahkampf-Schaden zurück." },
        };

        [Header("Regeln")]
        public float FusionRange = 5f;
        public int MinLevel = 2;
        [Tooltip("Höchste Stufe, auf der noch verschmolzen werden kann (Stufe 3 = ausentwickelt, keine Fusion mehr).")]
        public int MaxFusionLevel = 2;
        [Tooltip("Basis-Buddies müssen für eine Tri-Fusion genau diese Stufe haben.")]
        public int TriFusionLevel = 3;

        [Header("Optik")]
        public GameObject FusionBurstPrefab; // optional, am neuen Buddy
        public Material LinkMaterial;        // optional; leer = Laufzeit-Material
        public Color LinkColor = new Color(0.85f, 0.7f, 1f, 0.9f);
        public float LinkArcHeight = 1.5f;

        public event Action<FusionBuddy> OnFused;

        public bool CanFuseNow => ElementalBuddy.IsUpgradePhase;

        // Meta-Freischaltungen (Progression, Stand dieses Spiels): 2er-Fusionen bzw. Super-Elementare per Erfolg gesperrt
        public static bool IsFusion2Locked => !Progression.IsUnlocked(UnlockId.Fusion2);
        public static bool IsTriLocked => !Progression.IsUnlocked(UnlockId.TriFusion);
        // z. B. "2er-Fusionen gesperrt – Erfolg „Zwillingskraft“: 2 Buddies gleichzeitig auf Stufe 2"
        public static string Fusion2LockText => Progression.LockText(UnlockId.Fusion2);
        public static string TriLockText => Progression.LockText(UnlockId.TriFusion);

        // Sperrhinweis für diesen Buddy (null = Rezept-Art frei bzw. Buddy kommt dafür nicht in Frage)
        public string GetLockReason(ElementalBuddy buddy)
        {
            if (buddy == null || buddy.IsSuper) return null;
            if (!buddy.IsFusion && buddy.Level >= MinLevel && buddy.Level <= MaxFusionLevel)
                return IsFusion2Locked ? Fusion2LockText : null;
            if (buddy.IsFusion || buddy.Level == TriFusionLevel)
                return IsTriLocked ? TriLockText : null;
            return null;
        }

        private readonly List<FusionOption> _options = new List<FusionOption>();

        // Link-Hervorhebung (Laufzeit-Objekte)
        private readonly LineRenderer[] _linkArcs = new LineRenderer[2];
        private readonly LineRenderer[] _linkRings = new LineRenderer[2];
        private ElementalBuddy _linkA;
        private readonly ElementalBuddy[] _linkPartners = new ElementalBuddy[2];
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

        // Tri-Rezept über die Element-Bitmaske (genau drei Bits)
        public TriFusionRecipe FindTriRecipe(int mask)
        {
            if (TriRecipes == null) return null;
            foreach (var r in TriRecipes)
                if (r != null && r.Mask == mask) return r;
            return null;
        }

        public TriFusionRecipe FindTriRecipe(FusionElement result)
        {
            if (TriRecipes == null) return null;
            foreach (var r in TriRecipes)
                if (r != null && r.Result == result) return r;
            return null;
        }

        // Beschreibung/Icon für einen Fusions- oder Super-Buddy (beide Listen)
        public string GetDescription(FusionElement result)
        {
            var tri = FindTriRecipe(result);
            if (tri != null) return tri.Description;
            var r = FindRecipe(result);
            return r != null ? r.Description : null;
        }

        public Sprite GetIcon(FusionElement result)
        {
            var tri = FindTriRecipe(result);
            if (tri != null) return tri.Icon;
            var r = FindRecipe(result);
            return r != null ? r.Icon : null;
        }

        public float GetCost(FusionRecipe recipe) => recipe != null && recipe.ResultConfig != null ? Mathf.Ceil(recipe.ResultConfig.CostOutCombat) : 0f;
        public float GetCost(TriFusionRecipe recipe) => recipe != null && recipe.ResultConfig != null ? Mathf.Ceil(recipe.ResultConfig.CostOutCombat) : 0f;

        private bool IsFusable(ElementalBuddy b) =>
            b != null && b.isActiveAndEnabled && !b.IsFusion && b.Config != null && b.Level >= MinLevel && b.Level <= MaxFusionLevel;

        // Basis-Buddy genau auf TriFusionLevel
        private bool IsTriBase(ElementalBuddy b) =>
            b != null && b.isActiveAndEnabled && !b.IsDead && !b.IsFusion && b.Config != null && b.Level == TriFusionLevel;

        // 2er-Fusions-Buddy (kein Super-Elementar)
        private static bool IsTriFusion(ElementalBuddy b) =>
            b is FusionBuddy f && !f.IsSuper && f.isActiveAndEnabled && !f.IsDead && f.Config != null;

        private static int ElementBit(ElementalBuddy b) => 1 << Mathf.Clamp(b.ElementIndex, 0, ShrineBonuses.ElementCount - 1);

        private static int ElementMaskOf(ElementalBuddy b) => b is FusionBuddy f ? f.ElementMask : ElementBit(b);

        private static int BitCount(int m)
        {
            int n = 0;
            for (; m != 0; m &= m - 1) n++;
            return n;
        }

        private static float HorizontalDistance(ElementalBuddy a, ElementalBuddy b)
        {
            Vector3 d = a.transform.position - b.transform.position;
            d.y = 0f;
            return d.magnitude;
        }

        // Mögliche Partner (unabhängig von Bezahlbarkeit), nach Distanz sortiert. Liste wird wiederverwendet.
        // Basis Stufe 2: 2er-Fusionen. Basis Stufe 3: Tri (a) mit 2er-Fusionen ohne das eigene Element, (b) mit zwei Stufe-3-Buddies.
        // 2er-Fusion: Tri (a) mit Stufe-3-Buddies des fehlenden Elements.
        public List<FusionOption> GetOptions(ElementalBuddy buddy)
        {
            _options.Clear();
            if (buddy == null || !buddy.isActiveAndEnabled || buddy.IsSuper) return _options;
            var eco = EconomyManager.Instance;

            if (IsFusable(buddy))
            {
                if (IsFusion2Locked) return _options; // gesperrt: keine Optionen (Grund über GetBlockReason)
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
            }
            else if ((IsTriFusion(buddy) || IsTriBase(buddy)) && !IsTriLocked)
            {
                CollectTriOptions(buddy, eco);
            }
            _options.Sort((x, y) => x.Distance.CompareTo(y.Distance));
            return _options;
        }

        private readonly List<ElementalBuddy> _triBases = new List<ElementalBuddy>();

        private void CollectTriOptions(ElementalBuddy buddy, EconomyManager eco)
        {
            int ownMask = ElementMaskOf(buddy);
            var active = ElementalBuddy.Active;

            if (IsTriFusion(buddy))
            {
                // 2er-Fusion + Stufe-3-Buddy des fehlenden Elements
                foreach (var other in active)
                {
                    if (other == buddy || !IsTriBase(other) || (ownMask & ElementBit(other)) != 0) continue;
                    float dist = HorizontalDistance(buddy, other);
                    if (dist > FusionRange) continue;
                    AddTriOption(FindTriRecipe(ownMask | ElementBit(other)), other, null, dist, eco);
                }
                return;
            }

            // (a) Stufe-3-Buddy + 2er-Fusion ohne dieses Element
            foreach (var other in active)
            {
                if (other == buddy || !IsTriFusion(other) || (ElementMaskOf(other) & ownMask) != 0) continue;
                float dist = HorizontalDistance(buddy, other);
                if (dist > FusionRange) continue;
                AddTriOption(FindTriRecipe(ownMask | ElementMaskOf(other)), other, null, dist, eco);
            }

            // (b) zwei weitere Stufe-3-Buddies mit zwei anderen, verschiedenen Elementen; alle paarweise in Reichweite
            _triBases.Clear();
            foreach (var other in active)
                if (other != buddy && IsTriBase(other) && (ElementBit(other) & ownMask) == 0 && HorizontalDistance(buddy, other) <= FusionRange)
                    _triBases.Add(other);
            for (int i = 0; i < _triBases.Count; i++)
            {
                for (int j = i + 1; j < _triBases.Count; j++)
                {
                    var p = _triBases[i];
                    var q = _triBases[j];
                    if (p.ElementIndex == q.ElementIndex || HorizontalDistance(p, q) > FusionRange) continue;
                    float dist = Mathf.Max(HorizontalDistance(buddy, p), HorizontalDistance(buddy, q));
                    AddTriOption(FindTriRecipe(ownMask | ElementBit(p) | ElementBit(q)), p, q, dist, eco);
                }
            }
        }

        private void AddTriOption(TriFusionRecipe recipe, ElementalBuddy p, ElementalBuddy q, float dist, EconomyManager eco)
        {
            if (recipe == null || recipe.ResultConfig == null) return;
            float cost = GetCost(recipe);
            _options.Add(new FusionOption
            {
                Partner = p,
                Partner2 = q,
                TriRecipe = recipe,
                Cost = cost,
                Distance = dist,
                Affordable = eco == null || eco.CanAfford(cost),
            });
        }

        // Warum (gerade) keine Fusion möglich ist; null, wenn sofort verschmolzen werden kann.
        // Gibt es Partner, aber es läuft eine Welle: "Nur zwischen den Wellen" (Optionen trotzdem über GetOptions anzeigbar)
        public string GetBlockReason(ElementalBuddy buddy)
        {
            if (buddy == null) return null;
            if (buddy.IsSuper) return "Super-Elementare können nicht weiter verschmolzen werden";
            if (!buddy.IsFusion)
            {
                if (buddy.Level < MinLevel) return $"Ab Stufe {MinLevel} verschmelzbar";
                if (buddy.Level > TriFusionLevel) return $"Stufe {buddy.Level}: voll entwickelt – keine Fusion mehr (nur genau Stufe {MaxFusionLevel} oder {TriFusionLevel})";
                if (buddy.Level > MaxFusionLevel && buddy.Level < TriFusionLevel) return $"Nur genau Stufe {MaxFusionLevel} oder {TriFusionLevel}";
            }
            string locked = GetLockReason(buddy);
            if (locked != null) return locked;
            if (GetOptions(buddy).Count == 0)
            {
                if (!buddy.IsFusion && buddy.Level <= MaxFusionLevel)
                    return $"Kein Partner in {FusionRange:0.#} m (anderes Element, Stufe {(MinLevel == MaxFusionLevel ? MinLevel.ToString() : MinLevel + "–" + MaxFusionLevel)})";
                return TriNearMissReason(buddy);
            }
            if (!CanFuseNow) return "Nur zwischen den Wellen";
            return null;
        }

        // Tri-Fusion ohne Option: nächstliegenden Grund nennen (zu weit entfernt / falsche Stufe / niemand da)
        private string TriNearMissReason(ElementalBuddy buddy)
        {
            int ownMask = ElementMaskOf(buddy);
            ElementalBuddy far = null, wrongLevel = null;
            float farDist = float.MaxValue;
            _triBases.Clear(); // nahe Stufe-3-Partner (Weg b) für die Paar-Prüfung unten
            foreach (var other in ElementalBuddy.Active)
            {
                if (other == buddy || other == null || other.IsSuper || other.IsDead) continue;
                bool fits;
                bool levelOk;
                if (buddy.IsFusion)
                {
                    fits = !other.IsFusion && (ownMask & ElementBit(other)) == 0;
                    levelOk = other.Level == TriFusionLevel;
                }
                else if (other.IsFusion)
                {
                    fits = (ElementMaskOf(other) & ownMask) == 0;
                    levelOk = true;
                }
                else
                {
                    fits = (ElementBit(other) & ownMask) == 0;
                    levelOk = other.Level == TriFusionLevel;
                }
                if (!fits) continue;
                float d = HorizontalDistance(buddy, other);
                if (d > FusionRange)
                {
                    if (levelOk && d < farDist)
                    {
                        farDist = d;
                        far = other;
                    }
                }
                else if (!levelOk && wrongLevel == null) wrongLevel = other;
                else if (levelOk && !buddy.IsFusion && !other.IsFusion) _triBases.Add(other);
            }

            if (wrongLevel != null) return $"Nur genau Stufe {TriFusionLevel}: {wrongLevel.StageName} ist Stufe {wrongLevel.Level}";
            // Weg (b): beide Partner einzeln nah genug, aber zueinander zu weit (alle Zutaten müssen paarweise in Reichweite sein)
            for (int i = 0; i < _triBases.Count; i++)
                for (int j = i + 1; j < _triBases.Count; j++)
                {
                    var p = _triBases[i];
                    var q = _triBases[j];
                    float pq = HorizontalDistance(p, q);
                    if (p.ElementIndex != q.ElementIndex && pq > FusionRange)
                        return $"{p.StageName} und {q.StageName} zu weit voneinander entfernt ({pq:0.#} m > {FusionRange:0.#} m)";
                }
            if (far != null) return $"{far.StageName} zu weit entfernt ({farDist:0.#} m > {FusionRange:0.#} m)";
            if (buddy.IsFusion)
            {
                int missing = 0b1111 & ~ownMask;
                var names = new List<string>();
                for (int e = 0; e < ShrineBonuses.ElementCount; e++)
                    if ((missing & (1 << e)) != 0) names.Add(ElementInfo.Name(e));
                return $"Super-Fusion: Stufe-{TriFusionLevel}-Buddy ({string.Join("/", names)}) in {FusionRange:0.#} m nötig";
            }
            return $"Super-Fusion: Fusion ohne {ElementInfo.Name(buddy.ElementIndex)} oder zwei Stufe-{TriFusionLevel}-Buddies anderer Elemente in {FusionRange:0.#} m";
        }

        // ---------------- Fusion ----------------

        // Option aus GetOptions ausführen (2er oder Tri)
        public FusionBuddy TryFuse(ElementalBuddy buddy, FusionOption option)
        {
            if (option.IsTri) return TryFuseTri(buddy, option.Partner, option.Partner2);
            return TryFuse(buddy, option.Partner);
        }

        public FusionBuddy TryFuse(ElementalBuddy a, ElementalBuddy b)
        {
            if (!CanFuseNow || !IsFusable(a) || !IsFusable(b) || a == b) return null;
            if (IsFusion2Locked) return null;
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

            Vector3 pos = GroundCentroid(a.transform.position, b.transform.position);
            float paid = ParentPaid(a) + ParentPaid(b) + cost;

            RemoveIngredients(a, b, null);

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

            FinishFusion(fusion, pos);
            return fusion;
        }

        // Tri-Fusion: (a) c == null: eine 2er-Fusion + ein Stufe-3-Basis-Buddy; (b) drei Stufe-3-Basis-Buddies verschiedener Elemente
        public SuperBuddy TryFuseTri(ElementalBuddy a, ElementalBuddy b, ElementalBuddy c = null)
        {
            if (!CanFuseNow || a == null || b == null || a == b || c == a || c == b) return null;
            if (IsTriLocked) return null;

            int mask;
            if (c == null)
            {
                ElementalBuddy fusion = IsTriFusion(a) ? a : IsTriFusion(b) ? b : null;
                ElementalBuddy baseBuddy = fusion == a ? b : a;
                if (fusion == null || !IsTriBase(baseBuddy)) return null;
                if ((ElementMaskOf(fusion) & ElementBit(baseBuddy)) != 0) return null;
                mask = ElementMaskOf(fusion) | ElementBit(baseBuddy);
            }
            else
            {
                if (!IsTriBase(a) || !IsTriBase(b) || !IsTriBase(c)) return null;
                mask = ElementBit(a) | ElementBit(b) | ElementBit(c);
                if (BitCount(mask) != 3) return null;
                if (HorizontalDistance(a, c) > FusionRange || HorizontalDistance(b, c) > FusionRange) return null;
            }
            if (HorizontalDistance(a, b) > FusionRange) return null;

            var recipe = FindTriRecipe(mask);
            if (recipe == null || recipe.ResultConfig == null)
            {
                Debug.LogWarning($"FusionManager: Kein Tri-Rezept/Config für Element-Maske {mask}.");
                return null;
            }

            float cost = GetCost(recipe);
            var eco = EconomyManager.Instance;
            if (eco != null && cost > 0f && !eco.TrySpendShards(cost)) return null;

            Vector3 pos = c != null
                ? GroundCentroid(a.transform.position, b.transform.position, c.transform.position)
                : GroundCentroid(a.transform.position, b.transform.position);
            float paid = ParentPaid(a) + ParentPaid(b) + (c != null ? ParentPaid(c) : 0f) + cost;

            RemoveIngredients(a, b, c);

            SuperBuddy super = null;
            if (recipe.ResultConfig.Prefab != null)
            {
                var go = Instantiate(recipe.ResultConfig.Prefab, pos, Quaternion.identity);
                super = go.GetComponentInChildren<SuperBuddy>();
                if (super == null)
                {
                    Debug.LogError($"FusionManager: Prefab '{recipe.ResultConfig.Prefab.name}' hat keinen SuperBuddy – Platzhalter wird genutzt.");
                    Destroy(go);
                }
            }
            bool runtime = super == null;
            if (runtime) super = SuperBuddy.CreateRuntime(recipe.Result, pos); // inaktiv, wird unten aktiviert

            super.Config = recipe.ResultConfig;
            super.Element = recipe.Result;
            super.ParentElementA = recipe.ElementA;
            super.ParentElementB = recipe.ElementB;
            super.ParentElementC = recipe.ElementC;
            super.PaidCost = paid;
            if (runtime) super.gameObject.SetActive(true);

            FinishFusion(super, pos);
            return super;
        }

        private void RemoveIngredients(ElementalBuddy a, ElementalBuddy b, ElementalBuddy c)
        {
            HideLink();
            var im = InteractionManager.Instance;
            if (im != null && (im.SelectedBuddy == a || im.SelectedBuddy == b || (c != null && im.SelectedBuddy == c))) im.DeselectBuddy();

            // Zutaten sofort deaktivieren (Slots werden frei, bevor der neue Buddy sich registriert), dann zerstören
            foreach (var x in new[] { a, b, c })
            {
                if (x == null) continue;
                x.gameObject.SetActive(false);
                Destroy(x.gameObject);
            }
        }

        private void FinishFusion(FusionBuddy fusion, Vector3 pos)
        {
            if (FusionBurstPrefab != null) Destroy(Instantiate(FusionBurstPrefab, pos, Quaternion.identity), 4f);
            GameAudio.Play(SfxId.Fusion, pos);

            var im = InteractionManager.Instance;
            if (im != null) im.SelectBuddy(fusion);
            OnFused?.Invoke(fusion);
        }

        private static float ParentPaid(ElementalBuddy b)
        {
            if (b.PaidCost > 0f) return b.PaidCost;
            return b.Config != null ? b.Config.CostOutCombat : 0f;
        }

        // Schwerpunkt auf Bodenhöhe (Raycast auf FloorLayer), sonst mittlere Höhe
        private static Vector3 GroundCentroid(params Vector3[] points)
        {
            Vector3 mid = Vector3.zero;
            foreach (var p in points) mid += p;
            mid /= Mathf.Max(1, points.Length);
            var im = InteractionManager.Instance;
            if (im != null && im.FloorLayer.value != 0
                && Physics.Raycast(mid + Vector3.up * 20f, Vector3.down, out RaycastHit hit, 50f, im.FloorLayer.value, QueryTriggerInteraction.Ignore))
                mid.y = hit.point.y;
            return mid;
        }

        // ---------------- Hervorhebung ----------------

        // Bögen vom ausgewählten Buddy zu 1–2 Partnern + pulsierende Ringe unter den Partnern
        public void ShowLink(ElementalBuddy a, ElementalBuddy b, ElementalBuddy c = null)
        {
            if (a == null || b == null)
            {
                HideLink();
                return;
            }
            EnsureLinkObjects();
            _linkA = a;
            _linkPartners[0] = b;
            _linkPartners[1] = c;
            for (int i = 0; i < 2; i++)
            {
                bool on = _linkPartners[i] != null;
                _linkArcs[i].gameObject.SetActive(on);
                _linkRings[i].gameObject.SetActive(on);
            }
            UpdateLink();
        }

        public void HideLink()
        {
            _linkA = null;
            for (int i = 0; i < 2; i++)
            {
                _linkPartners[i] = null;
                if (_linkArcs[i] != null) _linkArcs[i].gameObject.SetActive(false);
                if (_linkRings[i] != null) _linkRings[i].gameObject.SetActive(false);
            }
        }

        void LateUpdate()
        {
            if (_linkArcs[0] == null || !_linkArcs[0].gameObject.activeSelf) return;
            if (_linkA == null || _linkPartners[0] == null || (_linkArcs[1].gameObject.activeSelf && _linkPartners[1] == null)) HideLink();
            else UpdateLink();
        }

        private void EnsureLinkObjects()
        {
            if (_linkArcs[0] != null) return;
            Material mat = LinkMaterial != null ? LinkMaterial : RangeIndicator.DefaultMaterial;

            for (int n = 0; n < 2; n++)
            {
                var arc = new GameObject($"FusionLink (Arc {n})").AddComponent<LineRenderer>();
                arc.transform.SetParent(transform, false);
                arc.useWorldSpace = true;
                arc.positionCount = ArcPoints;
                arc.numCapVertices = 4;
                SetupLine(arc, mat);
                _linkArcs[n] = arc;

                var ring = new GameObject($"FusionLink (Ring {n})").AddComponent<LineRenderer>();
                ring.transform.SetParent(transform, false);
                ring.useWorldSpace = false;
                ring.loop = true;
                ring.alignment = LineAlignment.TransformZ;
                ring.positionCount = RingPoints;
                for (int i = 0; i < RingPoints; i++)
                {
                    float ang = i * Mathf.PI * 2f / RingPoints;
                    ring.SetPosition(i, new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f));
                }
                ring.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                SetupLine(ring, mat);
                _linkRings[n] = ring;
            }
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
            Vector3 p0 = _linkA.transform.position + Vector3.up * 1f;

            for (int n = 0; n < 2; n++)
            {
                var partner = _linkPartners[n];
                if (partner == null) continue;
                var arc = _linkArcs[n];
                var ring = _linkRings[n];

                // Bogen zum Partner
                Vector3 p1 = partner.transform.position + Vector3.up * 1f;
                for (int i = 0; i < ArcPoints; i++)
                {
                    float t = i / (float)(ArcPoints - 1);
                    arc.SetPosition(i, Vector3.Lerp(p0, p1, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * LinkArcHeight));
                }
                arc.startWidth = arc.endWidth = Mathf.Lerp(0.08f, 0.14f, pulse);
                arc.startColor = c;
                arc.endColor = c;

                // Pulsierender Ring unter dem Partner
                ring.transform.position = partner.transform.position + Vector3.up * 0.5f;
                float radius = Mathf.Lerp(0.8f, 1.1f, pulse);
                ring.transform.localScale = new Vector3(radius, radius, 1f);
                ring.startWidth = ring.endWidth = 0.1f;
                ring.startColor = c;
                ring.endColor = c;
            }
        }
    }
}
