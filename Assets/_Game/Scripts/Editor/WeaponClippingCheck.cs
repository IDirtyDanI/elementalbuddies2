using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // Prüft, ob die Waffen eines Champions in irgendeiner Animation in den Körper eindringen.
    // Menü: BuddyTD → Champions → Waffen-Clipping prüfen (alle Visuals des Player-Prefabs).
    //
    // Verfahren: Jede Pose des Controllers (ChampionPoseSampler.Enumerate: Idle/Walk/Run, Sprung, alle Aktions-States,
    // Oberkörper-Aktionen zusätzlich im Lauf) wird in N Schritten mit dem echten Animator (alle Layer + ChampionWeaponHold)
    // ausgewertet. Der Körper wird per SkinnedMeshRenderer.BakeMesh als Dreiecksnetz gebacken (Raster-Hash, 6 cm).
    // Zwei Tests je Frame:
    // 1) Volumen: Für jeden Abtastpunkt der Waffe (Vertices, Dreiecks- und Kanten-Mitten) der nächste Oberflächenpunkt;
    //    liegt der Punkt hinter der Fläche (Pseudo-Normale) UND sagt die Strahl-Parität (Mehrheit von 5 Strahlen) „innen“,
    //    zählt der Abstand als Eindringtiefe (gedeckelt bei 12 cm). Die Parität filtert einseitige Flächen (Hutkrempe, Kragen).
    // 2) Durchstoßen: Waffenkanten, die ein Körperdreieck schneiden (dünne Robe/Ärmel haben kaum Volumen).
    // Körperteil = dominanter Knochen + Material des Dreiecks. Die haltende Hand (Hand + Finger) ist ausgenommen.
    // Ausgabe: Konsole + Captures/Clipping/<Visual>_report.txt, Kontaktbilder der schlimmsten Frames (roter Punkt).
    public static class WeaponClippingCheck
    {
        public const string OutDir = "Captures/Clipping";
        public const float Tolerance = 0.01f;   // < 1 cm gilt als Berührung
        private const float SearchRadius = 0.12f;
        private const float Cell = 0.06f;

        public class Hit
        {
            public string Clip;
            public float Time, Depth;
            public string Weapon, Part;
            public Vector3 Point;
            public int Frame;
        }

        public class ClipResult
        {
            public string Clip;
            public int Frames, FramesHit;
            public Hit Worst;
            // je Waffen-Sockel (Rechts/Links/Rücken): Frames mit Treffer und tiefster Treffer
            public Dictionary<string, int> SlotFrames = new Dictionary<string, int>();
            public Dictionary<string, Hit> SlotWorst = new Dictionary<string, Hit>();
        }

        public static string Slot(string weapon)
        {
            int i = weapon.IndexOf(':');
            return i > 0 ? weapon.Substring(0, i) : weapon;
        }

        [MenuItem("BuddyTD/Champions/Waffen-Clipping prüfen")]
        public static void CheckPlayerPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Player.prefab");
            var sb = new StringBuilder();
            foreach (var cv in prefab.GetComponentsInChildren<ChampionVisual>(true))
            {
                var res = Check(cv, 30, true);
                sb.AppendLine(Summary(cv.name, res));
            }
            Debug.Log("WeaponClippingCheck:\n" + sb);
        }

        public static string Summary(string name, List<ClipResult> res)
        {
            var sb = new StringBuilder();
            sb.AppendLine("== " + name);
            float worst = 0f;
            foreach (var r in res)
            {
                if (r.Worst != null) worst = Mathf.Max(worst, r.Worst.Depth);
                sb.AppendLine(string.Format("{0,-18} {1,2}/{2} Frames  max {3,5:0.0} cm  {4}", r.Clip, r.FramesHit, r.Frames,
                    r.Worst != null ? r.Worst.Depth * 100f : 0f,
                    r.Worst != null ? string.Format("t={0:0.00}s {1} ↔ {2}", r.Worst.Time, r.Worst.Weapon, r.Worst.Part) : "ok"));
                // mehrere Waffen: Aufschlüsselung je Sockel
                if (r.SlotWorst.Count > 1 || r.SlotWorst.Count == 1 && r.Worst != null && Slot(r.Worst.Weapon) != new List<string>(r.SlotWorst.Keys)[0])
                    foreach (var kv in r.SlotWorst)
                        sb.AppendLine(string.Format("    {0,-8} {1,2}/{2} Frames  max {3,5:0.0} cm  t={4:0.00}s ↔ {5}", kv.Key, r.SlotFrames[kv.Key], r.Frames, kv.Value.Depth * 100f, kv.Value.Time, kv.Value.Part));
            }
            sb.AppendLine(string.Format("Gesamt: max {0:0.0} cm", worst * 100f));
            return sb.ToString();
        }

        // ---------------- Kern ----------------

        private class BodyPart
        {
            public SkinnedMeshRenderer Smr;
            public Mesh Baked;
            public int[] Tris;
            public string[] TriPart;
        }

        private class WeaponPart
        {
            public string Name;
            public Transform T;
            public Vector3[] LocalPoints;
            public int[] Edges; // Paare von Indizes in LocalPoints (Waffen-Vertices), jede Kante einmal
            public HashSet<HumanBodyBones> Excluded = new HashSet<HumanBodyBones>();
        }

        // filter: nur bestimmte Posen prüfen (z. B. p => p.Label.StartsWith("Jump")); null = alle
        // label: Name für Bericht/Bilder (Standard: Objektname)
        public static List<ClipResult> Check(ChampionVisual source, int steps, bool screenshots, int maxShots = 8, System.Predicate<PoseSample> filter = null, string label = null, bool holds = true)
        {
            string name = string.IsNullOrEmpty(label) ? source.name : label;
            var results = new List<ClipResult>();
            var allHits = new List<Hit>();
            using (var s = new ChampionPoseSampler(source, new Vector3(0f, -400f, 0f), true, holds))
            {
                var ac = ChampionPoseSampler.ControllerOf(s.Animator);
                var weapons = CollectWeapons(s);
                if (weapons.Count == 0) return results;
                var bodies = CollectBody(s);
                int frame = 0;
                foreach (var pose in ChampionPoseSampler.Enumerate(ac))
                {
                    if (filter != null && !filter(pose)) continue;
                    var r = new ClipResult { Clip = pose.Label };
                    for (int i = 0; i < steps; i++)
                    {
                        float t = steps > 1 ? i / (float)(steps - 1) : 0f;
                        if (pose.Layer > 0) t *= 0.98f;
                        s.Sample(pose, t);
                        var slots = new Dictionary<string, Hit>();
                        var hit = Measure(bodies, weapons, slots);
                        r.Frames++;
                        frame++;
                        foreach (var kv in slots)
                        {
                            if (kv.Value.Depth < Tolerance) continue;
                            kv.Value.Clip = pose.Label;
                            kv.Value.Time = t * pose.Length;
                            kv.Value.Frame = i;
                            int n;
                            r.SlotFrames.TryGetValue(kv.Key, out n);
                            r.SlotFrames[kv.Key] = n + 1;
                            Hit old;
                            if (!r.SlotWorst.TryGetValue(kv.Key, out old) || kv.Value.Depth > old.Depth) r.SlotWorst[kv.Key] = kv.Value;
                        }
                        if (hit == null || hit.Depth < Tolerance) continue;
                        r.FramesHit++;
                        hit.Clip = pose.Label;
                        hit.Time = t * pose.Length;
                        hit.Frame = i;
                        allHits.Add(hit);
                        if (r.Worst == null || hit.Depth > r.Worst.Depth) r.Worst = hit;
                    }
                    results.Add(r);
                }

                if (screenshots)
                {
                    Directory.CreateDirectory(OutDir);
                    foreach (var f in Directory.GetFiles(OutDir, name + "_*.png")) File.Delete(f);
                    var worst = new List<ClipResult>(results);
                    worst.RemoveAll(x => x.Worst == null);
                    worst.Sort((x, y) => y.Worst.Depth.CompareTo(x.Worst.Depth));
                    var poses = ChampionPoseSampler.Enumerate(ac);
                    for (int i = 0; i < worst.Count && i < maxShots; i++)
                    {
                        var h = worst[i].Worst;
                        var pose = poses.Find(p => p.Label == h.Clip);
                        float t = steps > 1 ? h.Frame / (float)(steps - 1) : 0f;
                        if (pose.Layer > 0) t *= 0.98f;
                        s.Sample(pose, t);
                        Shot(s.Root.transform, h, OutDir + "/" + name + "_" + (i + 1) + "_" + Safe(h.Clip) + ".png");
                    }
                }

                foreach (var b in bodies) Object.DestroyImmediate(b.Baked);
            }

            Directory.CreateDirectory(OutDir);
            var rep = new StringBuilder(Summary(name, results));
            rep.AppendLine();
            rep.AppendLine("Alle Treffer (Clip;Zeit s;Tiefe cm;Waffe;Körperteil):");
            foreach (var h in allHits) rep.AppendLine(string.Format("{0};{1:0.000};{2:0.00};{3};{4}", h.Clip, h.Time, h.Depth * 100f, h.Weapon, h.Part));
            File.WriteAllText(OutDir + "/" + name + "_report.txt", rep.ToString());
            return results;
        }

        private static string Safe(string s) { return s.Replace("+", "_").Replace(" ", "_"); }

        private static List<WeaponPart> CollectWeapons(ChampionPoseSampler s)
        {
            var list = new List<WeaponPart>();
            Add(list, s.Visual.RightHandInstance, "Rechts", s.Animator, true, false);
            Add(list, s.Visual.LeftHandInstance, "Links", s.Animator, false, false);
            Add(list, s.Visual.BackInstance, "Rücken", s.Animator, false, true);
            return list;
        }

        private static readonly HumanBodyBones[] RightHandBones =
        {
            HumanBodyBones.RightHand, HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal,
            HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal,
            HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
            HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal,
            HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal,
        };

        private static void Add(List<WeaponPart> list, Transform inst, string slot, Animator a, bool right, bool back)
        {
            if (inst == null) return;
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var w = new WeaponPart { Name = slot + ":" + mf.sharedMesh.name, T = mf.transform };
                var v = mf.sharedMesh.vertices;
                bool shield = mf.sharedMesh.name.Contains("Shield");
                // Schild: Armriemen (Leder) und Griffstange umschließen Unterarm/Faust bestimmungsgemäß → nicht prüfen
                var tris = shield ? GripFreeTriangles(mf, "Leather", "SteelDark") : mf.sharedMesh.triangles;
                if (shield)
                {
                    // nur benutzte Vertices behalten (Indizes umnummerieren)
                    var map = new Dictionary<int, int>();
                    var used = new List<Vector3>();
                    for (int i = 0; i < tris.Length; i++)
                    {
                        int ni;
                        if (!map.TryGetValue(tris[i], out ni)) { ni = used.Count; map[tris[i]] = ni; used.Add(v[tris[i]]); }
                        tris[i] = ni;
                    }
                    v = used.ToArray();
                }
                var pts = new List<Vector3>(v);
                for (int i = 0; i < tris.Length; i += 3)
                {
                    Vector3 p0 = v[tris[i]], p1 = v[tris[i + 1]], p2 = v[tris[i + 2]];
                    pts.Add((p0 + p1 + p2) / 3f);
                    pts.Add((p0 + p1) * 0.5f);
                    pts.Add((p1 + p2) * 0.5f);
                    pts.Add((p2 + p0) * 0.5f);
                }
                w.LocalPoints = pts.ToArray();
                var edgeSet = new HashSet<long>();
                var edges = new List<int>();
                for (int i = 0; i < tris.Length; i += 3)
                    for (int k = 0; k < 3; k++)
                    {
                        int e0 = tris[i + k], e1 = tris[i + (k + 1) % 3];
                        long key = e0 < e1 ? ((long)e0 << 32) | (uint)e1 : ((long)e1 << 32) | (uint)e0;
                        if (edgeSet.Add(key)) { edges.Add(e0); edges.Add(e1); }
                    }
                w.Edges = edges.ToArray();
                if (back)
                {
                    w.Excluded.Add(HumanBodyBones.Chest);
                    w.Excluded.Add(HumanBodyBones.UpperChest);
                    w.Excluded.Add(HumanBodyBones.Spine);
                }
                else
                {
                    foreach (var b in RightHandBones)
                        w.Excluded.Add(right ? b : (HumanBodyBones)System.Enum.Parse(typeof(HumanBodyBones), b.ToString().Replace("Right", "Left")));
                    // Schild: Armriemen liegt bestimmungsgemäß am Unterarm
                    if (shield)
                        w.Excluded.Add(right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
                }
                list.Add(w);
            }
        }

        // Dreiecke aller Submeshes außer denen, deren Material einen der Namen enthält
        private static int[] GripFreeTriangles(MeshFilter mf, params string[] skip)
        {
            var mr = mf.GetComponent<MeshRenderer>();
            var mats = mr != null ? mr.sharedMaterials : new Material[0];
            var l = new List<int>();
            for (int sub = 0; sub < mf.sharedMesh.subMeshCount; sub++)
            {
                string n = sub < mats.Length && mats[sub] != null ? mats[sub].name : "";
                bool drop = false;
                foreach (var k in skip) if (n.Contains(k)) drop = true;
                if (!drop) l.AddRange(mf.sharedMesh.GetTriangles(sub));
            }
            return l.ToArray();
        }

        private static List<BodyPart> CollectBody(ChampionPoseSampler s)
        {
            var boneMap = new Dictionary<Transform, HumanBodyBones>();
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var t = s.Animator.GetBoneTransform((HumanBodyBones)i);
                if (t != null) boneMap[t] = (HumanBodyBones)i;
            }

            var list = new List<BodyPart>();
            foreach (var smr in s.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = smr.sharedMesh;
                if (mesh == null) continue;
                var bp = new BodyPart { Smr = smr, Baked = new Mesh() };
                var bw = mesh.boneWeights;
                var bones = smr.bones;
                var vertBone = new HumanBodyBones[mesh.vertexCount];
                for (int i = 0; i < vertBone.Length; i++)
                {
                    Transform t = bw.Length > i && bw[i].boneIndex0 < bones.Length ? bones[bw[i].boneIndex0] : null;
                    // nicht-humanoide Knochen: zum nächsten Humanoid-Vorfahren
                    while (t != null && !boneMap.ContainsKey(t)) t = t.parent;
                    vertBone[i] = t != null ? boneMap[t] : HumanBodyBones.Hips;
                }
                var tris = new List<int>();
                var parts = new List<string>();
                var mats = smr.sharedMaterials;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var st = mesh.GetTriangles(sub);
                    string mat = sub < mats.Length && mats[sub] != null ? mats[sub].name : "?";
                    for (int i = 0; i < st.Length; i += 3)
                    {
                        tris.Add(st[i]); tris.Add(st[i + 1]); tris.Add(st[i + 2]);
                        var b = vertBone[st[i]];
                        parts.Add(b + "/" + mat);
                    }
                }
                bp.Tris = tris.ToArray();
                bp.TriPart = parts.ToArray();
                list.Add(bp);
            }
            return list;
        }

        // Tiefste Durchdringung in der aktuellen Pose (null = keine)
        private static Hit Measure(List<BodyPart> bodies, List<WeaponPart> weapons, Dictionary<string, Hit> slots)
        {
            // Körper backen und rastern
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var triPart = new List<string>();
            foreach (var b in bodies)
            {
                // useScale = true liefert Vertices im lokalen Raum des Renderers (inkl. Knochen-Skalierung) → volle Matrix
                b.Smr.BakeMesh(b.Baked, true);
                var m = b.Smr.transform.localToWorldMatrix;
                int off = verts.Count;
                foreach (var v in b.Baked.vertices) verts.Add(m.MultiplyPoint3x4(v));
                foreach (var i in b.Tris) tris.Add(i + off);
                triPart.AddRange(b.TriPart);
            }
            var grid = new Dictionary<Vector3Int, List<int>>();
            var bodyBounds = new Bounds(verts.Count > 0 ? verts[0] : Vector3.zero, Vector3.zero);
            foreach (var v in verts) bodyBounds.Encapsulate(v);
            int triCount = tris.Count / 3;
            var normals = new Vector3[triCount];
            for (int t = 0; t < triCount; t++)
            {
                Vector3 a = verts[tris[t * 3]], b = verts[tris[t * 3 + 1]], c = verts[tris[t * 3 + 2]];
                normals[t] = Vector3.Cross(b - a, c - a);
                Vector3 mn = Vector3.Min(a, Vector3.Min(b, c)), mx = Vector3.Max(a, Vector3.Max(b, c));
                Vector3Int c0 = CellOf(mn), c1 = CellOf(mx);
                for (int x = c0.x; x <= c1.x; x++)
                    for (int y = c0.y; y <= c1.y; y++)
                        for (int z = c0.z; z <= c1.z; z++)
                        {
                            var key = new Vector3Int(x, y, z);
                            List<int> l;
                            if (!grid.TryGetValue(key, out l)) grid[key] = l = new List<int>();
                            l.Add(t);
                        }
            }

            Hit best = null;
            int r = Mathf.CeilToInt(SearchRadius / Cell);
            var seen = new HashSet<int>();
            foreach (var w in weapons)
            {
                var m = w.T.localToWorldMatrix;
                foreach (var lp in w.LocalPoints)
                {
                    Vector3 p = m.MultiplyPoint3x4(lp);
                    Vector3Int pc = CellOf(p);
                    float bestD = SearchRadius;
                    int bestT = -1;
                    Vector3 bestQ = Vector3.zero;
                    seen.Clear();
                    for (int x = -r; x <= r; x++)
                        for (int y = -r; y <= r; y++)
                            for (int z = -r; z <= r; z++)
                            {
                                List<int> l;
                                if (!grid.TryGetValue(new Vector3Int(pc.x + x, pc.y + y, pc.z + z), out l)) continue;
                                foreach (int t in l)
                                {
                                    if (!seen.Add(t)) continue;
                                    Vector3 q = ClosestOnTriangle(p, verts[tris[t * 3]], verts[tris[t * 3 + 1]], verts[tris[t * 3 + 2]]);
                                    float d = (p - q).magnitude;
                                    if (d < bestD) { bestD = d; bestT = t; bestQ = q; }
                                }
                            }
                    if (bestT < 0) continue;
                    // Pseudo-Normale: alle Dreiecke, deren nächster Punkt (fast) gleich weit weg ist
                    Vector3 n = Vector3.zero;
                    foreach (int t in seen)
                    {
                        Vector3 q = ClosestOnTriangle(p, verts[tris[t * 3]], verts[tris[t * 3 + 1]], verts[tris[t * 3 + 2]]);
                        if ((q - bestQ).sqrMagnitude < 1e-6f) n += normals[t].normalized;
                    }
                    if (Vector3.Dot(p - bestQ, n) >= 0f) continue; // außen
                    string part = triPart[bestT];
                    if (IsExcluded(w, part)) continue;
                    Hit sb0;
                    if (slots.TryGetValue(Slot(w.Name), out sb0) && bestD <= sb0.Depth) continue;
                    // Gegenprobe per Strahl-Parität: hinter einer einseitigen Fläche (Hutkrempe, Kragen, Manschette)
                    // zu liegen heißt nicht „im Körper“ – nur zählen, wenn die Mehrheit der Strahlen ungerade oft schneidet
                    if (!InsideByRays(p, verts, tris, grid, bodyBounds)) continue;
                    var nh = new Hit { Depth = bestD, Weapon = w.Name, Part = part, Point = p };
                    slots[Slot(w.Name)] = nh;
                    if (best == null || bestD > best.Depth) best = nh;
                }
            }
            // Kanten der Waffe, die eine Körperfläche schneiden (dünne Stoffe wie Robe/Ärmel haben kaum „Tiefe“,
            // ein Durchstoßen ist trotzdem sichtbar).
            foreach (var w in weapons)
            {
                var m = w.T.localToWorldMatrix;
                for (int e = 0; e < w.Edges.Length; e += 2)
                {
                    Vector3 a = m.MultiplyPoint3x4(w.LocalPoints[w.Edges[e]]), b = m.MultiplyPoint3x4(w.LocalPoints[w.Edges[e + 1]]);
                    Vector3Int c0 = CellOf(Vector3.Min(a, b)), c1 = CellOf(Vector3.Max(a, b));
                    seen.Clear();
                    for (int x = c0.x; x <= c1.x; x++)
                        for (int y = c0.y; y <= c1.y; y++)
                            for (int z = c0.z; z <= c1.z; z++)
                            {
                                List<int> l;
                                if (!grid.TryGetValue(new Vector3Int(x, y, z), out l)) continue;
                                foreach (int t in l)
                                {
                                    if (!seen.Add(t)) continue;
                                    Vector3 ta = verts[tris[t * 3]], tb = verts[tris[t * 3 + 1]], tc = verts[tris[t * 3 + 2]];
                                    if (!SegmentHit(a, b, ta, tb, tc)) continue;
                                    string part = triPart[t];
                                    if (IsExcluded(w, part)) continue;
                                    // Tiefe: Abstand des Kanten-Endpunkts hinter der Fläche zum Dreieck (gedeckelt), mind. 1 cm
                                    Vector3 nn = normals[t];
                                    Vector3 back = Vector3.Dot(a - ta, nn) < 0f ? a : b;
                                    float depth = Mathf.Clamp((back - ClosestOnTriangle(back, ta, tb, tc)).magnitude, 0.01f, SearchRadius);
                                    var eh = new Hit { Depth = depth, Weapon = w.Name, Part = part, Point = (a + b) * 0.5f };
                                    Hit sb1;
                                    if (!slots.TryGetValue(Slot(w.Name), out sb1) || depth > sb1.Depth) slots[Slot(w.Name)] = eh;
                                    if (best == null || depth > best.Depth) best = eh;
                                }
                            }
                }
            }
            return best;
        }

        // Strecke a→b schneidet Dreieck?
        private static bool SegmentHit(Vector3 a, Vector3 b, Vector3 ta, Vector3 tb, Vector3 tc)
        {
            Vector3 dir = b - a;
            Vector3 e1 = tb - ta, e2 = tc - ta;
            Vector3 pv = Vector3.Cross(dir, e2);
            float det = Vector3.Dot(e1, pv);
            if (det > -1e-12f && det < 1e-12f) return false;
            float inv = 1f / det;
            Vector3 tv = a - ta;
            float u = Vector3.Dot(tv, pv) * inv;
            if (u < 0f || u > 1f) return false;
            Vector3 qv = Vector3.Cross(tv, e1);
            float v = Vector3.Dot(dir, qv) * inv;
            if (v < 0f || u + v > 1f) return false;
            float t = Vector3.Dot(e2, qv) * inv;
            return t >= 0f && t <= 1f;
        }

        private static readonly Vector3[] RayDirs =
        {
            new Vector3(0.31f, 0.89f, 0.33f).normalized, new Vector3(-0.72f, 0.2f, 0.66f).normalized, new Vector3(0.05f, -0.97f, 0.22f).normalized,
            new Vector3(0.93f, -0.1f, -0.35f).normalized, new Vector3(-0.4f, 0.35f, -0.85f).normalized,
        };

        // Strahlen laufen durch das Raster (nur Dreiecke der durchquerten Zellen werden getestet)
        private static readonly HashSet<int> _rayVisited = new HashSet<int>();

        private static bool InsideByRays(Vector3 p, List<Vector3> verts, List<int> tris, Dictionary<Vector3Int, List<int>> grid, Bounds bounds)
        {
            int odd = 0;
            float maxLen = bounds.size.magnitude + Cell;
            float step = Cell * 0.25f;
            for (int d = 0; d < RayDirs.Length; d++)
            {
                int hits = 0;
                _rayVisited.Clear();
                var last = new Vector3Int(int.MinValue, 0, 0);
                for (float s = 0f; s <= maxLen; s += step)
                {
                    Vector3 q = p + RayDirs[d] * s;
                    if (s > Cell && !bounds.Contains(q)) break;
                    var cell = CellOf(q);
                    if (cell == last) continue;
                    last = cell;
                    List<int> l;
                    if (!grid.TryGetValue(cell, out l)) continue;
                    foreach (int t in l)
                        if (_rayVisited.Add(t) && RayHit(p, RayDirs[d], verts[tris[t * 3]], verts[tris[t * 3 + 1]], verts[tris[t * 3 + 2]])) hits++;
                }
                if ((hits & 1) == 1) odd++;
            }
            return odd * 2 > RayDirs.Length;
        }

        // Möller–Trumbore, nur Treffer vor dem Ursprung
        private static bool RayHit(Vector3 o, Vector3 dir, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 e1 = b - a, e2 = c - a;
            Vector3 pv = Vector3.Cross(dir, e2);
            float det = Vector3.Dot(e1, pv);
            if (det > -1e-9f && det < 1e-9f) return false;
            float inv = 1f / det;
            Vector3 tv = o - a;
            float u = Vector3.Dot(tv, pv) * inv;
            if (u < 0f || u > 1f) return false;
            Vector3 qv = Vector3.Cross(tv, e1);
            float v = Vector3.Dot(dir, qv) * inv;
            if (v < 0f || u + v > 1f) return false;
            return Vector3.Dot(e2, qv) * inv > 0f;
        }

        private static bool IsExcluded(WeaponPart w, string part)
        {
            int slash = part.IndexOf('/');
            string bone = slash > 0 ? part.Substring(0, slash) : part;
            foreach (var b in w.Excluded) if (b.ToString() == bone) return true;
            return false;
        }

        private static Vector3Int CellOf(Vector3 p)
        {
            return new Vector3Int(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.y / Cell), Mathf.FloorToInt(p.z / Cell));
        }

        // Nächster Punkt auf Dreieck (Ericson, Real-Time Collision Detection)
        public static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            float denom = 1f / (va + vb + vc);
            float v = vb * denom, w = vc * denom;
            return a + ab * v + ac * w;
        }

        // ---------------- Kontaktbilder ----------------

        private static void Shot(Transform root, Hit h, string path)
        {
            var camGo = new GameObject("__ClipCam") { hideFlags = HideFlags.HideAndDontSave };
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.hideFlags = HideFlags.HideAndDontSave;
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            marker.transform.position = h.Point;
            marker.transform.localScale = Vector3.one * 0.035f;
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.SetColor("_BaseColor", Color.red);
            marker.GetComponent<Renderer>().sharedMaterial = mat;
            var lightGo = new GameObject("__ClipLight") { hideFlags = HideFlags.HideAndDontSave };
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightGo.transform.rotation = Quaternion.Euler(35f, 200f, 0f);
            var rt = new RenderTexture(1024, 512, 24);
            try
            {
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.82f, 0.8f, 0.76f);
                cam.fieldOfView = 30f;
                cam.nearClipPlane = 0.05f;
                cam.targetTexture = rt;
                // links: Gesamtansicht schräg vorne, rechts: Nahaufnahme am Kontaktpunkt (von der Seite)
                Vector3 center = root.position + Vector3.up * 1.0f * root.lossyScale.y;
                Vector3[] eye = { center + new Vector3(1.8f, 0.5f, 3.6f), h.Point + (h.Point - center).normalized * 0.2f + new Vector3(0.9f, 0.25f, 0.5f) };
                Vector3[] look = { center, h.Point };
                var full = new Texture2D(1024, 512, TextureFormat.RGB24, false);
                for (int k = 0; k < 2; k++)
                {
                    cam.rect = new Rect(k * 0.5f, 0f, 0.5f, 1f);
                    cam.transform.position = eye[k];
                    cam.transform.LookAt(look[k]);
                    cam.Render();
                }
                RenderTexture.active = rt;
                full.ReadPixels(new Rect(0, 0, 1024, 512), 0, 0);
                full.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(path, full.EncodeToPNG());
                Object.DestroyImmediate(full);
            }
            finally
            {
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(marker);
                Object.DestroyImmediate(lightGo);
                Object.DestroyImmediate(mat);
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }
    }
}
