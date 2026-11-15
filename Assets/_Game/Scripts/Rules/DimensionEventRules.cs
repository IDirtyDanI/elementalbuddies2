using UnityEngine;

namespace ElementalBuddies
{
    // Dimensionsriss (wiki: Design/Dimensionsriss-Plan, Abschnitt B) – reine Rechenregeln, damit EditMode-Tests sie ohne
    // Szene prüfen können (Test-Assembly sieht nur ElementalBuddies.Rules):
    //  - Termin: Welle 20, danach alle 5 Wellen
    //  - Arena-Lage (weit neben der Karte) und „in welcher Dimension liegt ein Punkt“
    //  - Nexus-Verfall: Schonfrist, Takt, Schaden je Takt (DrainClock)
    //  - Boss-Leben (eigene Formel – nur Champions greifen an) und gedämpfter Boss-Schaden
    //  - Bitmaske „wer ist in der Dimension“ (Client-Ids), Eintritts-/Rückkehr-Positionen
    public static class DimensionEventRules
    {
        // ---------------- Termin ----------------

        public const int FirstWave = 20;
        public const int Every = 5;

        public static bool IsEventWave(int wave, int first = FirstWave, int every = Every)
        {
            if (first <= 0 || wave < first) return false;
            return (wave - first) % Mathf.Max(1, every) == 0;
        }

        // 0 = erstes Event (Welle first), 1 = zweites …; −1 = keine Event-Welle
        public static int EventIndex(int wave, int first = FirstWave, int every = Every)
        {
            return IsEventWave(wave, first, every) ? (wave - first) / Mathf.Max(1, every) : -1;
        }

        // Erste Event-Welle ≥ fromWave (−1 = Events aus)
        public static int NextEventWave(int fromWave, int first = FirstWave, int every = Every)
        {
            if (first <= 0) return -1;
            if (fromWave <= first) return first;
            int e = Mathf.Max(1, every);
            int k = (fromWave - first + e - 1) / e;
            return first + k * e;
        }

        // ---------------- Arena ----------------

        // Mittelpunkt der Arena (auf jedem Rechner lokal angelegt) – weit außerhalb jeder Karte (Karten: |x|,|z| < 80)
        public static readonly Vector3 ArenaCenter = new Vector3(400f, 0f, 0f);
        // Alles mit x > SplitX gehört zur Dimension
        public const float SplitX = 200f;
        public const float FloorRadius = 18f;      // Bodenscheibe der Platzhalter-Arena
        public const float WalkRadius = 16.5f;     // Innenkante der Begrenzung
        public const float MinimapRadius = 22f;    // Kartenausschnitt der Minimap in der Arena
        public const float LeashRadius = 40f;      // Aggro-Radius des Bosses (deckt die ganze Arena)

        public static bool InArena(Vector3 worldPos) => worldPos.x > SplitX;
        public static bool SameDimension(Vector3 a, Vector3 b) => InArena(a) == InArena(b);
        // Liegt der waagerechte Versatz zur Arenamitte hinter dem Mauerring?
        public static bool OutsideArenaRing(Vector3 offsetFromCenter, float keepRadius)
        {
            offsetFromCenter.y = 0f;
            return offsetFromCenter.sqrMagnitude > keepRadius * keepRadius;
        }

        // ---------------- Nexus-Verfall ----------------

        public const float DrainGrace = 30f;       // Schonfrist ab Wellenstart (s)
        // neue Schonfrist, wenn der letzte Spieler die Dimension verlässt (Tod): Wiederbelebung (solo 10 s) + Weg zurück
        // zum Portal – mit 12 s kostete jeder Tod zusätzliche Takte (#561)
        public const float DrainRegrace = 25f;
        public const float DrainInterval = 5f;     // Takt (s)
        public const float DrainFraction = 0.03f;  // Anteil der Nexus-Max-LP je Takt
        public const float ReturnWindow = 20f;     // nach dem Boss-Tod: so lange bleibt das Rückportal offen, dann holt der Server alle zurück
        public const float EnterRadius = 2.5f;     // Auslöse-Radius um Portal-Spawnpunkt bzw. Rückportal

        // Betreten nur mit Absicht (#569): so lange (s) muss die Figur ununterbrochen im Portal-Kreis stehen
        public const float EnterHold = 1.2f;
        public const float ReturnHold = 0.8f;      // dasselbe vor dem Rückportal (kein Gegnerstrom → kürzer)

        // Halte-Zähler des Portal-Kreises (Besitzer): füllt sich, solange die Figur im Kreis steht; Verlassen setzt zurück.
        public struct HoldTimer
        {
            public float Elapsed;
            public bool Fired;

            public void Reset() { Elapsed = 0f; Fired = false; }

            // Einen Schritt weiter. true = genau in diesem Schritt ist die Haltezeit erreicht (einmal je Aufenthalt;
            // nach Reset() – z. B. abgelehnte Anfrage – beginnt das Halten von vorn).
            public bool Step(bool inside, float deltaTime, float required)
            {
                if (!inside)
                {
                    Reset();
                    return false;
                }
                if (Fired) return false;
                Elapsed += Mathf.Max(0f, deltaTime);
                if (Elapsed < Mathf.Max(0f, required)) return false;
                Fired = true;
                return true;
            }

            // 0..1 (1 = Haltezeit erreicht)
            public float Progress(float required) =>
                required <= 0f ? (Elapsed > 0f || Fired ? 1f : 0f) : Mathf.Clamp01(Elapsed / required);
        }

        public static float DrainDamage(float nexusMaxHp, float fraction = DrainFraction) =>
            Mathf.Max(0f, nexusMaxHp) * Mathf.Max(0f, fraction);

        // Takte bis zum Fall eines vollen Nexus (für Texte/Tests)
        public static int DrainTicksToKill(float fraction = DrainFraction) =>
            fraction <= 0f ? int.MaxValue : Mathf.CeilToInt(1f / fraction - 1e-4f);

        public enum DrainPhase : byte
        {
            Off,        // kein Event / Boss tot
            Grace,      // Schonfrist läuft (Countdown bis NextTick)
            Draining,   // der Nexus verliert Leben (nächster Takt bei NextTick)
            Held,       // ein lebender Spieler ist in der Dimension – kein Verfall
        }

        // Zustandsautomat des Verfalls (Server). Zeiten in Sekunden einer beliebigen, monoton wachsenden Uhr.
        public struct DrainClock
        {
            public DrainPhase Phase;
            public double NextTick;

            public static DrainClock Start(double now, float grace = DrainGrace)
            {
                return new DrainClock { Phase = DrainPhase.Grace, NextTick = now + Mathf.Max(0f, grace) };
            }

            // Einen Schritt weiter; Rückgabe = Anzahl fälliger Takte (0 oder 1 – nach einem Hänger wird nicht nachgeholt)
            public int Step(double now, bool bossAlive, bool anyAliveInside, float interval = DrainInterval, float regrace = DrainRegrace)
            {
                if (!bossAlive || Phase == DrainPhase.Off)
                {
                    Phase = DrainPhase.Off;
                    return 0;
                }
                if (anyAliveInside)
                {
                    Phase = DrainPhase.Held;
                    return 0;
                }
                if (Phase == DrainPhase.Held)
                {
                    // letzter Spieler hat die Dimension verlassen: neue Schonfrist (nie kürzer als der Rest der alten)
                    Phase = DrainPhase.Grace;
                    NextTick = System.Math.Max(NextTick, now + Mathf.Max(0f, regrace));
                    return 0;
                }
                if (now < NextTick) return 0;
                Phase = DrainPhase.Draining;
                NextTick = now + Mathf.Max(0.1f, interval);
                return 1;
            }
        }

        // ---------------- Boss ----------------

        // Ziel-Leben des Bosses im ersten Event (Welle 20) für einen Spieler auf „Normal“, Belagerungsstufe 0.
        // Herleitung (Design/Champion-Balancing-2026-11): Grundangriff 1 Ziel ≈ 43–50 DPS ohne Karten; mit den üblichen
        // Schadens-/Tempo-Karten bis Welle 20 ≈ ×1,6 → ≈ 72; im Boss-Kampf (Warnflächen ausweichen, Nachsetzen) ≈ 75 %
        // Trefferzeit → ≈ 54; Element-Zauber (200 Mana zum Wellenstart) ≈ +10 % → ≈ 60 DPS. 60 × 68 s ≈ 4 000.
        // Nachgemessen (ChampionBench „single“, 2026-11-14, Rohwert ohne Rüstung – die Dimensions-Bosse haben keine):
        // Magier 48, Bogenschütze 44, Berserker 45, Schwertkämpfer 42 DPS → mit obigen Annahmen (×1,6 × 0,75 × 1,1 = ×1,32)
        // 55–63 DPS → 4 000 LP = 63 s (Magier) bis 72 s (Schwertkämpfer). Wert bestätigt (#556).
        // Echte Kämpfe (DimensionFightBench, 2026-11-14, Welle 20, 8 Champion-Karten: 3× Kampfeswut, 2× Kampfrhythmus,
        // Zähigkeit, Stiefel, Manabrunnen + Feuer-/Eis-Zauber, ausweichender Fahrer): Magier ≈ 120 DPS, Schwertkämpfer
        // ≈ 85–90 DPS → 4 000 LP fielen in 34 s (Magier) bzw. 44–49 s (Schwertkämpfer). Die Annahme ×1,32 war viel zu
        // niedrig (Karten ×1,75 Schaden, −20 % Abklingzeit, Zauber treffen fast immer). Mit 6 500 LP nachgemessen:
        // Magier 58 s, Schwertkämpfer 81 s (Seelenschnitter). Die Klassen liegen ×1,4 auseinander – ein Wert trifft nie
        // beide ins Fenster 60–75 s. 6 000 LP: schadensstarker Kartensatz ≈ 53 s / 75 s, üblicher (2× Kampfeswut) ≈ 62 s / 87 s.
        public const float TargetBossHp = 6000f;
        public const float HpPerExtraPlayer = 0.6f;
        public const float HpPerEvent = 0.15f;

        // Leben des Dimensions-Bosses: Ziel × Spielerzahl × Event-Nummer × Schwierigkeit × Belagerungsstufe
        public static float BossHp(float targetHp, int eventIndex, int players, float difficultyFactor = 1f, float siegeFactor = 1f,
            float perExtraPlayer = HpPerExtraPlayer, float perEvent = HpPerEvent)
        {
            float p = 1f + Mathf.Max(0f, perExtraPlayer) * Mathf.Max(0, players - 1);
            float e = Mathf.Pow(1f + Mathf.Max(0f, perEvent), Mathf.Max(0, eventIndex));
            return Mathf.Max(1f, targetHp) * p * e * Mathf.Max(0.01f, difficultyFactor) * Mathf.Max(0.01f, siegeFactor);
        }

        // Faktor auf Config.BaseHP, damit der Boss effektiv hp aushält (Rüstung herausgerechnet: Roh-Leben × (1 − Rüstung))
        public static float BossHpMultiplier(float baseHp, float armor, float hp)
        {
            return hp * (1f - Mathf.Clamp(armor, 0f, 0.9f)) / Mathf.Max(1f, baseHp);
        }

        // Anteil der Wellen-Schadensrampe, den der Dimensions-Boss übernimmt: In der Arena ist der Champion das einzige
        // Ziel (keine Buddies, die Treffer abfangen) – die volle Rampe (Welle 20: ×1,76) macht Fähigkeiten zu Zwei-Treffern.
        public const float DamageRampShare = 0.5f;

        public static float BossDamageMultiplier(float waveDamageMultiplier, float share = DamageRampShare)
        {
            if (waveDamageMultiplier <= 1f) return Mathf.Max(0f, waveDamageMultiplier);
            return 1f + (waveDamageMultiplier - 1f) * Mathf.Clamp01(share);
        }

        // ---------------- Regen mit mehreren Flächen (#570) ----------------

        // Mittelpunkte weiterer Regenflächen. first = erste Fläche (auf dem Ziel); others = Standorte anderer Spieler
        // (werden bevorzugt, wenn sie weit genug von schon gesetzten Flächen liegen); danach Zufallspunkte im Ring
        // [minOffset, maxOffset] um die erste Fläche. Zwei Flächen liegen nie näher als radius beieinander (Überlappung
        // der Kreise < 40 % der Fläche – „höchstens zur Hälfte“). rnd01 liefert Zufall in [0, 1).
        // Rückgabe: Anzahl gefüllter Einträge in result (≤ count, result[0] = first).
        public static int RainCenters(Vector3 first, Vector3[] others, int count, float radius, float minOffset, float maxOffset,
            System.Func<float> rnd01, Vector3[] result)
        {
            if (result == null || result.Length == 0 || count <= 0) return 0;
            count = Mathf.Min(count, result.Length);
            int n = 0;
            result[n++] = first;
            float minDist = Mathf.Max(0.01f, radius);
            if (others != null)
                for (int i = 0; i < others.Length && n < count; i++)
                {
                    Vector3 o = others[i];
                    o.y = first.y;
                    if (FarEnough(o, result, n, minDist)) result[n++] = o;
                }
            float lo = Mathf.Max(minDist, minOffset), hi = Mathf.Max(lo, maxOffset);
            int guard = 0;
            float baseAngle = rnd01 != null ? rnd01() * Mathf.PI * 2f : 0f;
            while (n < count && guard < 64)
            {
                // gleichmäßig verteilte Grundrichtung + Zufall: auch mit schlechtem Zufall finden sich Plätze
                float a = baseAngle + guard * 2.399963f + (rnd01 != null ? (rnd01() - 0.5f) * 0.6f : 0f);
                float d = Mathf.Lerp(lo, hi, rnd01 != null ? rnd01() : 0.5f);
                guard++;
                Vector3 c = first + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * d;
                if (FarEnough(c, result, n, minDist)) result[n++] = c;
            }
            return n;
        }

        private static bool FarEnough(Vector3 p, Vector3[] set, int n, float minDist)
        {
            for (int i = 0; i < n; i++)
            {
                Vector3 d = set[i] - p;
                d.y = 0f;
                if (d.sqrMagnitude < minDist * minDist) return false;
            }
            return true;
        }

        // ---------------- Wer ist drin (Bitmaske über Client-Ids, Bit = Id mod 64) ----------------

        public static int MaskBit(ulong clientId) => (int)(clientId % 64UL);

        public static ulong MaskSet(ulong mask, ulong clientId, bool inside)
        {
            ulong bit = 1UL << MaskBit(clientId);
            return inside ? mask | bit : mask & ~bit;
        }

        public static bool MaskHas(ulong mask, ulong clientId) => (mask & (1UL << MaskBit(clientId))) != 0UL;

        public static int MaskCount(ulong mask)
        {
            int n = 0;
            while (mask != 0UL) { n += (int)(mask & 1UL); mask >>= 1; }
            return n;
        }

        // ---------------- Positionen ----------------

        // Eintritt: Spieler nebeneinander (quer zur Blickrichtung des Eingangs), Slot 0 in der Mitte
        public static Vector3 EntryPosition(Vector3 entry, Vector3 forward, int slot, float spacing = 1.6f)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward.normalized);
            int s = Mathf.Clamp(slot, 0, 7);
            // 0, +1, −1, +2, −2 …
            float side = s == 0 ? 0f : ((s + 1) / 2) * (s % 2 == 1 ? 1f : -1f);
            return entry + right * side * spacing;
        }

        // Rückkehr: vom Portal-Spawnpunkt ein paar Meter Richtung Kartenmitte (nicht im Gegnerstrom), Slots seitlich versetzt
        public static Vector3 ReturnPosition(Vector3 portalSpawnPoint, Vector3 mapCenter, int slot, float inward = 5f, float spacing = 1.6f)
        {
            Vector3 dir = mapCenter - portalSpawnPoint;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
            dir.Normalize();
            Vector3 p = EntryPosition(portalSpawnPoint + dir * inward, dir, slot, spacing);
            p.y = portalSpawnPoint.y;
            return p;
        }

        // ---------------- Wurf (BossAbilityKind.Throw) ----------------

        // Zielpunkt eines Wurfs mit Vorhalt: Das Ziel lief in observed s von p0 nach p1. Vorgehalten wird die Strecke, die es
        // bei gleicher Laufrichtung bis zum Eintreffen des Geschosses (restliches Ausholen + Flugzeit) zurücklegt, × lead.
        // Die beobachtete Geschwindigkeit ist auf maxSpeed begrenzt (Blinzeln, Rückstoß).
        public static Vector3 ThrowAimPoint(Vector3 boss, Vector3 p0, Vector3 p1, float observed, float remainingWindup,
            float projectileSpeed, float lead, float maxSpeed)
        {
            if (observed <= 1e-4f || lead <= 0f) return p1;
            Vector3 v = (p1 - p0) / observed;
            v.y = 0f;
            if (v.magnitude > maxSpeed) v = v.normalized * maxSpeed;
            Vector3 flat = p1 - boss;
            flat.y = 0f;
            float time = Mathf.Max(0f, remainingWindup) + flat.magnitude / Mathf.Max(1f, projectileSpeed);
            return p1 + v * (time * Mathf.Clamp01(lead));
        }

        // Abstand eines Punkts zur Strecke a→b in der Bodenebene
        public static float FlatDistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector2 ap = new Vector2(p.x - a.x, p.z - a.z), ab = new Vector2(b.x - a.x, b.z - a.z);
            float len = ab.sqrMagnitude;
            float t = len > 1e-6f ? Mathf.Clamp01(Vector2.Dot(ap, ab) / len) : 0f;
            return (ap - ab * t).magnitude;
        }

        // Winkel der i-ten Linie eines Fächers (0 = Mitte, dann abwechselnd rechts/links: +s, −s, +2s, −2s …)
        public static float FanAngle(int index, float spread)
        {
            if (index <= 0) return 0f;
            int ring = (index + 1) / 2;
            return (index % 2 == 1 ? 1f : -1f) * ring * spread;
        }
    }
}
