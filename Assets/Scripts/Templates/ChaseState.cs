using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IA.FSM.Villager
{
    public class ChaseState : State
    {
        public override List<Action> GetBehaviours(params object[] parameters)
        {
            Transform transform = parameters[0] as Transform;
            float speed = Convert.ToSingle(parameters[1]);
            GameObject Target = parameters[2] as GameObject;

            List<Action> behabiours = new List<Action>();

            behabiours.Add(() =>
            {

                transform.position += (Target.transform.position - transform.position).normalized * speed * Time.deltaTime;
                if (Vector3.Distance(transform.position, Target.transform.position) < 1.0f)
                {
                    Transition((int)Flags.OnNearTarget);
                }
                if (Vector3.Distance(transform.position, Target.transform.position) > 5.0f)
                {
                    Transition((int)Flags.OnLostTarget);
                }
            }
            );

            behabiours.Add(() => Debug.Log("CHASE"));

            return behabiours;
        }

        public override List<Action> GetOnEnterBehaviours(params object[] parameters)
        {
            return new List<Action>();

        }

        public override List<Action> GetOnExitBehaviours(params object[] parameters)
        {
            return new List<Action>();

        }

        public override void Transition(int flag)
        {
            SetFlag?.Invoke(flag);

        }
    }
}
