using UnityEngine;

namespace ElementalBuddies
{
    // Optionaler Tod-Clip (Vorbereitung Etappe 2, z. B. Absturz der Flieger): EnemyBrain löst beim Tod das Modell
    // (Animator-Objekt) vom Gegner, setzt den Trigger „Die“ und hängt diese Komponente an. Der Gegner selbst wird wie
    // bisher sofort entfernt (Wellenzählung, Netz-Despawn unverändert); das Modell spielt den Clip als reine Optik zu Ende
    // und verschwindet danach. Flieger fallen dabei bis auf den Boden.
    public class EnemyCorpse : MonoBehaviour
    {
        private float _until;
        private bool _fall;
        private float _vy;
        private float _groundY = float.NegativeInfinity;

        public void Init(float duration, bool fall)
        {
            _until = Time.time + Mathf.Max(0.05f, duration);
            _fall = fall;
            if (fall && Physics.Raycast(transform.position + Vector3.up * 0.2f, Vector3.down, out RaycastHit hit, 60f,
                    FlyingMotor.SurfaceMask, QueryTriggerInteraction.Ignore))
                _groundY = hit.point.y;
            // Kollider des Modells (falls vorhanden) sollen nichts mehr treffen/blockieren
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            // Treffer-Aufblitzen (HitFlash per MaterialPropertyBlock) nicht am Kadaver hängen lassen (#375)
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                r.SetPropertyBlock(null);
                for (int i = 0; i < r.sharedMaterials.Length; i++) r.SetPropertyBlock(null, i);
            }
        }

        void Update()
        {
            if (Time.time >= _until)
            {
                Destroy(gameObject);
                return;
            }
            if (!_fall) return;
            _vy -= 18f * Time.deltaTime;
            Vector3 p = transform.position;
            p.y = Mathf.Max(_groundY, p.y + _vy * Time.deltaTime);
            if (p.y <= _groundY) _vy = 0f;
            transform.position = p;
        }
    }
}
