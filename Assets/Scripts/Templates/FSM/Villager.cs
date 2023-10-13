using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
namespace IA.FSM.Villager
{
    internal enum States
    {
        Patrol,
        Chase,
        Explode,
        Dead
    }

    internal enum Flags
    {
        OnSeeTarget,
        OnNearTarget,
        OnLostTarget,
        OnExplodeSuccess
    }
}

namespace IA.FSM.Villager
{
    public class Villager : MonoBehaviour
    {
        /*
         * Ir a un target
         * Patrol
         * Idle
         * Atack
         * Morir
         * recivir daño
         * retirarse
         * chase
         */

        public GameObject Target;

        private float speed = 5;

        private FSM fsm;

        private void Start()
        {
            fsm = new FSM(Enum.GetValues(typeof(States)).Length, Enum.GetValues(typeof(Flags)).Length);

            fsm.SetRelation((int)States.Patrol, (int)Flags.OnSeeTarget, (int)States.Chase);
            fsm.SetRelation((int)States.Patrol, (int)Flags.OnNearTarget, (int)States.Explode);

            fsm.SetRelation((int)States.Chase, (int)Flags.OnLostTarget, (int)States.Patrol);
            fsm.SetRelation((int)States.Chase, (int)Flags.OnNearTarget, (int)States.Explode);

            fsm.SetRelation((int)States.Explode, (int)Flags.OnExplodeSuccess, (int)States.Dead);

            fsm.AddState<PatrolState>((int)States.Patrol, () => new object[] { transform, speed, Target });
            fsm.AddState<ChaseState>((int)States.Chase, () => new object[] { transform, speed, Target });
            fsm.AddState<ExplodeState>((int)States.Explode);
            fsm.AddState<DeadState>((int)States.Dead);

            fsm.SetCurrentStateForced((int)States.Patrol);
        }

        private void Update()
        {
            fsm.Update();
        }
    }
}