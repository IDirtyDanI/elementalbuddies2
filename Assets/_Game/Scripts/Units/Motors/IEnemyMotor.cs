using UnityEngine;

namespace ElementalBuddies
{
    // Bewegungs-Schnittstelle des EnemyBrain (Walddorf-Plan „Flieger-Mechanik“): NavAgentMotor kapselt den bisherigen
    // NavMeshAgent 1:1 (Bodengegner), FlyingMotor fliegt direkt aufs Ziel. Die Namen folgen dem NavMeshAgent, damit der
    // Brain-Code für Bodengegner unverändert bleibt.
    public interface IEnemyMotor
    {
        // Bewegung möglich (Agent: auf dem NavMesh; Flieger: immer, solange aktiv)
        bool IsReady { get; }
        bool IsStopped { get; set; }
        // Ist-Geschwindigkeit (Welt); Setzen (meist Vector3.zero) bremst sofort
        Vector3 Velocity { get; set; }
        // Höchsttempo (m/s)
        float Speed { get; set; }
        bool HasPath { get; }
        bool PathPending { get; }
        // Nächster Wegpunkt (Flieger: Ziel)
        Vector3 SteeringTarget { get; }
        // Körperradius in Weltmetern (Trefferbreite)
        float Radius { get; }
        bool SetDestination(Vector3 destination);
        void ResetPath();
        bool Warp(Vector3 position);
        // Verschieben um offset (Rückstoß)
        void Move(Vector3 offset);
        // Clients: Simulation abschalten (Position kommt per NetworkTransform)
        void DisableSimulation();
    }

    // Der bisherige NavMeshAgent als Motor (Bodengegner, Bosse, Ramme) – reine Weiterleitung, Verhalten unverändert.
    public sealed class NavAgentMotor : IEnemyMotor
    {
        private readonly UnityEngine.AI.NavMeshAgent _agent;
        private readonly Transform _transform;

        public NavAgentMotor(UnityEngine.AI.NavMeshAgent agent)
        {
            _agent = agent;
            _transform = agent != null ? agent.transform : null;
        }

        public UnityEngine.AI.NavMeshAgent Agent => _agent;

        public bool IsReady => _agent != null && _agent.isOnNavMesh;
        public bool IsStopped
        {
            get => _agent.isStopped;
            set => _agent.isStopped = value;
        }
        public Vector3 Velocity
        {
            get => _agent.velocity;
            set => _agent.velocity = value;
        }
        public float Speed
        {
            get => _agent.speed;
            set => _agent.speed = value;
        }
        public bool HasPath => _agent.hasPath;
        public bool PathPending => _agent.pathPending;
        public Vector3 SteeringTarget => _agent.steeringTarget;
        public float Radius => _agent != null ? _agent.radius * _transform.lossyScale.x : 0.4f;
        public bool SetDestination(Vector3 destination) => _agent.SetDestination(destination);
        public void ResetPath() => _agent.ResetPath();
        public bool Warp(Vector3 position) => _agent.Warp(position);
        public void Move(Vector3 offset) => _agent.Move(offset);
        public void DisableSimulation()
        {
            if (_agent != null && _agent.enabled) _agent.enabled = false;
        }
    }
}
