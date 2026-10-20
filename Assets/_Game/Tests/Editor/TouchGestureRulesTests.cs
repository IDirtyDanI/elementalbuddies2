using NUnit.Framework;
using UnityEngine;

namespace ElementalBuddies.Tests
{
    // Mobil-Version (Paket B): ein Finger in der Welt-Zone ist Tippen (Auswahl / Geist versetzen), Ziehen (Geist folgt)
    // oder langes Drücken (Ping). Schwellen: Weg TapSlop (Pixel), Zeit LongPress (s).
    public class TouchGestureRulesTests
    {
        private const float Slop = 20f;
        private const float Long = TouchGesture.LongPressSeconds;

        [Test]
        public void Kurz_und_ruhig_ist_Tippen()
        {
            var g = TouchGesture.Begin(new Vector2(100f, 100f), 1f);
            Assert.AreEqual(TouchGestureEvent.None, g.Update(new Vector2(105f, 103f), 1.1f, Slop, Long));
            Assert.AreEqual(TouchGestureEvent.Tap, g.End(new Vector2(108f, 104f), 1.2f, Slop, Long));
            Assert.IsFalse(g.Active);
        }

        [Test]
        public void Weg_ueber_der_Schwelle_ist_Ziehen_und_kein_Tippen()
        {
            var g = TouchGesture.Begin(Vector2.zero, 0f);
            Assert.AreEqual(TouchGestureEvent.DragStarted, g.Update(new Vector2(0f, Slop + 1f), 0.1f, Slop, Long));
            Assert.AreEqual(TouchGestureState.Drag, g.State);
            Assert.AreEqual(TouchGestureEvent.None, g.Update(new Vector2(50f, 50f), 0.2f, Slop, Long), "Ziehen meldet sich nur einmal");
            Assert.AreEqual(TouchGestureEvent.None, g.End(new Vector2(0f, 0f), 0.3f, Slop, Long), "zurück zum Start bleibt Ziehen");
        }

        [Test]
        public void Ziehen_schliesst_langes_Druecken_aus()
        {
            var g = TouchGesture.Begin(Vector2.zero, 0f);
            g.Update(new Vector2(Slop * 2f, 0f), 0.1f, Slop, Long);
            Assert.AreEqual(TouchGestureEvent.None, g.Update(new Vector2(Slop * 2f, 0f), 2f, Slop, Long));
            Assert.AreEqual(TouchGestureState.Drag, g.State);
        }

        [Test]
        public void Halten_ohne_Bewegung_ist_langes_Druecken_einmalig()
        {
            var g = TouchGesture.Begin(Vector2.zero, 10f);
            Assert.AreEqual(TouchGestureEvent.None, g.Update(new Vector2(3f, 3f), 10f + Long - 0.01f, Slop, Long));
            Assert.AreEqual(TouchGestureEvent.LongPress, g.Update(new Vector2(3f, 3f), 10f + Long, Slop, Long));
            Assert.AreEqual(TouchGestureEvent.None, g.Update(new Vector2(3f, 3f), 11f, Slop, Long));
            Assert.AreEqual(TouchGestureEvent.None, g.Update(new Vector2(200f, 3f), 11.1f, Slop, Long), "danach kein Ziehen mehr");
            Assert.AreEqual(TouchGestureEvent.None, g.End(new Vector2(3f, 3f), 11.2f, Slop, Long), "nach dem Ping kein Tippen");
        }

        [Test]
        public void Verpasstes_langes_Druecken_wird_beim_Loslassen_nachgeholt()
        {
            var g = TouchGesture.Begin(Vector2.zero, 0f);
            Assert.AreEqual(TouchGestureEvent.LongPress, g.End(Vector2.zero, Long + 0.2f, Slop, Long));
        }

        [Test]
        public void Schwelle_genau_erreicht_gilt_noch_als_ruhig()
        {
            var g = TouchGesture.Begin(Vector2.zero, 0f);
            Assert.AreEqual(TouchGestureEvent.None, g.Update(new Vector2(Slop, 0f), 0.1f, Slop, Long));
            Assert.AreEqual(TouchGestureEvent.Tap, g.End(new Vector2(Slop, 0f), 0.2f, Slop, Long));
        }

        [Test]
        public void Abbruch_durch_zweiten_Finger()
        {
            var g = TouchGesture.Begin(Vector2.zero, 0f);
            g.Cancel();
            Assert.IsFalse(g.Active);
            Assert.AreEqual(TouchGestureEvent.None, g.Update(new Vector2(100f, 0f), 1f, Slop, Long));
            Assert.AreEqual(TouchGestureEvent.None, g.End(Vector2.zero, 1f, Slop, Long));
        }
    }
}
