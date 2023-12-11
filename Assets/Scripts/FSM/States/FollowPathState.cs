using System;
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
        int failedFlag;
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
            failedFlag = (int)parameters[6];

            Vector3 targetPos = Vector3.zero;
            if (parameters.Length > 7)
                targetPos = (Vector3)parameters[7];
            
            currentNode = 0;
            
            List<Action> behaviours = new List<Action>();

            behaviours.Add(() =>
            { 
              Pathfinding.Voronoi.VoronoiAStarPathfinder pathFinder;
              pathFinder = pathManager.GetPathfinder(pathfinderIndex);
            
              lock (pathFinder.grid.grid)
              {
                  if (parameters.Length > 7)
                      path = pathManager.GetPathfinder(pathfinderIndex).FindPath(startPos, targetPos);
                  else
                      path = pathManager.GetPathfinder(pathfinderIndex).FindPathToPOI(startPos);
              }
            
              if (path == null)
                  Transition(failedFlag);
              else if (path.Count == 1)
                  Transition(reachedFlag); //If start and end are the same, the path will be of 1 node
            });
            
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
            
            //If path follower has an exit behaviour, but failed to find path, exit
            if(path == null) return new List<Action>();
            if(path.Count < 2) return new List<Action>();
            
            Action<Vector2Int> destinyGridPos = (Action<Vector2Int>)parameters[0];
            
            List<Action> behaviours = new List<Action>();
            
            behaviours.Add(() =>
            { if (path == null) return;
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