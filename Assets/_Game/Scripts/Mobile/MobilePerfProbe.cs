using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ElementalBuddies
{
    // Leistungs-Sonde für Gerätetests (Gerätetest Tab M11, 2026-10-20): misst CPU-/GPU-Zeit (FrameTimingManager), während
    // sie nacheinander einzelne Kostenblöcke abschaltet, und schreibt je Schritt eine Zeile „[PerfProbe] …“ ins Log (adb logcat).
    // Läuft NUR in Development-Builds im Touch-Modus und nur, wenn die Datei „perfprobe“ in Application.persistentDataPath
    // liegt (adb push). Startet im Spiel, sobald mindestens MinEnemies Gegner leben, sonst nach 8 s (z. B. Hauptmenü).
    public class MobilePerfProbe : MonoBehaviour
    {
        private const int MinEnemies = 12;
        private const float Settle = 2f;
        private const float Measure = 5f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!Debug.isDebugBuild || !MobilePlatform.TouchMode) return;
            if (!System.IO.File.Exists(System.IO.Path.Combine(Application.persistentDataPath, "perfprobe"))) return;
            var go = new GameObject("MobilePerfProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<MobilePerfProbe>();
            Active = true;
        }

        // Solange die Sonde läuft, greift der Bildraten-Rückfall von MobileQuality nicht ein
        public static bool Active { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Active = false;

        private readonly FrameTiming[] _timings = new FrameTiming[1];
        private bool _runningInGame;
        private bool _manual;

        // Einmaliger Durchlauf auf Befehl (Test-Fernsteuerung „probe“), z. B. mitten in einer großen Welle
        public static bool RunOnce()
        {
            if (Active) return false;
            var go = new GameObject("MobilePerfProbe (einmal)");
            DontDestroyOnLoad(go);
            go.AddComponent<MobilePerfProbe>()._manual = true;
            Active = true;
            return true;
        }

        void Start() => StartCoroutine(_manual ? Once() : Loop());

        private IEnumerator Once()
        {
            yield return RunAll("Fernsteuerung(" + EnemyBrain.All.Count + " Gegner)");
            Active = false;
            Destroy(gameObject);
        }

        private IEnumerator Loop()
        {
            // Hauptmenü bzw. Szene ohne Gegner: einmal nach 8 s; im Spiel, sobald genug Gegner leben (einmal pro Szene)
            yield return new WaitForSecondsRealtime(8f);
            yield return RunAll("Start");
            while (true)
            {
                yield return new WaitForSecondsRealtime(2f);
                if (!_runningInGame && EnemyBrain.All.Count >= MinEnemies)
                {
                    _runningInGame = true;
                    yield return RunAll("Welle(" + EnemyBrain.All.Count + " Gegner)");
                }
            }
        }

        private IEnumerator RunAll(string label)
        {
            int prevFps = Application.targetFrameRate;
            Application.targetFrameRate = 120; // nicht an der Bildraten-Grenze messen
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            Debug.Log($"[PerfProbe] === {label} | Render Scale {(asset != null ? asset.renderScale : -1f):0.00} | {Screen.width}x{Screen.height} ===");

            yield return Step("Basis", null, null);

            float prevShadow = asset != null ? asset.shadowDistance : 0f;
            yield return Step("ohne Schatten", () => { if (asset != null) asset.shadowDistance = 0f; },
                                               () => { if (asset != null) asset.shadowDistance = prevShadow; });

            var terrains = Terrain.activeTerrains;
            var dens = new float[terrains.Length];
            yield return Step("ohne Gras", () => { for (int i = 0; i < terrains.Length; i++) { dens[i] = terrains[i].detailObjectDensity; terrains[i].detailObjectDensity = 0f; } },
                                           () => { for (int i = 0; i < terrains.Length; i++) terrains[i].detailObjectDensity = dens[i]; });
            yield return Step("ohne Terrain-Boden", () => { foreach (var t in terrains) t.drawHeightmap = false; },
                                                    () => { foreach (var t in terrains) t.drawHeightmap = true; });
            // Vorherigen Zustand zurück (MobileQuality schaltet auf Low Gras/Bäume schon ab)
            var foliage = new bool[terrains.Length];
            yield return Step("ohne Bäume+Gras", () => { for (int i = 0; i < terrains.Length; i++) { foliage[i] = terrains[i].drawTreesAndFoliage; terrains[i].drawTreesAndFoliage = false; } },
                                                 () => { for (int i = 0; i < terrains.Length; i++) terrains[i].drawTreesAndFoliage = foliage[i]; });

            var canvases = new List<Canvas>();
            yield return Step("ohne UI", () => { canvases.Clear(); foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None)) if (c.enabled && c.isRootCanvas) { canvases.Add(c); c.enabled = false; } },
                                         () => { foreach (var c in canvases) if (c != null) c.enabled = true; });

            var parts = new List<ParticleSystemRenderer>();
            yield return Step("ohne Partikel", () => { parts.Clear(); foreach (var p in FindObjectsByType<ParticleSystemRenderer>(FindObjectsSortMode.None)) if (p.enabled) { parts.Add(p); p.enabled = false; } },
                                               () => { foreach (var p in parts) if (p != null) p.enabled = true; });

            var lights = new List<Light>();
            yield return Step("ohne Zusatzlichter", () => { lights.Clear(); foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.enabled && l.type != LightType.Directional) { lights.Add(l); l.enabled = false; } },
                                                    () => { foreach (var l in lights) if (l != null) l.enabled = true; });

            // Gegner im Einzelnen (Messung B, 2026-11-02): Skinning-Knochen, Animation, Anzahl
            var prevSkin = QualitySettings.skinWeights;
            yield return Step("Gegner 2 Knochen", () => QualitySettings.skinWeights = SkinWeights.TwoBones,
                                                  () => QualitySettings.skinWeights = prevSkin);
            var anims = new List<Animator>();
            yield return Step("Gegner ohne Animation", () => { anims.Clear(); foreach (var e in EnemyBrain.All) if (e != null) foreach (var an in e.GetComponentsInChildren<Animator>()) if (an.enabled) { anims.Add(an); an.enabled = false; } },
                                                       () => { foreach (var an in anims) if (an != null) an.enabled = true; });
            var half = new List<Renderer>();
            yield return Step("halbe Gegner-Modelle", () => { half.Clear(); var all = EnemyBrain.All; for (int i = 0; i < all.Count; i += 2) if (all[i] != null) foreach (var r in all[i].GetComponentsInChildren<Renderer>()) if (r.enabled) { half.Add(r); r.enabled = false; } },
                                                      () => { foreach (var r in half) if (r != null) r.enabled = true; });

            var skinned = new List<SkinnedMeshRenderer>();
            yield return Step("ohne Gegner-Modelle", () => { skinned.Clear(); foreach (var e in EnemyBrain.All) if (e != null) foreach (var r in e.GetComponentsInChildren<SkinnedMeshRenderer>()) if (r.enabled) { skinned.Add(r); r.enabled = false; } },
                                                     () => { foreach (var r in skinned) if (r != null) r.enabled = true; });

            float prevScale = asset != null ? asset.renderScale : 1f;
            yield return Step("Render Scale 0,5", () => { if (asset != null) asset.renderScale = 0.5f; },
                                                  () => { if (asset != null) asset.renderScale = prevScale; });

            Application.targetFrameRate = prevFps;
            Debug.Log("[PerfProbe] === Ende ===");
        }

        private IEnumerator Step(string name, System.Action apply, System.Action revert)
        {
            apply?.Invoke();
            yield return new WaitForSecondsRealtime(Settle);
            float t = 0f, cpu = 0f, gpu = 0f, frame = 0f;
            int n = 0, gpuN = 0;
            while (t < Measure)
            {
                yield return null;
                t += Time.unscaledDeltaTime;
                frame += Time.unscaledDeltaTime * 1000f;
                n++;
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, _timings) > 0)
                {
                    cpu += (float)_timings[0].cpuFrameTime;
                    if (_timings[0].gpuFrameTime > 0) { gpu += (float)_timings[0].gpuFrameTime; gpuN++; }
                }
            }
            revert?.Invoke();
            float avg = frame / Mathf.Max(1, n);
            Debug.Log($"[PerfProbe] {name,-20} Frame {avg:0.0} ms ({1000f / avg:0} fps) | CPU {cpu / Mathf.Max(1, n):0.0} ms | GPU {(gpuN > 0 ? (gpu / gpuN).ToString("0.0") : "–")} ms | Gegner {EnemyBrain.All.Count}");
        }
    }
}
