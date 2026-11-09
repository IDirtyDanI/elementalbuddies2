using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Kamera-Lage (Position + Drehung) eines Film-Bilds
    public struct CutscenePose
    {
        public Vector3 Position;
        public Quaternion Rotation;

        public CutscenePose(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }

        public Vector3 Forward => Rotation * Vector3.forward;
    }

    // Reine Bahn-Mathematik des Intro-Films zur ersten Welle (wiki: Design/Intro-Cutscene). Ohne MonoBehaviour und ohne
    // Physik/NavMesh, damit EditMode-Tests sie prüfen können: Die Laufzeit (IntroCutscene) liefert die Ecken des
    // NavMesh-Pfads Portal → Nexus, hier wird daraus eine geglättete Bahn (Catmull-Rom + Glättung, nach Bogenlänge
    // parametrisiert) und die Kamera-Lage als Funktion der Filmzeit t.
    // Drehbuch: 0–0,5 s Einblendung (Spielkamera steht), 0,5–4 s Einstellung 1 (tief hinter dem Portal, leichtes
    // Heranfahren), 4–8,5 s Einstellung 2 (Aufstieg, Überflug entlang des Wegs), 8,5–11 s Einstellung 3 (Schwenk um den
    // Nexus, am Ende Blick vom Nexus den Weg hinunter), 11–12 s Rückblende zur Spielkamera (Blend()).
    // Die Einstellungen gehen über kurze Überblend-Fenster ineinander über; jede Einstellung ist für sich stetig, also
    // ist die ganze Bahn sprungfrei.
    public sealed class IntroCutscenePath
    {
        // ---------------- Zeitplan (s) ----------------
        public const float Duration = 12f;
        public const float FadeInEnd = 0.5f;
        public const float Shot1End = 4f;
        public const float Shot2End = 8.5f;
        public const float Shot3End = 11f;
        public const float ReturnDuration = Duration - Shot3End;   // normale Rückblende
        public const float SkipReturnDuration = 0.4f;              // Rückblende nach „Überspringen“
        public const float TitleIn = 8.9f, TitleOut = 10.9f;       // Titel „Welle 1“

        // Überblend-Fenster zwischen den Einstellungen (halbe Breite)
        private const float Cross12 = 0.4f;
        private const float Cross23 = 0.35f;

        // ---------------- Kamera-Parameter (m) ----------------
        public float LowHeight = 2.5f;        // Einstellung 1: Höhe über dem Weg
        public float BehindStart = 6f;        // Einstellung 1: Abstand hinter dem Portal am Anfang …
        public float BehindEnd = 3.5f;        // … und am Ende (Heranfahren)
        public float FlyHeight = 11f;         // Einstellung 2: Flughöhe
        public float LookAhead = 14f;         // Blickpunkt so weit voraus auf dem Weg
        public float ApproachStop = 10f;      // Einstellung 2 endet so weit vor dem Nexus (Bogenlänge)
        public float ApproachSpeed = 6f;      // Restgeschwindigkeit am Ende von Einstellung 2 (m/s)
        public float NexusRadius = 9f;        // Einstellung 3: Endabstand hinter dem Nexus
        public float NexusHeight = 7f;        // Einstellung 3: Endhöhe über dem Nexus
        public float NexusLookBack = 12f;     // Endblick: Punkt so weit vor dem Nexus auf dem Weg
        public float OrbitSign = 1f;          // Drehsinn des Schwenks (+1 / −1)
        public float EndAngleOffset = 0f;     // Endlage seitlich um den Nexus versetzt (Grad), z. B. neben einer Baumkrone
        public float PortalSetback = 0f;      // Spawnpunkt liegt so weit vor dem Portalbogen (Kamera steht hinter dem Bogen)

        private const float SampleStep = 1f;  // Abstand der Bahnpunkte nach dem Glätten

        public Vector3 Portal { get; }
        public Vector3 Nexus { get; }
        public float Length { get; }
        public bool UsedFallback { get; }     // kein gültiger Pfad: gerade Linie Portal → Nexus

        // Waagerechte Laufrichtung am Anfang (vom Portal weg) und am Ende (auf den Nexus zu)
        public Vector3 StartDir { get; }
        public Vector3 EndDir { get; }

        private readonly List<Vector3> _pts = new List<Vector3>();
        private readonly List<float> _cum = new List<float>();

        // corners: Pfad-Ecken (z. B. NavMeshPath.corners), darf null/leer sein → Fallback gerade Linie
        public IntroCutscenePath(IList<Vector3> corners, Vector3 portal, Vector3 nexus)
        {
            Portal = portal;
            Nexus = nexus;

            var raw = new List<Vector3>();
            bool valid = corners != null && corners.Count >= 2;
            if (valid)
            {
                if (FlatDistance(corners[0], portal) > 1f) raw.Add(portal);
                for (int i = 0; i < corners.Count; i++) raw.Add(corners[i]);
                if (FlatDistance(corners[corners.Count - 1], nexus) > 1f) raw.Add(nexus);
            }
            else
            {
                raw.Add(portal);
                raw.Add(nexus);
            }
            UsedFallback = !valid;

            // Doppelte Punkte entfernen
            var pts = new List<Vector3> { raw[0] };
            for (int i = 1; i < raw.Count; i++)
                if ((raw[i] - pts[pts.Count - 1]).sqrMagnitude > 0.0025f) pts.Add(raw[i]);
            // Portal = Nexus (entartet): kurze Ersatzstrecke, damit nichts durch null teilt
            if (pts.Count < 2) pts.Add(pts[0] + Vector3.right);

            var curve = CatmullRom(pts);
            Resample(curve, SampleStep, _pts);
            Smooth(_pts, 6);

            _cum.Add(0f);
            for (int i = 1; i < _pts.Count; i++) _cum.Add(_cum[i - 1] + Vector3.Distance(_pts[i - 1], _pts[i]));
            Length = Mathf.Max(0.01f, _cum[_cum.Count - 1]);

            float probe = Mathf.Min(6f, Length);
            StartDir = FlatDir(PointOnCurve(probe) - PointOnCurve(0f), Vector3.right);
            EndDir = FlatDir(PointOnCurve(Length) - PointOnCurve(Length - probe), StartDir);
        }

        // ---------------- Bahn ----------------

        // Punkt nach Bogenlänge s (m ab Portal). Außerhalb [0, Length] gerade weiter entlang Start-/Endrichtung.
        public Vector3 PointAt(float s)
        {
            if (s < 0f) return PointOnCurve(0f) + StartDir * s;
            if (s > Length) return PointOnCurve(Length) + EndDir * (s - Length);
            return PointOnCurve(s);
        }

        // Bogenlänge des Bahnpunkts, der p (waagerecht) am nächsten liegt
        public float ProjectS(Vector3 p)
        {
            float best = float.MaxValue, bestS = 0f;
            for (int i = 0; i < _pts.Count - 1; i++)
            {
                Vector3 a = _pts[i], b = _pts[i + 1];
                Vector3 ab = b - a; ab.y = 0f;
                Vector3 ap = p - a; ap.y = 0f;
                float len2 = ab.sqrMagnitude;
                float k = len2 > 1e-6f ? Mathf.Clamp01(Vector3.Dot(ap, ab) / len2) : 0f;
                float d = (ap - ab * k).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    bestS = Mathf.Lerp(_cum[i], _cum[i + 1], k);
                }
            }
            return bestS;
        }

        private Vector3 PointOnCurve(float s)
        {
            s = Mathf.Clamp(s, 0f, _cum[_cum.Count - 1]);
            int lo = 0, hi = _cum.Count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (_cum[mid] <= s) lo = mid; else hi = mid;
            }
            float seg = _cum[hi] - _cum[lo];
            float k = seg > 1e-6f ? (s - _cum[lo]) / seg : 0f;
            return Vector3.Lerp(_pts[lo], _pts[hi], k);
        }

        // ---------------- Einstellungen ----------------

        // Kamera-Lage zur Filmzeit t (0 … Shot3End; davor = Anfang von Einstellung 1, danach = Ende von Einstellung 3).
        // columnS: optional die Bogenlänge der Gegnerkolonne (NaN = keine Gegner) – Einstellung 2 neigt den Blick dann
        // zur Kolonne, solange die Kamera sie noch nicht überholt hat. Die Position hängt nur von t ab.
        public CutscenePose Evaluate(float t, float columnS = float.NaN)
        {
            t = Mathf.Clamp(t, FadeInEnd, Shot3End);
            Shot1(t, out Vector3 p1, out Vector3 l1);
            Shot2(t, columnS, out Vector3 p2, out Vector3 l2);
            Shot3(t, out Vector3 p3, out Vector3 l3);

            float w12 = Smooth01((t - (Shot1End - Cross12)) / (2f * Cross12));
            float w23 = Smooth01((t - (Shot2End - Cross23)) / (2f * Cross23));
            Vector3 pos = Vector3.Lerp(Vector3.Lerp(p1, p2, w12), p3, w23);
            Vector3 look = Vector3.Lerp(Vector3.Lerp(l1, l2, w12), l3, w23);
            return new CutscenePose(pos, LookRotation(look - pos));
        }

        // Fokuspunkt der Einstellung (was gefilmt wird) – z. B. für den Verdeckungs-Fader
        public Vector3 FocusAt(float t)
        {
            t = Mathf.Clamp(t, FadeInEnd, Shot3End);
            if (t < Shot1End) return PointAt(0f) + Vector3.up;
            if (t < Shot2End) return PointAt(CameraS2(t) + LookAhead) + Vector3.up;
            return Nexus + Vector3.up * 1.5f;
        }

        // Einstellung 1: tief hinter dem Portal, gleichmäßiges Heranfahren (läuft für den Übergang linear weiter)
        private void Shot1(float t, out Vector3 pos, out Vector3 look)
        {
            float k = (t - FadeInEnd) / (Shot1End - FadeInEnd); // bewusst ohne Begrenzung nach oben
            float s = -PortalSetback - Mathf.Lerp(BehindStart, BehindEnd, k);
            pos = PointAt(s) + Vector3.up * LowHeight;
            look = PointAt(s + LookAhead) + Vector3.up * 1.2f;
        }

        // Bogenlänge der Kamera in Einstellung 2: kubische Hermite-Kurve von −BehindEnd (Geschwindigkeit wie das
        // Heranfahren) bis ApproachStop vor dem Nexus (Restgeschwindigkeit ApproachSpeed); außerhalb linear weiter
        private float CameraS2(float t)
        {
            float dur = Shot2End - Shot1End;
            float s0 = -PortalSetback - BehindEnd;
            float s1 = Mathf.Max(s0 + 1f, Length - ApproachStop);
            float v0 = (BehindStart - BehindEnd) / (Shot1End - FadeInEnd) * dur; // pro Einheit u
            float v1 = ApproachSpeed * dur;
            // Monotonie (keine Rückwärtsfahrt): Steigungen höchstens 3 × mittlere Steigung
            float limit = 3f * (s1 - s0);
            v0 = Mathf.Min(v0, limit);
            v1 = Mathf.Min(v1, limit);
            float u = (t - Shot1End) / dur;
            if (u < 0f) return s0 + v0 * u;
            if (u > 1f) return s1 + v1 * (u - 1f);
            float u2 = u * u, u3 = u2 * u;
            return (2f * u3 - 3f * u2 + 1f) * s0 + (u3 - 2f * u2 + u) * v0 + (-2f * u3 + 3f * u2) * s1 + (u3 - u2) * v1;
        }

        private void Shot2(float t, float columnS, out Vector3 pos, out Vector3 look)
        {
            float s = CameraS2(t);
            float u = (t - Shot1End) / (Shot2End - Shot1End);
            float h = Mathf.Lerp(LowHeight, FlyHeight, Smooth01(u / 0.45f));
            pos = PointAt(s) + Vector3.up * h;
            look = PointAt(s + LookAhead) + Vector3.up;

            // Gegnerkolonne voraus: Blick halb zu ihr hin, schwindet, sobald die Kamera sie überholt
            if (!float.IsNaN(columnS))
            {
                float ahead = columnS - s;
                float w = 0.5f * Mathf.Clamp01(ahead / 6f) * Mathf.Clamp01((LookAhead * 2.5f - ahead) / LookAhead);
                if (w > 0f) look = Vector3.Lerp(look, PointAt(columnS) + Vector3.up, w);
            }
        }

        // Einstellung 3: Schwenk um den Nexus (Polarkoordinaten) vom Ende der Anflugbahn auf die Rückseite (hinter dem
        // Nexus, in Laufrichtung), dabei dreht sich der Blick vom Nexus auf den Weg, der zum Nexus führt
        private void Shot3(float t, out Vector3 pos, out Vector3 look)
        {
            Vector3 start = PointAt(CameraS2(Shot2End)) + Vector3.up * FlyHeight;
            Vector3 rel = start - Nexus; rel.y = 0f;
            float r0 = Mathf.Max(1f, rel.magnitude);
            float a0 = rel.sqrMagnitude > 1e-4f ? Mathf.Atan2(rel.x, rel.z) * Mathf.Rad2Deg : Mathf.Atan2(-EndDir.x, -EndDir.z) * Mathf.Rad2Deg;
            float a1 = Mathf.Atan2(EndDir.x, EndDir.z) * Mathf.Rad2Deg + EndAngleOffset;
            float sweep = OrbitSign >= 0f ? Mathf.Repeat(a1 - a0, 360f) : -Mathf.Repeat(a0 - a1, 360f);

            float v = Mathf.Clamp01((t - Shot2End) / (Shot3End - Shot2End));
            float e = Smooth01(v);
            float a = (a0 + sweep * e) * Mathf.Deg2Rad;
            float r = Mathf.Lerp(r0, NexusRadius, e);
            float y = Mathf.Lerp(start.y, Nexus.y + NexusHeight, e);
            pos = new Vector3(Nexus.x + Mathf.Sin(a) * r, y, Nexus.z + Mathf.Cos(a) * r);

            Vector3 nexusLook = Nexus + Vector3.up * 1.5f;
            Vector3 roadLook = PointAt(Length - NexusLookBack) + Vector3.up;
            look = Vector3.Lerp(nexusLook, roadLook, Smooth01((v - 0.2f) / 0.8f));
        }

        // Endlage von Einstellung 3 (für Sichtprüfungen der Laufzeit)
        public CutscenePose FinalPose => Evaluate(Shot3End);

        // ---------------- Rückblende / Zeitkurven ----------------

        // Weiches Gleiten von a nach b (k = 0..1, ease-in-out); arc hebt die Mitte der Fahrt etwas an
        public static CutscenePose Blend(CutscenePose a, CutscenePose b, float k, float arc = 0f)
        {
            float e = Smooth01(k);
            Vector3 p = Vector3.Lerp(a.Position, b.Position, e) + Vector3.up * (arc * Mathf.Sin(e * Mathf.PI));
            return new CutscenePose(p, Quaternion.Slerp(a.Rotation, b.Rotation, e));
        }

        // Smoothstep auf [0, 1] begrenzt
        public static float Smooth01(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        // Kinobalken (0 = weg, 1 = ganz drin): Einfahren 0–0,5 s, Ausfahren während der Rückblende
        public static float BarsAt(float t, float returnStart, float returnDuration)
        {
            if (t < returnStart) return Smooth01(t / FadeInEnd);
            return 1f - Smooth01((t - returnStart) / Mathf.Max(0.01f, returnDuration));
        }

        // Titel-Deckkraft (0..1) in Einstellung 3
        public static float TitleAlpha(float t)
        {
            float fadeIn = Smooth01((t - TitleIn) / 0.4f);
            float fadeOut = 1f - Smooth01((t - (TitleOut - 0.3f)) / 0.3f);
            return Mathf.Min(fadeIn, fadeOut);
        }

        // Schwarzblende beim Schnitt von der Spielkamera auf Einstellung 1 (0,35–0,5 s abblenden, 0,5–0,8 s aufblenden)
        public static float CutFadeAt(float t)
        {
            if (t < FadeInEnd) return Smooth01((t - 0.3f) / 0.2f);
            return 1f - Smooth01((t - FadeInEnd) / 0.3f);
        }

        // ---------------- Hilfen ----------------

        private static Quaternion LookRotation(Vector3 dir)
        {
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
            return Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f; b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private static Vector3 FlatDir(Vector3 v, Vector3 fallback)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : fallback;
        }

        // Zentripetale Catmull-Rom-Kurve durch alle Punkte (Endpunkte gespiegelt verlängert), dicht abgetastet
        private static List<Vector3> CatmullRom(List<Vector3> p)
        {
            var result = new List<Vector3>();
            int n = p.Count;
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 p1 = p[i], p2 = p[i + 1];
                Vector3 p0 = i > 0 ? p[i - 1] : p1 + (p1 - p2);
                Vector3 p3 = i + 2 < n ? p[i + 2] : p2 + (p2 - p1);
                float seg = Vector3.Distance(p1, p2);
                int steps = Mathf.Max(1, Mathf.CeilToInt(seg / 0.5f));
                for (int k = 0; k < steps; k++) result.Add(CentripetalPoint(p0, p1, p2, p3, k / (float)steps));
            }
            result.Add(p[n - 1]);
            return result;
        }

        private static Vector3 CentripetalPoint(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
        {
            float t0 = 0f;
            float t1 = t0 + Mathf.Max(1e-4f, Mathf.Sqrt(Vector3.Distance(p0, p1)));
            float t2 = t1 + Mathf.Max(1e-4f, Mathf.Sqrt(Vector3.Distance(p1, p2)));
            float t3 = t2 + Mathf.Max(1e-4f, Mathf.Sqrt(Vector3.Distance(p2, p3)));
            float t = Mathf.Lerp(t1, t2, u);
            Vector3 a1 = (t1 - t) / (t1 - t0) * p0 + (t - t0) / (t1 - t0) * p1;
            Vector3 a2 = (t2 - t) / (t2 - t1) * p1 + (t - t1) / (t2 - t1) * p2;
            Vector3 a3 = (t3 - t) / (t3 - t2) * p2 + (t - t2) / (t3 - t2) * p3;
            Vector3 b1 = (t2 - t) / (t2 - t0) * a1 + (t - t0) / (t2 - t0) * a2;
            Vector3 b2 = (t3 - t) / (t3 - t1) * a2 + (t - t1) / (t3 - t1) * a3;
            return (t2 - t) / (t2 - t1) * b1 + (t - t1) / (t2 - t1) * b2;
        }

        // Gleichmäßig neu abtasten (Abstand step entlang der Polylinie)
        private static void Resample(List<Vector3> line, float step, List<Vector3> output)
        {
            output.Clear();
            output.Add(line[0]);
            float carry = 0f;
            for (int i = 1; i < line.Count; i++)
            {
                Vector3 a = line[i - 1], b = line[i];
                float seg = Vector3.Distance(a, b);
                float pos = step - carry;
                while (pos <= seg)
                {
                    output.Add(Vector3.Lerp(a, b, pos / seg));
                    pos += step;
                }
                carry = seg - (pos - step);
            }
            if ((output[output.Count - 1] - line[line.Count - 1]).sqrMagnitude > 1e-4f) output.Add(line[line.Count - 1]);
        }

        // Laplace-Glättung (Endpunkte fest): nimmt die Zacken der NavMesh-Ecken heraus
        private static void Smooth(List<Vector3> pts, int iterations)
        {
            if (pts.Count < 3) return;
            var tmp = new Vector3[pts.Count];
            for (int it = 0; it < iterations; it++)
            {
                tmp[0] = pts[0];
                tmp[pts.Count - 1] = pts[pts.Count - 1];
                for (int i = 1; i < pts.Count - 1; i++) tmp[i] = pts[i - 1] * 0.25f + pts[i] * 0.5f + pts[i + 1] * 0.25f;
                for (int i = 0; i < pts.Count; i++) pts[i] = tmp[i];
            }
        }
    }
}
