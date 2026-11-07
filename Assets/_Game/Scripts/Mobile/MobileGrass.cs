using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Mobil-Gras (Gerätetest Tab M11, 2026-11-02): ~100 000 Gras-Instanzen mit dem PBR-Shader-Graph-Gras kosten auf dem
    // Mali-G52 ~10 ms, Render Scale hilft kaum (Vertex-/Instanz-gebunden). Auf der Low-Stufe bekommt das Terrain deshalb eine
    // Laufzeit-Kopie seiner TerrainData: weniger, dafür größere Büschel und der sparsame Shader „BuddyTD/GrasMobil“
    // (Licht pro Vertex, keine Schatten). Das Asset selbst bleibt unverändert (wichtig im Editor und für die PC-Version).
    public static class MobileGrass
    {
        private const string MaterialPath = "GrasMobil"; // Resources/GrasMobil.mat, hält den Shader im Build
        private static readonly List<Object> _created = new List<Object>();

        public static void Apply(Terrain t, float density, float sizeScale, int basemapResolution)
        {
            var src = t.terrainData;
            if (src == null || src.detailPrototypes.Length == 0) return;
            var baseMat = Resources.Load<Material>(MaterialPath);
            if (baseMat == null) { Debug.LogWarning("[MobileGrass] Resources/GrasMobil.mat fehlt"); return; }

            var data = Object.Instantiate(src);
            data.name = src.name + " (Mobil)";
            _created.Add(data);
            // Schärfere Basemap (Gesamtbild des Bodens): Wege bleiben scharf, obwohl das Terrain nur eine Textur liest
            if (basemapResolution > data.baseMapResolution) data.baseMapResolution = basemapResolution;

            int w = data.detailWidth, h = data.detailHeight;
            var protos = data.detailPrototypes;
            long before = 0, after = 0;
            for (int i = 0; i < protos.Length; i++)
            {
                var layer = data.GetDetailLayer(0, 0, w, h, i);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int v = layer[y, x];
                        if (v == 0) continue;
                        before += v;
                        layer[y, x] = MobileQualityRules.ThinGrassCount(v, density, x, y, i);
                        after += layer[y, x];
                    }
                data.SetDetailLayer(0, 0, i, layer);
                protos[i] = MobilePrototype(protos[i], baseMat, sizeScale);
            }
            data.detailPrototypes = protos;
            t.terrainData = data;
            Debug.Log($"[MobileGrass] {src.name}: {before} → {after} Gras-Instanzen, Größe ×{sizeScale:0.00}, Basemap {data.baseMapResolution}");
        }

        private static DetailPrototype MobilePrototype(DetailPrototype p, Material baseMat, float sizeScale)
        {
            var copy = new DetailPrototype(p);
            copy.minWidth *= sizeScale; copy.maxWidth *= sizeScale;
            copy.minHeight *= sizeScale; copy.maxHeight *= sizeScale;
            if (!p.usePrototypeMesh || p.prototype == null) return copy;
            var mf = p.prototype.GetComponentInChildren<MeshFilter>();
            var mr = p.prototype.GetComponentInChildren<MeshRenderer>();
            if (mf == null || mr == null || mr.sharedMaterial == null) return copy;

            var src = mr.sharedMaterial;
            var mat = new Material(baseMat) { name = src.name + " (Mobil)" };
            if (src.HasProperty("_MainTex")) mat.SetTexture("_MainTex", src.GetTexture("_MainTex"));
            if (src.HasProperty("_Color")) mat.SetColor("_Color", src.GetColor("_Color"));
            if (src.HasProperty("_Scale")) mat.SetFloat("_Scale", src.GetFloat("_Scale"));
            if (src.HasProperty("_Speed")) mat.SetFloat("_Speed", src.GetFloat("_Speed"));
            mat.enableInstancing = true;

            // Vorlage für die Terrain-Details: inaktives, verstecktes Objekt mit Mesh + Mobil-Material
            var go = new GameObject(p.prototype.name + " (Mobil)");
            go.hideFlags = HideFlags.HideAndDontSave;
            go.SetActive(false);
            if (Application.isPlaying) Object.DontDestroyOnLoad(go);
            go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            _created.Add(mat);
            _created.Add(go);
            copy.prototype = go;
            return copy;
        }

        // Kopien der vorigen Szene freigeben (vor dem Anwenden auf die Terrains einer neuen Szene aufrufen)
        public static void Cleanup()
        {
            foreach (var o in _created)
                if (o != null) { if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o); }
            _created.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _created.Clear();
    }
}
