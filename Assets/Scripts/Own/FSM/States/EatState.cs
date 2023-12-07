using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IA.FSM.Miner
{
    public class EatState : State
    {
        //Set values
        float eatDuration;
        Vector2Int foodStoragePos;
        //Run values
        float timer;
        
        public override List<Action> GetOnEnterBehaviours(params object[] parameters)
        {
            eatDuration = (float)parameters[0];
            foodStoragePos = (Vector2Int)parameters[1];
            
            timer = eatDuration;

            return new List<Action>(); //Doesn't have behaviours, its just a setter
        }

        public override List<Action> GetBehaviours(params object[] parameters)
        {
            float dt = (float)parameters[0];
            Func<Vector2Int, bool> TryEat = (Func<Vector2Int, bool>)parameters[1];
            
            List<Action> behaviours = new List<Action>();
            
            behaviours.Add(() =>
            {
                timer += dt;
            if (timer >= eatDuration)
            {
                if (TryEat.Invoke(foodStoragePos))
                {
                    timer = 0;
                    Transition((int)Flags.OnAte);
                }
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