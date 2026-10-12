using UnityEditor;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // Gelände außerhalb der Stadt (max(|x|,|z|) > 44 m) neu streuen.
    // Hintergrund: Die Karte wurde per Achsen-Remap vergrößert (44–57 m = Kopie der 44-m-Zeile/Spalte, 57–80 m gestaucht).
    // Dadurch zeigten Gras-Detail-Layer und Fels-Splat dort 13 m lange einfarbige Streifen bzw. gestauchte Muster.
    // Dieses Werkzeug wendet die ursprünglichen Erzeugungsregeln an echten Weltkoordinaten neu an:
    //  – Detail-Layer 0–3 (Gras Basis/Hell/Trocken/Dunkel) für alle Zellen außerhalb 44 m,
    //  – Splat Gras/Fels (Layer 0/1) außerhalb 44 m aus Steilheit/Höhe + Perlin; Straßen (Layer 2/3) bleiben unverändert.
    // Höhen werden NICHT verändert (NavMesh ist darauf gebacken). Innenbereich (≤ 44 m) bleibt unangetastet.
    // Idempotent: fester Seed, feste Reihenfolge, Straßengewichte werden nur gelesen.
    public static class TerrainGrass
    {
        public const string TerrainDataPath = "Assets/_Game/Environment/GrassTerrain.asset";
        const float Inner = 44f;          // Grenze des unveränderten Innenbereichs (Stadtmauer-Vorfeld)
        const int Seed = 12;
        const float RoadThreshold = 0.2f; // Cobblestone+Plaza-Gewicht, ab dem kein Gras wächst (entspricht Innenbereich)
        const float StallMargin = 0.3f;   // Rand um die Händlerstand-Grundfläche ohne Gras

        [MenuItem("BuddyTD/Gelände/Gras außerhalb 44 m neu streuen")]
        public static void Run()
        {
            var td = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
            if (td == null) { Debug.LogError("[TerrainGrass] TerrainData nicht gefunden: " + TerrainDataPath); return; }
            if (td.terrainLayers.Length < 4 || td.detailPrototypes.Length < 4)
            {
                Debug.LogError("[TerrainGrass] Erwartet 4 Terrain-Layer (Gras, Fels, Pflaster, Platz) und 4 Detail-Prototypen.");
                return;
            }

            Undo.RegisterCompleteObjectUndo(td, "Gras außerhalb 44 m neu streuen");
            int splat = RepaintSplat(td);
            int cells = ScatterDetails(td);
            EditorUtility.SetDirty(td);
            AssetDatabase.SaveAssets();
            Debug.Log($"[TerrainGrass] Neu gestreut: {cells} Detail-Zellen, {splat} Splat-Zellen außerhalb {Inner} m.");
        }

        // Terrain liegt bei (−80, 0, −80), Größe 160 × 160 → Weltkoordinate aus normierter Koordinate
        static float WorldX(TerrainData td, Vector3 origin, float n) { return origin.x + n * td.size.x; }
        static float WorldZ(TerrainData td, Vector3 origin, float n) { return origin.z + n * td.size.z; }

        static Vector3 Origin(TerrainData td)
        {
            // Position aus der Szene, falls vorhanden; sonst zentriert um den Ursprung (wie in der Spielszene)
            foreach (var t in Terrain.activeTerrains)
                if (t != null && t.terrainData == td) return t.transform.position;
            return new Vector3(-td.size.x * 0.5f, 0f, -td.size.z * 0.5f);
        }

        // Fels-Anteil nach Originalregel: Steilheit > 22° bzw. Höhe > 7 m, ±0,15 Perlin-Rauschen (Periode 4 m)
        static float RockWeight(TerrainData td, float nx, float nz)
        {
            float steep = td.GetSteepness(nx, nz);
            float h = td.GetInterpolatedHeight(nx, nz);
            float r = Mathf.Clamp01(Mathf.Max((steep - 22f) / 12f, (h - 7f) / 5f));
            return Mathf.Clamp01(r + (Mathf.PerlinNoise(nx * 40f, nz * 40f) - 0.5f) * 0.3f);
        }

        static int RepaintSplat(TerrainData td)
        {
            Vector3 o = Origin(td);
            int ar = td.alphamapResolution;
            float[,,] am = td.GetAlphamaps(0, 0, ar, ar);
            int changed = 0;
            for (int y = 0; y < ar; y++)
            for (int x = 0; x < ar; x++)
            {
                float nx = (x + 0.5f) / ar, nz = (y + 0.5f) / ar;
                float wx = WorldX(td, o, nx), wz = WorldZ(td, o, nz);
                if (Mathf.Max(Mathf.Abs(wx), Mathf.Abs(wz)) <= Inner) continue;
                // Straßen-Anteil bleibt erhalten, Rest teilt sich Gras/Fels
                float road = Mathf.Clamp01(am[y, x, 2] + am[y, x, 3]);
                float rock = RockWeight(td, nx, nz);
                am[y, x, 0] = (1f - road) * (1f - rock);
                am[y, x, 1] = (1f - road) * rock;
                changed++;
            }
            td.SetAlphamaps(0, 0, am);
            return changed;
        }

        static int ScatterDetails(TerrainData td)
        {
            Vector3 o = Origin(td);
            int res = td.detailResolution;
            int ar = td.alphamapResolution;
            float[,,] am = td.GetAlphamaps(0, 0, ar, ar);
            var maps = new int[4][,];
            for (int l = 0; l < 4; l++) maps[l] = td.GetDetailLayer(0, 0, res, res, l);

            // Händlerstände: Grundfläche (Collider) + Rand, in lokalen Koordinaten des Stands
            var stalls = MerchantSetup.Stalls;
            Vector3 sc = MerchantSetup.StandColliderCenter;
            float hx = MerchantSetup.StandColliderSize.x * 0.5f + StallMargin;
            float hz = MerchantSetup.StandColliderSize.z * 0.5f + StallMargin;

            var rnd = new System.Random(Seed);
            int cells = 0;
            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float nx = (x + 0.5f) / res, nz = (y + 0.5f) / res;
                float wx = WorldX(td, o, nx), wz = WorldZ(td, o, nz);
                float d = Mathf.Max(Mathf.Abs(wx), Mathf.Abs(wz));
                if (d <= Inner) continue;
                cells++;
                for (int l = 0; l < 4; l++) maps[l][y, x] = 0;

                // Originalregel (Zufallszahlen immer gleich verbrauchen → Muster unabhängig von Masken)
                float steep = td.GetSteepness(nx, nz);
                float h = td.GetInterpolatedHeight(nx, nz);
                int count = d <= 26f ? 2 + rnd.Next(2) : 1 + rnd.Next(2);
                if (steep > 24f || h > 6f) count = 0;
                else if (h > 3f) count = rnd.Next(2);

                float nL = Mathf.PerlinNoise(wx * 0.09f + 11f, wz * 0.09f + 5f);
                float nD = Mathf.PerlinNoise(wx * 0.07f + 51f, wz * 0.07f + 23f);
                float nK = Mathf.PerlinNoise(wx * 0.11f + 91f, wz * 0.11f + 71f);
                float pD = Mathf.Clamp01((nD - 0.62f) * 3f) * 0.8f;
                float pL = Mathf.Clamp01((nL - 0.45f) * 2.2f) * 0.7f;
                float pK = Mathf.Clamp01((nK - 0.55f) * 2.5f) * 0.7f;
                var add = new int[4];
                for (int k = 0; k < count; k++)
                {
                    double r = rnd.NextDouble();
                    int v = 0;
                    if (r < pD) v = 2;
                    else if (r < pD + pL * (1f - pD)) v = 1;
                    else if (rnd.NextDouble() < pK) v = 3;
                    add[v]++;
                }

                // Masken: Straße (Pflaster/Platz) und Händlerstände
                int ax = Mathf.Clamp((int)(nx * ar), 0, ar - 1), ay = Mathf.Clamp((int)(nz * ar), 0, ar - 1);
                if (am[ay, ax, 2] + am[ay, ax, 3] > RoadThreshold) continue;
                if (InStall(stalls, sc, hx, hz, wx, wz)) continue;

                for (int l = 0; l < 4; l++) maps[l][y, x] = add[l];
            }

            for (int l = 0; l < 4; l++) td.SetDetailLayer(0, 0, l, maps[l]);
            return cells;
        }

        static bool InStall(MerchantSetup.StallDef[] stalls, Vector3 center, float hx, float hz, float wx, float wz)
        {
            foreach (var s in stalls)
            {
                // Welt → lokal (Drehung um Y um Yaw)
                float dx = wx - s.X, dz = wz - s.Z;
                float a = -s.Yaw * Mathf.Deg2Rad;
                float lx = dx * Mathf.Cos(a) + dz * Mathf.Sin(a);
                float lz = -dx * Mathf.Sin(a) + dz * Mathf.Cos(a);
                if (Mathf.Abs(lx - center.x) <= hx && Mathf.Abs(lz - center.z) <= hz) return true;
            }
            return false;
        }
    }
}
