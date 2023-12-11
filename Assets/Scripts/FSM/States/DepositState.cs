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
        bool deposited;
        
        public override List<Action> GetOnEnterBehaviours(params object[] parameters)
        {
            depositDuration = (float)parameters[0];
            depositFlag = (int)parameters[1];
            
            timer = 0;
            deposited = false;

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
                deposited = true;
                SetFlag(depositFlag);
            }
            });
            
            return behaviours;
        }

        public override List<Action> GetOnExitBehaviours(params object[] parameters)
        {
            if(parameters == null) return new List<Action>();
            if(parameters.Length <= 0) return new List<Action>();
            
            Action depositSuccess = (Action)parameters[0];
            
            List<Action> behaviours = new List<Action>();
            
            behaviours.Add(() =>
            {
                if(deposited)
                    depositSuccess?.Invoke();
                deposited = false;
            });
            
            return behaviours;
        }

        public override void Transition(int flag)
        {
            SetFlag?.Invoke(flag);
        }
    }
}