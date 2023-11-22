using System.Collections.Generic;
using IA.Math;
using UnityEngine;

namespace IA.Game
{
    [System.Serializable]
    public class Map
    {
        [Header("Set Values")]
        public int width;
        public int height;

        [Header("Runtime Values")]
        public List<Vec2> food;
        public List<Agent.AgentBase> population1;
        public List<Agent.AgentBase> population2;
        public Dictionary<int, List<Agent.AgentBase>> foodTaken = new Dictionary<int, List<Agent.AgentBase>>();
    }
}