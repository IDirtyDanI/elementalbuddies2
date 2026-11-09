using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace ElementalBuddies.Tests
{
    // Intro-Film zur ersten Welle (wiki: Design/Intro-Cutscene): Kamerabahn aus den Pfad-Ecken Portal → Nexus
    public class IntroCutscenePathTests
    {
        // Stadt-ähnlich: Portal West (−44, 0) → Nexus (26, 0) mit zwei Knicken
        private static readonly Vector3 Portal = new Vector3(-44f, 0f, 0f);
        private static readonly Vector3 NexusPos = new Vector3(26f, 0f, 0f);

        private static List<Vector3> Corners() => new List<Vector3>
        {
            new Vector3(-43.5f, 0f, 0.2f),
            new Vector3(-20f, 0f, 6f),
            new Vector3(0f, 0.5f, 6f),
            new Vector3(10f, 0.5f, -2f),
            new Vector3(23f, 0f, 0f),
        };

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        [Test]
        public void Laenge_Ist12Sekunden()
        {
            Assert.AreEqual(12f, IntroCutscenePath.Duration, 1e-5f);
            Assert.AreEqual(IntroCutscenePath.Duration, IntroCutscenePath.Shot3End + IntroCutscenePath.ReturnDuration, 1e-5f);
            Assert.Less(IntroCutscenePath.FadeInEnd, IntroCutscenePath.Shot1End);
            Assert.Less(IntroCutscenePath.Shot1End, IntroCutscenePath.Shot2End);
            Assert.Less(IntroCutscenePath.Shot2End, IntroCutscenePath.Shot3End);
            Assert.Less(IntroCutscenePath.SkipReturnDuration, IntroCutscenePath.ReturnDuration);
        }

        [Test]
        public void Bahn_BeginntAmPortal_EndetAmNexus()
        {
            var path = new IntroCutscenePath(Corners(), Portal, NexusPos);
            Assert.IsFalse(path.UsedFallback);
            Assert.Less(Vector3.Distance(path.PointAt(0f), Portal), 0.6f);
            Assert.Less(Vector3.Distance(path.PointAt(path.Length), NexusPos), 0.6f);
            Assert.Greater(path.Length, 70f, "Umweg über die Knicke ist länger als die Luftlinie");
            Assert.Greater(path.StartDir.x, 0.5f, "vom Portal weg Richtung Osten");
        }

        [Test]
        public void Start_TiefHinterDemPortal_BlickDurchsPortal()
        {
            var path = new IntroCutscenePath(Corners(), Portal, NexusPos);
            var pose = path.Evaluate(IntroCutscenePath.FadeInEnd);
            Vector3 rel = pose.Position - Portal;
            Assert.Less(Vector3.Dot(Flat(rel), path.StartDir), -3f, "Kamera steht hinter dem Portal");
            Assert.AreEqual(path.LowHeight, pose.Position.y - Portal.y, 0.6f, "tief, etwa 2,5 m");
            Assert.Greater(Vector3.Dot(Flat(pose.Forward).normalized, path.StartDir), 0.9f, "Blick durchs Portal den Weg entlang");
        }

        [Test]
        public void Einstellung1_FaehrtLeichtHeran()
        {
            var path = new IntroCutscenePath(Corners(), Portal, NexusPos);
            float d0 = Vector3.Distance(Flat(path.Evaluate(0.5f).Position), Flat(Portal));
            float d1 = Vector3.Distance(Flat(path.Evaluate(3.5f).Position), Flat(Portal));
            Assert.Less(d1, d0);
            Assert.Less(d0 - d1, 4f, "nur leichtes Heranfahren");
        }

        [Test]
        public void Einstellung2_FliegtHochUeberDenWeg()
        {
            var path = new IntroCutscenePath(Corners(), Portal, NexusPos);
            var mid = path.Evaluate(7f);
            float s = path.ProjectS(mid.Position);
            Assert.Greater(mid.Position.y - path.PointAt(s).y, 9f, "Flughöhe etwa 10–12 m");
            Assert.Less(Vector3.Distance(Flat(mid.Position), Flat(path.PointAt(s))), 1.5f, "über dem Weg");
            Assert.Less(mid.Forward.y, -0.2f, "Blick schräg nach unten auf den Weg");
            Assert.Greater(s, path.Length * 0.4f, "bis 7 s mehr als die Hälfte geschafft");
        }

        [Test]
        public void Ende_BlicktVomNexusDenWegHinunterRichtungPortal()
        {
            var path = new IntroCutscenePath(Corners(), Portal, NexusPos);
            var end = path.Evaluate(IntroCutscenePath.Shot3End);
            Vector3 toNexus = Flat(NexusPos - end.Position);
            Vector3 fwd = Flat(end.Forward).normalized;
            Assert.Greater(Vector3.Dot(Flat(end.Position - NexusPos), path.EndDir), 4f, "Kamera steht hinter dem Nexus");
            Assert.Greater(Vector3.Dot(fwd, toNexus.normalized), 0.8f, "Nexus im Vordergrund");
            Assert.Greater(Vector3.Dot(fwd, Flat(Portal - end.Position).normalized), 0.7f, "Blick Richtung Portal");
            Vector3 road = path.PointAt(path.Length - 20f);
            Assert.Greater(Vector3.Dot(fwd, Flat(road - end.Position).normalized), 0.9f, "Blick den Weg hinunter");
            Assert.Less(end.Forward.y, 0f, "von oben");
            Assert.Greater(end.Position.y - NexusPos.y, 4f, "über dem Nexus (Baumkronen)");
        }

        [Test]
        public void Ende_SeitlichVersetzt_BlicktWeiterDenWegHinunter()
        {
            foreach (float offset in new[] { 25f, -25f })
            foreach (float sign in new[] { 1f, -1f })
            {
                var path = new IntroCutscenePath(Corners(), Portal, NexusPos) { EndAngleOffset = offset, OrbitSign = sign, NexusRadius = 10.5f, NexusHeight = 9f };
                var end = path.Evaluate(IntroCutscenePath.Shot3End);
                Assert.AreEqual(10.5f, Vector3.Distance(Flat(end.Position), Flat(NexusPos)), 0.05f);
                Vector3 road = path.PointAt(path.Length - 20f);
                Assert.Greater(Vector3.Dot(Flat(end.Forward).normalized, Flat(road - end.Position).normalized), 0.9f);
                AssertContinuous(path, float.NaN);
            }
        }

        [Test]
        public void Positionen_StetigOhneSpruenge()
        {
            var path = new IntroCutscenePath(Corners(), Portal, NexusPos);
            AssertContinuous(path, float.NaN);
            AssertContinuous(path, 12f); // mit Gegnerkolonne: nur der Blick ändert sich, ebenfalls stetig
        }

        private static void AssertContinuous(IntroCutscenePath path, float columnS)
        {
            const float dt = 0.01f;
            var prev = path.Evaluate(IntroCutscenePath.FadeInEnd, columnS);
            for (float t = IntroCutscenePath.FadeInEnd + dt; t <= IntroCutscenePath.Shot3End + 1e-4f; t += dt)
            {
                var cur = path.Evaluate(t, columnS);
                float step = Vector3.Distance(prev.Position, cur.Position);
                Assert.Less(step, 0.35f, $"Sprung bei t = {t:0.00}: {step:0.00} m");
                Assert.Less(Quaternion.Angle(prev.Rotation, cur.Rotation), 3f, $"Dreh-Sprung bei t = {t:0.00}");
                Assert.IsFalse(float.IsNaN(cur.Position.x) || float.IsNaN(cur.Rotation.w), $"NaN bei t = {t:0.00}");
                prev = cur;
            }
        }

        [Test]
        public void OhnePfad_GeradeLinieAlsFallback()
        {
            foreach (var corners in new List<IList<Vector3>> { null, new List<Vector3>(), new List<Vector3> { Portal } })
            {
                var path = new IntroCutscenePath(corners, Portal, NexusPos);
                Assert.IsTrue(path.UsedFallback);
                Assert.AreEqual(70f, path.Length, 0.1f);
                Assert.AreEqual(0f, path.PointAt(35f).z, 1e-3f, "gerade Linie");
                var start = path.Evaluate(IntroCutscenePath.FadeInEnd);
                Assert.Less(start.Position.x, Portal.x, "hinter dem Portal");
                var end = path.Evaluate(IntroCutscenePath.Shot3End);
                Assert.Greater(end.Position.x, NexusPos.x, "hinter dem Nexus");
                Assert.Less(end.Forward.x, -0.7f, "Blick zurück Richtung Portal");
                AssertContinuous(path, float.NaN);
            }
        }

        [Test]
        public void KurzerOderEntarteterPfad_OhneNaN()
        {
            var shortPath = new IntroCutscenePath(null, Portal, Portal + new Vector3(8f, 0f, 0f));
            AssertContinuous(shortPath, float.NaN);
            var same = new IntroCutscenePath(null, Portal, Portal);
            var pose = same.Evaluate(6f);
            Assert.IsFalse(float.IsNaN(pose.Position.x) || float.IsNaN(pose.Rotation.w));
        }

        [Test]
        public void Rueckblende_GleitetWeichZurSpielkamera()
        {
            var a = new CutscenePose(new Vector3(30f, 6f, 0f), Quaternion.Euler(20f, -90f, 0f));
            var b = new CutscenePose(new Vector3(0f, 15f, -8f), Quaternion.Euler(60f, 0f, 0f));
            Assert.AreEqual(a.Position, IntroCutscenePath.Blend(a, b, 0f, 3f).Position);
            Assert.Less(Vector3.Distance(b.Position, IntroCutscenePath.Blend(a, b, 1f, 3f).Position), 1e-4f);
            Assert.Less(Quaternion.Angle(b.Rotation, IntroCutscenePath.Blend(a, b, 1f).Rotation), 0.01f);
            // Ease-in-out: am Anfang langsam
            float early = Vector3.Distance(a.Position, IntroCutscenePath.Blend(a, b, 0.05f).Position);
            float mid = Vector3.Distance(IntroCutscenePath.Blend(a, b, 0.45f).Position, IntroCutscenePath.Blend(a, b, 0.5f).Position);
            Assert.Less(early, mid);
        }

        [Test]
        public void Kinobalken_UndTitel_Zeitkurven()
        {
            Assert.AreEqual(0f, IntroCutscenePath.BarsAt(0f, 11f, 1f), 1e-4f);
            Assert.AreEqual(1f, IntroCutscenePath.BarsAt(0.5f, 11f, 1f), 1e-4f);
            Assert.AreEqual(1f, IntroCutscenePath.BarsAt(10f, 11f, 1f), 1e-4f);
            Assert.AreEqual(0f, IntroCutscenePath.BarsAt(12f, 11f, 1f), 1e-4f);
            Assert.AreEqual(0f, IntroCutscenePath.TitleAlpha(5f), 1e-4f);
            Assert.AreEqual(1f, IntroCutscenePath.TitleAlpha(10f), 1e-4f);
            Assert.AreEqual(0f, IntroCutscenePath.TitleAlpha(11.5f), 1e-4f);
            Assert.AreEqual(1f, IntroCutscenePath.CutFadeAt(0.5f), 1e-4f);
            Assert.AreEqual(0f, IntroCutscenePath.CutFadeAt(1f), 1e-4f);
        }
    }
}
