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
        // Rückfall: Mittel unter dieser Bildrate über das Messfenster → Render Scale senken; am Minimum → 30 fps
        public const float FallbackFps = 50f;
        public const float FallbackWindow = 6f;
        // Android: kurze Bildseite höchstens so viele Pixel rendern
        public const float LowTierMaxShortSidePx = 1080f;

        // Starke Android-GPUs (Adreno 7xx/8xx, Mali-G7xx/Immortalis, Xclipse) → High; alles andere Android → Low
        public static Tier Classify(bool isIos, string gpuName, int systemMemoryMb)
        {
            if (isIos) return Tier.High;
            string g = (gpuName ?? "").ToLowerInvariant();
            bool strongGpu = g.Contains("immortalis") || g.Contains("xclipse") ||
                             g.Contains("adreno (tm) 7") || g.Contains("adreno (tm) 8") || g.Contains("adreno 7") || g.Contains("adreno 8") ||
                             g.Contains("mali-g7");
            return strongGpu && systemMemoryMb >= 6000 ? Tier.High : Tier.Low;
        }

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

        // Einen Rückfall-Schritt: erst Render Scale senken (bis MinRenderScale), danach Ziel-Bildrate 30
        public static void Fallback(ref float renderScale, ref int targetFps)
        {
            if (renderScale > MinRenderScale + 0.001f) renderScale = Mathf.Max(MinRenderScale, renderScale - FallbackStep);
            else if (targetFps > 30) targetFps = 30;
        }

        // Partikel/Treffer-Effekte: Anteil des PC-Budgets
        public static float VfxBudgetScale(Tier tier) => tier == Tier.High ? 0.8f : 0.6f;

        // Gras (Terrain-Details): Dichte-Faktor und maximale Distanz in m
        public static float DetailDensityScale(Tier tier) => tier == Tier.High ? 0.7f : 0.45f;
        public static float DetailDistance(Tier tier) => tier == Tier.High ? 60f : 40f;

        // Brennende Gebäude mit Punktlicht (PC: 6)
        public static int FireLights(Tier tier) => tier == Tier.High ? 4 : 3;
    }
}
