using System;
using UnityEngine;

namespace ElementalBuddies
{
    // Fliegende Lavakugel des Magma-Buddys: Parabel von Start zu Ziel, beim Aufschlag wird OnImpact aufgerufen.
    // Eigenständig, damit der Einschlag auch passiert, wenn der Buddy während des Flugs stirbt.
    public class LavaBall : MonoBehaviour
    {
        private Vector3 _from, _to;
        private float _flightTime, _peak, _t;
        private Action<Vector3> _onImpact;

        public static LavaBall Launch(Vector3 from, Vector3 to, float flightTime, float peakHeight, GameObject prefab,
            float runtimeSize, Action<Vector3> onImpact)
        {
            GameObject go;
            if (prefab != null)
            {
                go = Instantiate(prefab, from, Quaternion.identity);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);
                var rend = go.GetComponent<MeshRenderer>();
                rend.sharedMaterial = LavaPuddle.RuntimeLavaMaterial;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                go.transform.position = from;
                go.transform.localScale = Vector3.one * runtimeSize;
            }
            go.name = "LavaBall";

            var ball = go.AddComponent<LavaBall>();
            ball._from = from;
            ball._to = to;
            ball._flightTime = Mathf.Max(0.05f, flightTime);
            ball._peak = peakHeight;
            ball._onImpact = onImpact;
            return ball;
        }

        void Update()
        {
            _t += Time.deltaTime;
            float p = Mathf.Clamp01(_t / _flightTime);
            // Lineare Bahn + Parabel-Bogen (Scheitel = _peak über der Verbindungslinie)
            Vector3 pos = Vector3.Lerp(_from, _to, p) + Vector3.up * (4f * _peak * p * (1f - p));
            Vector3 dir = pos - transform.position;
            transform.position = pos;
            if (dir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(dir);

            if (p >= 1f)
            {
                var cb = _onImpact;
                _onImpact = null;
                cb?.Invoke(_to);
                DetachTrails();
                Destroy(gameObject);
            }
        }

        // Schweif, Rauch und Tropfen beim Einschlag ablösen, damit sie ausklingen statt schlagartig zu verschwinden
        private void DetachTrails()
        {
            foreach (var ps in GetComponentsInChildren<ParticleSystem>())
            {
                if (ps.transform == transform) continue;
                ps.transform.SetParent(null, true);
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                Destroy(ps.gameObject, ps.main.startLifetime.constantMax + 0.5f);
            }
            foreach (var tr in GetComponentsInChildren<TrailRenderer>())
            {
                tr.transform.SetParent(null, true);
                tr.emitting = false;
                tr.autodestruct = true;
            }
        }
    }
}
