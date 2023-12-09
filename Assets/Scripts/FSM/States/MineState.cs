using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IA.FSM.States.Miner
{
    public class MineState : State
    {
        //Set values
        float mineInterval;
        int actionsPerFood;
        int maxMinerals;
        Vector2Int minePos;
        //Run values
        float timer;
        int mineralCount;
        
        public override List<Action> GetOnEnterBehaviours(params object[] parameters)
        {
            mineInterval = (float)parameters[0];
            actionsPerFood = (int)parameters[1];
            maxMinerals = (int)parameters[2];
            minePos = (Vector2Int)parameters[3];
            
            timer = mineInterval;

            return new List<Action>(); //Doesn't have behaviours, its just a setter
        }

        public override List<Action> GetBehaviours(params object[] parameters)
        {
            float dt = (float)parameters[0];
            Func<Vector2Int, bool> TryMine = (Func<Vector2Int, bool>)parameters[1];
            
            List<Action> behaviours = new List<Action>();
            
            behaviours.Add(() =>
            {
                timer += dt;
            if (timer >= mineInterval)
            {
                if (TryMine.Invoke(minePos))
                {
                    timer = 0;
                    mineralCount++;
                    if (mineralCount >= maxMinerals)
                    {
                        mineralCount = 0;
                        Transition((int)IA.FSM.Miner.Flags.OnInventoryFull);
                    }
                    else if (mineralCount % actionsPerFood == 0)
                    {
                        Transition((int)IA.FSM.Miner.Flags.OnHungry);
                    }
                }
                else
                {
                    SetFlag((int)IA.FSM.Miner.Flags.OnMineEmpty);
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