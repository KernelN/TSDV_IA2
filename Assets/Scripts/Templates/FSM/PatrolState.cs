using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UIController;
namespace IA.FSM.Villager
{
    public class PatrolState : State
    {
        public override List<Action> GetBehaviours(params object[] parameters)
        {
            Transform transform = parameters[0] as Transform;
            float speed = Convert.ToSingle(parameters[1]);
            GameObject Target = parameters[2] as GameObject;

            List<Action> behabiours = new List<Action>();

            behabiours.Add(() =>
            {
                transform.position += Vector3.right * Time.deltaTime * speed;
                if (Mathf.Abs(transform.position.x) > 10.0f)
                    speed *= -1;

                if (Vector3.Distance(transform.position, Target.transform.position) < 3.0f)
                {
                    speed = Mathf.Abs(speed);
                    Transition((int)Flags.OnSeeTarget);
                }
                if (Vector3.Distance(transform.position, Target.transform.position) < 1.0f)
                {
                    speed = Mathf.Abs(speed);
                    Transition((int)Flags.OnNearTarget);
                }
            });

            behabiours.Add(() => Debug.Log("PATROL"));

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
