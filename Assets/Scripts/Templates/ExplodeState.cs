using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace IA.FSM.Villager
{
    public class ExplodeState : State
    {
        public override List<Action> GetBehaviours(params object[] parameters)
        {
            List<Action> behaviours = new List<Action>();
            behaviours.Add(() =>
            {
                Debug.Log("BOOM");
                Transition((int)Flags.OnExplodeSuccess);
            });
            return behaviours;
        }

        public override List<Action> GetOnEnterBehaviours(params object[] parameters)
        {
            List<Action> behaviours = new List<Action>();
            behaviours.Add(() =>
            {
                Debug.Log("Adios mundo cruel");
            });
            return behaviours;

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
