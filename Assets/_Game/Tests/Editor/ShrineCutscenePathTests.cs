using NUnit.Framework;
using UnityEngine;

namespace ElementalBuddies.Tests
{
    // Schrein-Film und allgemeine Blick-Fahrt (wiki: Design/Dimensionsriss-Plan, Abschnitt A): Bahn-Mathematik
    public class ShrineCutscenePathTests
    {
        // Spielkamera über der Figur (Versatz (0, 15, −8), Blick schräg nach unten), Schrein 40 m entfernt
        private static readonly Vector3 Player = new Vector3(5f, 0f, -3f);
        private static readonly Vector3 ShrinePos = new Vector3(-30f, 0.4f, 18f);

        private static CutscenePose GamePose(Vector3 player)
        {
            Vector3 pos = player + new Vector3(0f, 15f, -8f);
            return new CutscenePose(pos, Quaternion.LookRotation(player - pos, Vector3.up)) { Fov = 60f };
        }

        private static FocusShotPath ShrinePath(float sign = 1f)
        {
            var start = GamePose(Player);
            return ShrineCutscenePath.Create(start, ShrinePos, ShrineCutscenePath.ApproachAngle(start, ShrinePos), sign);
        }

        private static float FlatAngle(Vector3 from, Vector3 to) => Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg;

        [Test]
        public void Zeitplan_IstFuenfSekunden()
        {
            Assert.AreEqual(5f, ShrineCutscenePath.Duration, 1e-5f);
            Assert.Less(ShrineCutscenePath.BarsIn, ShrineCutscenePath.FlyEnd);
            Assert.Less(ShrineCutscenePath.FlyEnd, ShrineCutscenePath.ArcEnd);
            Assert.AreEqual(ShrineCutscenePath.Duration, ShrineCutscenePath.ArcEnd + ShrineCutscenePath.ReturnDuration, 1e-5f);
            var path = ShrinePath();
            Assert.AreEqual(ShrineCutscenePath.Duration, path.Duration, 1e-5f);
            // Titel steht während des Bogens und ist zur Rückblende weg; der Ton fällt in den Anflug
            Assert.GreaterOrEqual(ShrineCutscenePath.TitleIn, ShrineCutscenePath.BarsIn);
            Assert.LessOrEqual(ShrineCutscenePath.TitleOut, ShrineCutscenePath.ArcEnd + 1e-5f);
            Assert.Less(ShrineCutscenePath.SoundTime, ShrineCutscenePath.FlyEnd);
        }

        [Test]
        public void Beginnt_AnDerSpielkamera_UndLoestSichLangsam()
        {
            var path = ShrinePath();
            var start = GamePose(Player);
            var p0 = path.Evaluate(0f);
            Assert.Less(Vector3.Distance(start.Position, p0.Position), 1e-3f);
            Assert.Less(Quaternion.Angle(start.Rotation, p0.Rotation), 0.05f);
            // Während die Balken einfahren, ist erst ein kleiner Teil des Wegs geflogen
            float w = path.FlyWeight(ShrineCutscenePath.BarsIn);
            Assert.Greater(w, 0f);
            Assert.Less(w, 0.1f);
            Assert.AreEqual(1f, path.FlyWeight(ShrineCutscenePath.FlyEnd), 1e-5f);
        }

        [Test]
        public void Bahn_IstStetig_AuchAnDenAbschnittsgrenzen()
        {
            foreach (float sign in new[] { 1f, -1f })
            {
                var path = ShrinePath(sign);
                var game = GamePose(Player + new Vector3(1f, 0f, 0.5f));
                const float dt = 1f / 120f;
                var prev = path.PoseAt(0f, game);
                float prevStep = 0f;
                for (float t = dt; t <= path.Duration + 1e-4f; t += dt)
                {
                    var cur = path.PoseAt(t, game);
                    float step = Vector3.Distance(prev.Position, cur.Position);
                    // Kein Sprung (schnellster Teil ist die Rückblende: ≈ 43 m in 0,8 s, Spitze ≈ 80 m/s) und kein Drehsprung
                    Assert.Less(step, 0.9f, $"Positionssprung bei t={t:0.000}");
                    Assert.Less(Quaternion.Angle(prev.Rotation, cur.Rotation), 3f, $"Drehsprung bei t={t:0.000}");
                    // Kein Knick: die Schrittweite ändert sich von Bild zu Bild nur wenig (auch bei 0,6 / 2,0 / 4,2 s)
                    Assert.Less(Mathf.Abs(step - prevStep), 0.03f, $"Geschwindigkeitssprung bei t={t:0.000}");
                    prev = cur;
                    prevStep = step;
                }
            }
        }

        [Test]
        public void Kamera_BleibtUeberDemBoden()
        {
            foreach (float height in new[] { ShrineCutscenePath.DefaultHeight, 0.2f })
            {
                var start = GamePose(Player);
                var path = ShrineCutscenePath.Create(start, ShrinePos, 30f, 1f, ShrineCutscenePath.DefaultRadius, height);
                float ground = Mathf.Max(Player.y, ShrinePos.y);
                for (float t = 0f; t <= path.Duration; t += 0.02f)
                {
                    var pose = path.PoseAt(t, start);
                    Assert.GreaterOrEqual(pose.Position.y, ground + path.MinClearance - 1e-3f, $"zu tief bei t={t:0.00}");
                }
            }
        }

        [Test]
        public void Bogen_BlicktAufDenSchrein_UndUmfasstVierzigGrad()
        {
            foreach (float sign in new[] { 1f, -1f })
            {
                var path = ShrinePath(sign);
                Vector3 look = ShrinePos + Vector3.up * ShrineCutscenePath.LookHeight;
                for (float t = ShrineCutscenePath.FlyEnd; t <= ShrineCutscenePath.ArcEnd + 1e-4f; t += 0.05f)
                {
                    var pose = path.Evaluate(t);
                    Assert.Less(Vector3.Angle(pose.Forward, look - pose.Position), 0.1f, $"blickt nicht zum Schrein bei t={t:0.00}");
                    Vector3 flat = pose.Position - ShrinePos; flat.y = 0f;
                    Assert.AreEqual(ShrineCutscenePath.DefaultRadius, flat.magnitude, 1e-2f);
                    Assert.AreEqual(ShrinePos.y + ShrineCutscenePath.DefaultHeight, pose.Position.y, 1e-3f);
                }
                float a0 = FlatAngle(ShrinePos, path.Evaluate(ShrineCutscenePath.FlyEnd).Position);
                float a1 = FlatAngle(ShrinePos, path.Evaluate(ShrineCutscenePath.ArcEnd).Position);
                Assert.AreEqual(sign * ShrineCutscenePath.SweepDegrees, Mathf.DeltaAngle(a0, a1), 0.05f);
                // Bogen liegt mittig um den Anflugwinkel
                float mid = ShrineCutscenePath.ApproachAngle(GamePose(Player), ShrinePos);
                Assert.AreEqual(-sign * ShrineCutscenePath.SweepDegrees * 0.5f, Mathf.DeltaAngle(mid, a0), 0.05f);
                // Der Bogen läuft aus (die Rückblende beginnt ohne Ruck)
                float tail = Vector3.Distance(path.Evaluate(ShrineCutscenePath.ArcEnd - 0.02f).Position, path.Evaluate(ShrineCutscenePath.ArcEnd).Position);
                Assert.Less(tail, 0.01f);
            }
        }

        [Test]
        public void Rueckblende_EndetAufDerSpielLage()
        {
            var path = ShrinePath();
            // Die Figur hat sich inzwischen bewegt: Ziel ist die dann gültige Spiel-Lage
            var game = GamePose(Player + new Vector3(2f, 0f, -1f));
            var end = path.PoseAt(path.Duration, game);
            Assert.Less(Vector3.Distance(game.Position, end.Position), 1e-3f);
            Assert.Less(Quaternion.Angle(game.Rotation, end.Rotation), 0.05f);
            Assert.AreEqual(game.Fov, end.Fov, 1e-4f);
            var later = path.PoseAt(path.Duration + 3f, game);
            Assert.Less(Vector3.Distance(game.Position, later.Position), 1e-3f);
            // Beginn der Rückblende = Ende des Bogens
            var a = path.PoseAt(ShrineCutscenePath.ArcEnd, game);
            var b = path.Evaluate(ShrineCutscenePath.ArcEnd);
            Assert.Less(Vector3.Distance(a.Position, b.Position), 1e-4f);
        }

        [Test]
        public void Anflug_IstHoch_UndWeiterWegHoeher()
        {
            Assert.AreEqual(0f, ShrineCutscenePath.ApexFor(Vector3.zero, Vector3.zero), 1e-5f);
            Assert.Less(ShrineCutscenePath.ApexFor(Vector3.zero, new Vector3(10f, 0f, 0f)), ShrineCutscenePath.ApexFor(Vector3.zero, new Vector3(30f, 0f, 0f)));
            Assert.AreEqual(6f, ShrineCutscenePath.ApexFor(Vector3.zero, new Vector3(500f, 0f, 0f)), 1e-5f);
            // Mitte des Flugs liegt über der geraden Verbindung
            var path = ShrinePath();
            float tMid = 0f;
            for (float t = 0f; t < ShrineCutscenePath.FlyEnd; t += 0.005f)
                if (path.FlyWeight(t) >= 0.5f) { tMid = t; break; }
            Vector3 straight = Vector3.Lerp(path.Start.Position, path.OrbitPosition(tMid), path.FlyWeight(tMid));
            Assert.Greater(path.Evaluate(tMid).Position.y, straight.y + path.Apex * 0.9f);
        }

        [Test]
        public void Entartet_SchreinUnterDerKamera_KeinNaN()
        {
            var start = new CutscenePose(ShrinePos + Vector3.up * 15f, Quaternion.LookRotation(Vector3.down, Vector3.forward));
            float angle = ShrineCutscenePath.ApproachAngle(start, ShrinePos);
            Assert.IsFalse(float.IsNaN(angle));
            var path = ShrineCutscenePath.Create(start, ShrinePos, angle);
            for (float t = 0f; t <= path.Duration; t += 0.1f)
            {
                var pose = path.PoseAt(t, start);
                Assert.IsFalse(float.IsNaN(pose.Position.x + pose.Position.y + pose.Position.z), $"NaN bei t={t:0.0}");
                Assert.IsFalse(float.IsNaN(pose.Rotation.x + pose.Rotation.w), $"NaN bei t={t:0.0}");
            }
        }

        // ---------------- Allgemeine Blick-Fahrt (Cutscene.PlayFocusShot) ----------------

        [Test]
        public void BlickFahrt_KommtAmVersatzAn_HaeltUndKehrtZurueck()
        {
            var start = GamePose(Player);
            Vector3 target = new Vector3(-44f, 0f, 2f);
            Vector3 offset = new Vector3(6f, 8f, -9f);
            var path = FocusShotPath.FromOffset(start, target, offset);
            path.LookHeight = 2f;
            Assert.AreEqual(4f, path.Duration, 1e-5f); // 1,6 s Flug + 1,6 s Halten + 0,8 s Rückblende

            Assert.Less(Vector3.Distance(start.Position, path.Evaluate(0f).Position), 1e-3f);
            var arrive = path.Evaluate(path.FlyEnd);
            Assert.Less(Vector3.Distance(target + offset, arrive.Position), 1e-3f);
            Vector3 look = target + Vector3.up * 2f;
            Assert.Less(Vector3.Angle(arrive.Forward, look - arrive.Position), 0.1f);
            // Ohne Bogen steht das Bild beim Halten
            var hold = path.Evaluate(path.HoldEnd);
            Assert.Less(Vector3.Distance(arrive.Position, hold.Position), 1e-4f);
            Assert.Less(Quaternion.Angle(arrive.Rotation, hold.Rotation), 0.01f);
            // Rückkehr auf die Spiel-Lage
            var end = path.PoseAt(path.Duration, start);
            Assert.Less(Vector3.Distance(start.Position, end.Position), 1e-3f);
            Assert.Less(Quaternion.Angle(start.Rotation, end.Rotation), 0.05f);
        }

        [Test]
        public void BlickFahrt_Blickwinkel_WirdUeberblendet()
        {
            var start = GamePose(Player);
            var path = FocusShotPath.FromOffset(start, ShrinePos, new Vector3(0f, 8f, -11f));
            Assert.AreEqual(0f, path.Evaluate(1f).Fov, 1e-5f); // ohne eigenen Blickwinkel: Spielkamera unverändert
            path.Fov = 40f;
            Assert.AreEqual(60f, path.Evaluate(0f).Fov, 1e-3f);
            Assert.AreEqual(40f, path.Evaluate(path.FlyEnd).Fov, 1e-3f);
            float mid = path.Evaluate(path.FlyEnd * 0.7f).Fov;
            Assert.Greater(mid, 40f);
            Assert.Less(mid, 60f);
        }

        [Test]
        public void BlickFahrt_SenkrechterVersatz_NimmtErsatzwinkel()
        {
            Assert.AreEqual(180f, FocusShotPath.AngleOf(Vector3.up, 180f), 1e-5f);
            Assert.AreEqual(90f, FocusShotPath.AngleOf(Vector3.right, 0f), 1e-4f);
            Assert.AreEqual(0f, FocusShotPath.AngleOf(Vector3.forward, 77f), 1e-4f);
            var path = FocusShotPath.FromOffset(GamePose(Player), ShrinePos, Vector3.up * 12f);
            Assert.AreEqual(0.5f, path.Radius, 1e-5f);
            Assert.IsFalse(float.IsNaN(path.Evaluate(path.FlyEnd).Rotation.w));
        }

        // ---------------- Gemeinsame Zeitkurven ----------------

        [Test]
        public void Zeitkurven_EntsprechenDemIntroFilm()
        {
            for (float t = 0f; t <= IntroCutscenePath.Duration; t += 0.05f)
            {
                Assert.AreEqual(IntroCutscenePath.BarsAt(t, IntroCutscenePath.Shot3End, IntroCutscenePath.ReturnDuration),
                    CutsceneTiming.Bars(t, IntroCutscenePath.FadeInEnd, IntroCutscenePath.Shot3End, IntroCutscenePath.ReturnDuration), 1e-5f);
                Assert.AreEqual(IntroCutscenePath.TitleAlpha(t),
                    CutsceneTiming.TitleAlpha(t, IntroCutscenePath.TitleIn, IntroCutscenePath.TitleOut), 1e-5f);
            }
            // Überspringen bei 3 s: Balken fahren in 0,4 s aus
            Assert.AreEqual(1f, CutsceneTiming.Bars(3f, 0.5f, 3f, 0.4f), 1e-5f);
            Assert.AreEqual(0f, CutsceneTiming.Bars(3.4f, 0.5f, 3f, 0.4f), 1e-5f);
        }

        [Test]
        public void SchreinFilm_BalkenUndTitel()
        {
            float inf = float.PositiveInfinity;
            Assert.AreEqual(0f, CutsceneTiming.Bars(0f, ShrineCutscenePath.BarsIn, inf, 1f), 1e-5f);
            Assert.AreEqual(1f, CutsceneTiming.Bars(ShrineCutscenePath.BarsIn, ShrineCutscenePath.BarsIn, inf, 1f), 1e-5f);
            Assert.AreEqual(0f, CutsceneTiming.Bars(ShrineCutscenePath.Duration, ShrineCutscenePath.BarsIn, ShrineCutscenePath.ArcEnd, ShrineCutscenePath.ReturnDuration), 1e-5f);
            Assert.AreEqual(0f, CutsceneTiming.TitleAlpha(1f, ShrineCutscenePath.TitleIn, ShrineCutscenePath.TitleOut), 1e-5f);
            Assert.AreEqual(1f, CutsceneTiming.TitleAlpha(3f, ShrineCutscenePath.TitleIn, ShrineCutscenePath.TitleOut), 1e-5f);
            Assert.AreEqual(0f, CutsceneTiming.TitleAlpha(ShrineCutscenePath.ArcEnd, ShrineCutscenePath.TitleIn, ShrineCutscenePath.TitleOut), 1e-5f);
        }
    }
}
