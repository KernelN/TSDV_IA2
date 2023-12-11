using System;
using System.Collections.Generic;
using UnityEngine;

namespace IA.FSM.States.Caravan
{
    public class PackLoadState : State
    {
        //Set values
        float loadDuration;
        //Run values
        float timer;
        
        public override List<Action> GetOnEnterBehaviours(params object[] parameters)
        {
            loadDuration = (float)parameters[0];
            
            timer = loadDuration;

            return new List<Action>(); //Doesn't have behaviours, its just a setter
        }

        public override List<Action> GetBehaviours(params object[] parameters)
        {
            float dt = (float)parameters[0];
            
            List<Action> behaviours = new List<Action>();
            
            behaviours.Add(() =>
            { timer += dt;
              if (timer >= loadDuration)
              {
                  Transition((int)IA.FSM.Caravan.Flags.OnInventoryFull);
              }
            });
            
            return behaviours;
        }

        public override List<Action> GetOnExitBehaviours(params object[] parameters)
        {
            return new List<Action>(); //noting to do
        }

        public override void Transition(int flag)
        {
            SetFlag?.Invoke(flag);
        }
    }
}