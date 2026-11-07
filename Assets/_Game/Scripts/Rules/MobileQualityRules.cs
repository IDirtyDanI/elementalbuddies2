using UnityEngine;

namespace ElementalBuddies
{
    // Mobil-Grafik (Paket P, 2026-10-20): reine Regeln für MobileQuality – Geräteklasse, Render Scale, Rückfall bei
    // zu niedriger Bildrate. Ohne Unity-Laufzeitabhängigkeiten, damit EditMode-Tests sie prüfen können.
    public static class MobileQualityRules
    {
        // Low = Android-Einstiegs-/Mittelklasse (z. B. Tab M11, Mali-G52), High = iOS (A-Chips) und starke Android-GPUs
        public enum Tier { Low, High }

        public const float MinRenderScale = 0.6f;
        public const float FallbackStep = 0.05f;
        // Der Rückfall senkt die Render Scale höchstens bis hierhin (Tab M11, 2026-11-02: 0,8 → 0,6 sparte bei vielen
        // Gegnern nur ~6 ms GPU, das Bild wurde aber deutlich unscharf – Gegner kosten Geometrie, nicht Pixel)
        public const float FallbackMinRenderScale = 0.7f;
        // Rückfall: Mittel unter dieser Bildrate über das Messfenster → Render Scale senken; am Minimum → 30 fps
        public const float FallbackFps = 50f;
        public const float FallbackWindow = 6f;
        // Einzelbilder darüber (Laden, Szenenwechsel) zählen nicht, sondern starten das Messfenster neu
        public const float FallbackMaxFrameTime = 0.25f;
        // Android: kurze Bildseite höchstens so viele Pixel rendern
        public const float LowTierMaxShortSidePx = 1080f;

        // Starke Android-GPUs (Adreno 7xx/8xx, Mali-G7xx/Immortalis, Xclipse) → High; alles andere Android → Low
        public static Tier Classify(bool isIos, string gpuName, int systemMemoryMb)
        {
            if (isIos) return Tier.High;
            string g = (gpuName ?? "").ToLowerInvariant();
            bool strongGpu = g.Contains("immortalis") || g.Contains("xclipse") ||
                             g.Contains("adreno (tm) 7") || g.Contains("adreno (tm) 8") || g.Contains("adreno 7") || g.Contains("adreno 8") ||
                             g.Contains("mali-g7") || IsPowerVrDxt(g);
            return strongGpu && systemMemoryMb >= 6000 ? Tier.High : Tier.Low;
        }

        // Imagination PowerVR DXT (D-Serie, z. B. Google Pixel 10 / Tensor G5, Tester-Feedback 2026-11-03: Boden schwammig,
        // weil als Low erkannt). Ältere PowerVR (Rogue GE8xxx, B-/C-Serie) stecken in Einstiegsgeräten und bleiben Low.
        public static bool IsPowerVrDxt(string gpuLower) =>
            gpuLower.Contains("powervr") && (gpuLower.Contains("dxt") || gpuLower.Contains("d-series"));

        // Grafik-Einstellung im Pausenmenü (2026-11-03): Automatisch = Erkennung oben, sonst fest gewählt
        public enum Choice { Auto = 0, Fast = 1, Pretty = 2 }

        public static Tier Resolve(Choice choice, Tier detected) =>
            choice == Choice.Fast ? Tier.Low : choice == Choice.Pretty ? Tier.High : detected;

        public static string TierLabel(Tier t) => t == Tier.High ? "Schön" : "Schnell";

        public static string ChoiceLabel(Choice choice, Tier detected) =>
            choice == Choice.Auto ? "Auto (" + TierLabel(detected) + ")" : TierLabel(Resolve(choice, detected));

        // Start-Render-Scale je Gerät: Telefon mit hoher DPI 0,7 (Android) bzw. 0,85 (iPhone), Tablet 0,8 (Android) bzw.
        // 0,85 (iPad). Android rendert die kurze Seite zusätzlich höchstens mit LowTierMaxShortSidePx.
        public static float RenderScale(Tier tier, bool phone, float dpi, float shortSidePx)
        {
            float s;
            if (tier == Tier.High) s = 0.85f;
            else if (!phone) s = 0.8f;
            else s = dpi >= 400f ? 0.7f : 0.75f;
            if (tier == Tier.Low && shortSidePx > 1f) s = Mathf.Min(s, LowTierMaxShortSidePx / shortSidePx);
            return Mathf.Clamp(s, MinRenderScale, 1f);
        }

        // Einen Rückfall-Schritt: erst Render Scale senken (bis FallbackMinRenderScale; liegt der Gerätewert schon darunter,
        // bleibt er), danach Ziel-Bildrate 30
        public static void Fallback(ref float renderScale, ref int targetFps)
        {
            if (renderScale > FallbackMinRenderScale + 0.001f) renderScale = Mathf.Max(FallbackMinRenderScale, renderScale - FallbackStep);
            else if (targetFps > 30) targetFps = 30;
        }

        // Rückfall zurücknehmen, sobald es ruhig ist (Wellenende): wieder Gerätewerte – reicht es dann nicht, greift der
        // Rückfall in der nächsten Welle erneut. Sonst bliebe das Bild nach einer großen Welle für den Rest des Spiels unscharf.
        public static bool ShouldRestore(bool waveWasActive, bool waveActive, float renderScale, float baseRenderScale, int targetFps, int baseTargetFps)
            => waveWasActive && !waveActive && (renderScale < baseRenderScale - 0.001f || targetFps < baseTargetFps);

        // Ziel-Bildrate: Low fest 30 (Tab M11 schaffte ~25–37 ms, schwankende 33/45 fps auf dem 90-Hz-Schirm wirkten
        // hakelig; Nutzerentscheidung 2026-11-02), High 60
        public static int TargetFps(Tier tier) => tier == Tier.High ? 60 : 30;

        // Partikel/Treffer-Effekte: Anteil des PC-Budgets
        public static float VfxBudgetScale(Tier tier) => tier == Tier.High ? 0.8f : 0.6f;

        // Gras (Terrain-Details): Dichte-Faktor und maximale Distanz in m. Low seit dem Gerätetest Tab M11 (2026-10-20,
        // GPU-gebunden, ~19 fps) deutlich dünner: Alpha-Test-Gras ist auf Mali teuer (Überzeichnen).
        public static float DetailDensityScale(Tier tier) => tier == Tier.High ? 0.7f : 0.25f;
        public static float DetailDistance(Tier tier) => tier == Tier.High ? 60f : 40f;

        // Gerätetest 2026-11-02: ~100 000 Gras-Instanzen mit Shader-Graph-Gras (PBR, Alpha-Clip, Wind) kosten auf dem Mali-G52
        // ~10 ms außerhalb der Stadt; die Dichte-Einstellung greift bei instanziertem Gras nicht. Low: eigenes Mobil-Gras
        // (MobileGrass) mit diesem Anteil der Instanzen, dafür um GrassSizeScale größer (Fläche ×0,4×1,69 ≈ 68 %).
        public static float GrassInstanceShare(Tier tier) => tier == Tier.High ? 1f : 0.4f;
        public static float GrassSizeScale(Tier tier) => tier == Tier.High ? 1f : 1.3f;
        public static bool MobileGrass(Tier tier) => tier == Tier.Low;

        // Ausdünnen einer Detail-Zelle (Anzahl-Modus): Erwartungswert count×share, deterministisch je Zelle/Ebene gestreut
        public static int ThinGrassCount(int count, float share, int x, int y, int layer)
        {
            if (count <= 0 || share >= 1f) return count;
            if (share <= 0f) return 0;
            uint hsh = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(layer * 83492791);
            hsh ^= hsh >> 13; hsh *= 0x5bd1e995; hsh ^= hsh >> 15;
            float dither = (hsh & 0xFFFF) / 65536f;
            return Mathf.FloorToInt(count * share + dither);
        }

        // Zusatzlichter pro Objekt (Forward-Renderer; PC nutzt Forward+). Low: nur die zwei nächsten Lichter.
        public static int MaxAdditionalLightsPerObject(Tier tier) => tier == Tier.High ? 4 : 2;

        // Gegner werfen auf Low keine Schatten (viele Skinned Meshes = viele Schatten-Draws); Gebäude/Figur behalten sie
        public static bool EnemyShadows(Tier tier) => tier == Tier.High;

        // Post-Processing (Farbkorrektur, Vignette, Tonemapping) kostet auf Mali einen Vollbild-Pass; Low ohne
        public static bool PostProcessing(Tier tier) => tier == Tier.High;

        // Terrain auf Low: Basemap ab dieser Distanz, Mesh-Fehler in Pixeln (Unity-Standard 5). Verlauf Gerätetest Tab M11:
        // Basemap überall mit 256 px (Asset) machte Wege matschig (60 cm/px); voller Terrain-Shader (4 Ebenen) kostet ~8 ms
        // (pixelgebunden). 2026-11-02: Basemap überall, aber auf 2048 px hochgesetzt (8 cm/px, nur in der Laufzeit-Kopie)
        public const float LowTerrainBasemapDistance = 0f;
        public const int LowTerrainBasemapResolution = 2048;
        public const float LowTerrainPixelError = 25f;
        // Terrain-Bäume auf Low: Sichtweite und Billboard-Distanz in m (Stadt/Walddorf haben derzeit keine Terrain-Bäume)
        public const float LowTreeDistance = 80f;
        public const float LowTreeBillboardDistance = 30f;

        // Schattendistanz in m (Mobil-Asset: 40). Low: Schatten kosten in der Welle ~5 ms (Gerätetest 2026-11-02)
        public static float ShadowDistance(Tier tier) => tier == Tier.High ? 40f : 25f;

        // Brennende Gebäude mit Punktlicht (PC: 6)
        public static int FireLights(Tier tier) => tier == Tier.High ? 4 : 3;
    }
}
