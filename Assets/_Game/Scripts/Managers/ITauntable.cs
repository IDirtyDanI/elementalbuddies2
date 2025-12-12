using UnityEngine;

namespace ElementalBuddies
{
    public interface ITauntable
    {
        void Taunt(Transform target, float duration);
    }
}