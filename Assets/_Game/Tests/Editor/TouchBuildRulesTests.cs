using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace ElementalBuddies.Tests
{
    // Mobil-Version (Paket B): Geist-Platzierung aus dem Bildschirmpunkt (Versatz nach oben), Raster, Suchreihenfolge des
    // Start-Bauplatzes, vergrößerte Buddy-Trefferzone (nächster im Radius) und ✓/✗-Knöpfe im sicheren Bereich.
    public class TouchBuildRulesTests
    {
        private static readonly Rect Screen = new Rect(0f, 0f, 2000f, 1000f);

        [Test]
        public void Geist_liegt_ueber_dem_Finger()
        {
            Vector2 p = TouchBuildRules.GhostScreenPoint(new Vector2(500f, 300f), 150f, Screen);
            Assert.AreEqual(new Vector2(500f, 450f), p);
        }

        [Test]
        public void Geist_bleibt_im_Bildschirm()
        {
            Assert.AreEqual(1000f, TouchBuildRules.GhostScreenPoint(new Vector2(500f, 950f), 150f, Screen).y);
            Assert.AreEqual(0f, TouchBuildRules.GhostScreenPoint(new Vector2(-30f, 10f), 0f, Screen).x);
            Assert.AreEqual(10f, TouchBuildRules.GhostScreenPoint(new Vector2(5f, 10f), -50f, Screen).y, "negativer Versatz zählt nicht");
        }

        [Test]
        public void Raster_wie_am_PC()
        {
            Assert.AreEqual(new Vector3(3f, 0.25f, -2f), TouchBuildRules.SnapToGrid(new Vector3(2.6f, 0.25f, -2.4f)));
            Assert.AreEqual(Mathf.Round(4.5f), TouchBuildRules.SnapToGrid(new Vector3(4.5f, 0f, 0f)).x);
        }

        [Test]
        public void Suchreihenfolge_von_innen_nach_aussen()
        {
            var cells = new List<Vector2Int>();
            int n = TouchBuildRules.SearchOrder(2, cells);
            Assert.AreEqual(25, n);
            Assert.AreEqual(Vector2Int.zero, cells[0]);
            for (int i = 1; i < cells.Count; i++)
                Assert.LessOrEqual(cells[i - 1].sqrMagnitude, cells[i].sqrMagnitude, "Abstand steigt nie");
            Assert.AreEqual(new Vector2Int(0, 1), cells[1], "Gleichstand: zuerst größeres y");
            Assert.AreEqual(0, TouchBuildRules.SearchOrder(-1, cells));
        }

        [Test]
        public void Naechster_Buddy_im_Radius()
        {
            var buddies = new List<Vector3> { new Vector3(5f, 0f, 0f), new Vector3(0.6f, 2f, 0f), new Vector3(0.3f, 0f, 0.3f) };
            Assert.AreEqual(2, TouchBuildRules.PickNearest(Vector3.zero, buddies, TouchBuildRules.PickRadius));
            Assert.AreEqual(1, TouchBuildRules.PickNearest(new Vector3(0.7f, 0f, 0f), buddies, TouchBuildRules.PickRadius), "Höhe zählt nicht");
        }

        [Test]
        public void Ausserhalb_des_Radius_kein_Buddy()
        {
            var buddies = new List<Vector3> { new Vector3(0.81f, 0f, 0f) };
            Assert.AreEqual(-1, TouchBuildRules.PickNearest(Vector3.zero, buddies, 0.8f));
            Assert.AreEqual(-1, TouchBuildRules.PickNearest(Vector3.zero, new List<Vector3>(), 0.8f));
            Assert.AreEqual(-1, TouchBuildRules.PickNearest(Vector3.zero, null, 0.8f));
        }

        [Test]
        public void Knoepfe_ueber_dem_Geist_nebeneinander()
        {
            TouchBuildRules.ConfirmButtons(new Vector2(1000f, 400f), 100f, 80f, 20f, Screen, out var ok, out var no);
            Assert.AreEqual(500f, ok.y);
            Assert.AreEqual(500f, no.y);
            Assert.Greater(ok.x, no.x, "✓ rechts, ✗ links");
            Assert.AreEqual(100f, ok.x - no.x, 1e-3f, "Mittenabstand = Größe + Lücke");
        }

        [Test]
        public void Knoepfe_unter_dem_Geist_wenn_oben_kein_Platz()
        {
            TouchBuildRules.ConfirmButtons(new Vector2(1000f, 950f), 100f, 80f, 20f, Screen, out var ok, out _);
            Assert.AreEqual(850f, ok.y);
        }

        [Test]
        public void Knoepfe_im_sicheren_Bereich()
        {
            var safe = new Rect(100f, 50f, 1800f, 900f); // Notch links, Home-Indikator unten
            TouchBuildRules.ConfirmButtons(new Vector2(0f, 0f), 100f, 80f, 20f, safe, out var ok, out var no);
            Assert.GreaterOrEqual(no.x - 40f, safe.xMin - 1e-3f);
            Assert.GreaterOrEqual(ok.y - 40f, safe.yMin - 1e-3f);
            Assert.AreEqual(100f, ok.x - no.x, 1e-3f, "Abstand bleibt");
            TouchBuildRules.ConfirmButtons(new Vector2(5000f, 400f), 100f, 80f, 20f, safe, out ok, out _);
            Assert.LessOrEqual(ok.x + 40f, safe.xMax + 1e-3f);
        }
    }
}
