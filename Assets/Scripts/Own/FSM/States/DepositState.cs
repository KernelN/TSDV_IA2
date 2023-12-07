using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IA.FSM.Miner
{
    public class DepositState : State
    {
        //Set values
        float depositDuration;
        //Run values
        float timer;
        
        public override List<Action> GetOnEnterBehaviours(params object[] parameters)
        {
            depositDuration = (float)parameters[0];
            
            timer = depositDuration;

            List<Action> behaviours = new List<Action>();
            
            behaviours.Add(() =>
            { 
             //Doesn't have behaviours, its just a setter
            });
            
            return behaviours;
        }

        public override List<Action> GetBehaviours(params object[] parameters)
        {
            float dt = (float)parameters[0];
            
            List<Action> behaviours = new List<Action>();
            
            behaviours.Add(() =>
            {
                timer += dt;
            if (timer >= depositDuration)
            {
                SetFlag((int)Flags.OnInventoryEmpty);
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