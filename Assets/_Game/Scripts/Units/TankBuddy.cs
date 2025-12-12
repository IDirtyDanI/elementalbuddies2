using UnityEngine;

namespace ElementalBuddies
{
    public class TankBuddy : ElementalBuddy
    {
        [Header("Tank Stats")]
        public float TauntRadius = 4f;
        public float TauntDuration = 2f;
        public float DamageReduction = 0.15f;
        public float AuraDps = 8f;

        private float _lastAuraTime;

        protected override void Start()
        {
            base.Start();
            CurrentHP = 100f; 
        }

        public override void TakeDamage(float amount)
        {
            float reduced = amount * (1f - DamageReduction);
            base.TakeDamage(reduced);
        }

        protected override bool TryPerformAction()
        {
            // Taunt logic (controlled by Config.FireRate which should be 1/6 for 6s CD)
            Collider[] hits = Physics.OverlapSphere(transform.position, TauntRadius);
            bool tauntedAny = false;
            foreach (var hit in hits)
            {
                if (hit.CompareTag("Enemy"))
                {
                    var tauntable = hit.GetComponent<ITauntable>();
                    if (tauntable != null)
                    {
                        tauntable.Taunt(transform, TauntDuration);
                        tauntedAny = true;
                    }
                }
            }
            return tauntedAny;
        }

        protected override void Update()
        {
            base.Update();
            
            // Aura Logic (1 tick per second)
            if (Time.time >= _lastAuraTime + 1f)
            {
                _lastAuraTime = Time.time;
                Collider[] hits = Physics.OverlapSphere(transform.position, TauntRadius); 
                foreach (var hit in hits)
                {
                    if (hit.CompareTag("Enemy"))
                    {
                        var dmg = hit.GetComponent<IDamageable>();
                        if (dmg != null) dmg.TakeDamage(AuraDps);
                    }
                }
            }
        }
    }
}