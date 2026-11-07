using NUnit.Framework;

namespace ElementalBuddies.Tests
{
    // Mobil-Grafik (Paket P): Geräteklasse, Render Scale je Gerät, Bildraten-Rückfall
    public class MobileQualityRulesTests
    {
        private const float Eps = 0.001f;

        [Test]
        public void Classify_TabM11_Low_iPhone_High()
        {
            Assert.AreEqual(MobileQualityRules.Tier.Low, MobileQualityRules.Classify(false, "Mali-G52 MC2", 3700));
            Assert.AreEqual(MobileQualityRules.Tier.High, MobileQualityRules.Classify(true, "Apple A18 Pro GPU", 7800));
            Assert.AreEqual(MobileQualityRules.Tier.High, MobileQualityRules.Classify(false, "Adreno (TM) 740", 8000));
            // starke GPU, aber wenig Speicher → vorsichtig
            Assert.AreEqual(MobileQualityRules.Tier.Low, MobileQualityRules.Classify(false, "Adreno (TM) 740", 4000));
            Assert.AreEqual(MobileQualityRules.Tier.Low, MobileQualityRules.Classify(false, null, 0));
        }

        [Test]
        public void RenderScale_JeGeraet()
        {
            // Tab M11: Tablet, 1200 px kurze Seite → 0,8 (Deckel 1080/1200 = 0,9 greift nicht)
            Assert.AreEqual(0.8f, MobileQualityRules.RenderScale(MobileQualityRules.Tier.Low, false, 207f, 1200f), Eps);
            // Android-Telefon mit hoher DPI → 0,7, sonst 0,75
            Assert.AreEqual(0.7f, MobileQualityRules.RenderScale(MobileQualityRules.Tier.Low, true, 450f, 1080f), Eps);
            Assert.AreEqual(0.75f, MobileQualityRules.RenderScale(MobileQualityRules.Tier.Low, true, 320f, 720f), Eps);
            // iPhone 16 Pro Max → 0,85 (kein Pixel-Deckel)
            Assert.AreEqual(0.85f, MobileQualityRules.RenderScale(MobileQualityRules.Tier.High, true, 460f, 1320f), Eps);
            // Android mit sehr hoher Auflösung: kurze Seite höchstens 1080 px
            Assert.AreEqual(1080f / 1600f, MobileQualityRules.RenderScale(MobileQualityRules.Tier.Low, false, 280f, 1600f), Eps);
        }

        [Test]
        public void Fallback_ErstRenderScaleDann30fps()
        {
            float s = 0.8f;
            int fps = 60;
            MobileQualityRules.Fallback(ref s, ref fps);
            Assert.AreEqual(0.75f, s, Eps);
            Assert.AreEqual(60, fps);
            MobileQualityRules.Fallback(ref s, ref fps);
            Assert.AreEqual(MobileQualityRules.FallbackMinRenderScale, s, Eps);
            MobileQualityRules.Fallback(ref s, ref fps);
            Assert.AreEqual(30, fps);
            MobileQualityRules.Fallback(ref s, ref fps);
            Assert.AreEqual(30, fps);
            Assert.AreEqual(MobileQualityRules.FallbackMinRenderScale, s, Eps);
        }

        [Test]
        public void Fallback_nicht_unter_0_7_und_Geraetewert_darunter_bleibt()
        {
            Assert.GreaterOrEqual(MobileQualityRules.FallbackMinRenderScale, 0.7f - Eps);
            float s = 0.675f; // Android mit sehr hoher Auflösung startet schon darunter
            int fps = 30;
            MobileQualityRules.Fallback(ref s, ref fps);
            Assert.AreEqual(0.675f, s, Eps);
        }

        [Test]
        public void Rueckfall_wird_am_Wellenende_zurueckgenommen()
        {
            // Welle endet, Scale war abgesenkt → zurück
            Assert.IsTrue(MobileQualityRules.ShouldRestore(true, false, 0.7f, 0.8f, 30, 30));
            // Ziel-Bildrate abgesenkt (High: 60 → 30) → zurück
            Assert.IsTrue(MobileQualityRules.ShouldRestore(true, false, 0.85f, 0.85f, 30, 60));
            // Welle läuft noch / keine Welle beendet / nichts abgesenkt → nichts tun
            Assert.IsFalse(MobileQualityRules.ShouldRestore(true, true, 0.7f, 0.8f, 30, 30));
            Assert.IsFalse(MobileQualityRules.ShouldRestore(false, false, 0.7f, 0.8f, 30, 30));
            Assert.IsFalse(MobileQualityRules.ShouldRestore(true, false, 0.8f, 0.8f, 30, 30));
        }

        [Test]
        public void Budgets_Mobil_KleinerAlsPc()
        {
            Assert.Less(MobileQualityRules.VfxBudgetScale(MobileQualityRules.Tier.Low), MobileQualityRules.VfxBudgetScale(MobileQualityRules.Tier.High));
            Assert.LessOrEqual(MobileQualityRules.VfxBudgetScale(MobileQualityRules.Tier.High), 1f);
            Assert.Less(MobileQualityRules.FireLights(MobileQualityRules.Tier.Low), 6);
            Assert.Less(MobileQualityRules.DetailDensityScale(MobileQualityRules.Tier.Low), 1f);
        }

        [Test]
        public void Low_ohne_Gegner_Schatten_und_Post_mit_wenig_Lichtern()
        {
            Assert.IsFalse(MobileQualityRules.EnemyShadows(MobileQualityRules.Tier.Low));
            Assert.IsFalse(MobileQualityRules.PostProcessing(MobileQualityRules.Tier.Low));
            Assert.AreEqual(2, MobileQualityRules.MaxAdditionalLightsPerObject(MobileQualityRules.Tier.Low));
            Assert.Less(MobileQualityRules.DetailDistance(MobileQualityRules.Tier.Low), MobileQualityRules.DetailDistance(MobileQualityRules.Tier.High));
        }

        [Test]
        public void High_behaelt_Schatten_und_Post()
        {
            Assert.IsTrue(MobileQualityRules.EnemyShadows(MobileQualityRules.Tier.High));
            Assert.IsTrue(MobileQualityRules.PostProcessing(MobileQualityRules.Tier.High));
            Assert.AreEqual(4, MobileQualityRules.MaxAdditionalLightsPerObject(MobileQualityRules.Tier.High));
        }

        [Test]
        public void Low_fest_30_fps_High_60()
        {
            Assert.AreEqual(30, MobileQualityRules.TargetFps(MobileQualityRules.Tier.Low));
            Assert.AreEqual(60, MobileQualityRules.TargetFps(MobileQualityRules.Tier.High));
        }

        [Test]
        public void Mobil_Gras_nur_auf_Low()
        {
            Assert.IsTrue(MobileQualityRules.MobileGrass(MobileQualityRules.Tier.Low));
            Assert.IsFalse(MobileQualityRules.MobileGrass(MobileQualityRules.Tier.High));
            Assert.AreEqual(1f, MobileQualityRules.GrassInstanceShare(MobileQualityRules.Tier.High));
        }

        [Test]
        public void Gras_Ausduennen_trifft_den_Anteil()
        {
            long before = 0, after = 0;
            for (int y = 0; y < 200; y++)
                for (int x = 0; x < 200; x++)
                {
                    int v = (x + y) % 5;
                    before += v;
                    after += MobileQualityRules.ThinGrassCount(v, 0.4f, x, y, 1);
                }
            Assert.AreEqual(0.4, after / (double)before, 0.02);
            Assert.AreEqual(3, MobileQualityRules.ThinGrassCount(3, 1f, 0, 0, 0));
            Assert.AreEqual(0, MobileQualityRules.ThinGrassCount(3, 0f, 0, 0, 0));
            Assert.AreEqual(MobileQualityRules.ThinGrassCount(4, 0.4f, 7, 9, 2), MobileQualityRules.ThinGrassCount(4, 0.4f, 7, 9, 2));
        }

        [Test]
        public void Low_kuerzere_Schatten_als_High()
        {
            Assert.Less(MobileQualityRules.ShadowDistance(MobileQualityRules.Tier.Low), MobileQualityRules.ShadowDistance(MobileQualityRules.Tier.High));
            Assert.Greater(MobileQualityRules.ShadowDistance(MobileQualityRules.Tier.Low), 15f); // Spieler + nahe Gebäude behalten Schatten
        }

        [Test]
        public void Classify_Pixel10_PowerVrDxt_High_alte_PowerVR_Low()
        {
            // Google Pixel 10 Pro (Tensor G5): Tester-Feedback 2026-11-03, Boden schwammig auf Low
            Assert.AreEqual(MobileQualityRules.Tier.High, MobileQualityRules.Classify(false, "PowerVR D-Series DXT-48-1536", 16000));
            Assert.AreEqual(MobileQualityRules.Tier.High, MobileQualityRules.Classify(false, "Imagination PowerVR DXT-48-1536 MC1", 12000));
            // Einstiegsgeräte mit PowerVR bleiben Low
            Assert.AreEqual(MobileQualityRules.Tier.Low, MobileQualityRules.Classify(false, "PowerVR Rogue GE8320", 3000));
            Assert.AreEqual(MobileQualityRules.Tier.Low, MobileQualityRules.Classify(false, "PowerVR B-Series BXM-8-256", 6000));
        }

        [Test]
        public void Grafik_Einstellung_ueberschreibt_Erkennung()
        {
            var low = MobileQualityRules.Tier.Low;
            var high = MobileQualityRules.Tier.High;
            Assert.AreEqual(low, MobileQualityRules.Resolve(MobileQualityRules.Choice.Auto, low));
            Assert.AreEqual(high, MobileQualityRules.Resolve(MobileQualityRules.Choice.Auto, high));
            Assert.AreEqual(low, MobileQualityRules.Resolve(MobileQualityRules.Choice.Fast, high));
            Assert.AreEqual(high, MobileQualityRules.Resolve(MobileQualityRules.Choice.Pretty, low));
            Assert.AreEqual("Auto (Schnell)", MobileQualityRules.ChoiceLabel(MobileQualityRules.Choice.Auto, low));
            Assert.AreEqual("Schön", MobileQualityRules.ChoiceLabel(MobileQualityRules.Choice.Pretty, low));
        }
    }
}
