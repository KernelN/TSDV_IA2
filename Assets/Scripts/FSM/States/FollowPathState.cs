using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IA.FSM.States
{
    public class FollowPathState : State
    {
        //Set values
        List<Pathfinding.Grid.PathNode> path;
        float speed;
        float nodeDiameter;
        int reachedFlag;
        //Run values
        int currentNode;
        
        public override List<Action> GetOnEnterBehaviours(params object[] parameters)
        {
            Pathfinding.PathManager pathManager = (Pathfinding.PathManager)parameters[0];
            int pathfinderIndex = (int)parameters[1];
            Vector3 startPos = (Vector3)parameters[2];
            speed = (float)parameters[3];
            nodeDiameter = (float)parameters[4];
            reachedFlag = (int)parameters[5];

            Vector3 targetPos = Vector3.zero;
            if (parameters.Length > 6)
                targetPos = (Vector3)parameters[6];
            
            currentNode = 0;
            
            List<Action> behaviours = new List<Action>();

            behaviours.Add(() =>
            { 
              if (parameters.Length > 6)
                  path = pathManager.GetPathfinder(pathfinderIndex).FindPath(startPos, targetPos);
              else
                  path = pathManager.GetPathfinder(pathfinderIndex).FindPathToPOI(startPos); });
            
            return behaviours;
        }

        public override List<Action> GetBehaviours(params object[] parameters)
        {
            float dt = (float)parameters[0];
            Vector3 pos = (Vector3)parameters[1];
            Action<Vector3> getNewPos = (Action<Vector3>)parameters[2];
            
            List<Action> behaviours = new List<Action>();
            
            behaviours.Add(() =>
            {
                if (currentNode < path.Count - 1)
                {
                    Vector3 movement = path[currentNode + 1].worldPos - pos;
                    pos += movement.normalized * (speed * dt);

                    if (Vector3.Distance(pos, path[currentNode + 1].worldPos) < nodeDiameter)
                        currentNode++;
                    
                    getNewPos?.Invoke(pos);
                }
                else
                {
                    getNewPos?.Invoke(path[^1].worldPos);
                    Transition(reachedFlag);
                }
            });
            
            return behaviours;
        }

        public override List<Action> GetOnExitBehaviours(params object[] parameters)
        {
            //Not all path followers need this
            if(parameters == null) return new List<Action>();
            if(parameters.Length <= 0) return new List<Action>();
            
            Action<Vector2Int> destinyGridPos = (Action<Vector2Int>)parameters[0];
            
            List<Action> behaviours = new List<Action>();
            
            behaviours.Add(() =>
            {
                destinyGridPos?.Invoke(path[^1].gridPos);
            });
            
            return behaviours;
        }

        public override void Transition(int flag)
        {
            SetFlag?.Invoke(flag);
        }
    }
}