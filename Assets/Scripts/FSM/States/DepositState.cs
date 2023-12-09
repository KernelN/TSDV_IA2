using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IA.FSM.States
{
    public class DepositState : State
    {
        //Set values
        float depositDuration;
        int depositFlag;
        //Run values
        float timer;
        
        public override List<Action> GetOnEnterBehaviours(params object[] parameters)
        {
            depositDuration = (float)parameters[0];
            depositFlag = (int)parameters[1];
            
            timer = 0;

            return new List<Action>();
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
                SetFlag(depositFlag);
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