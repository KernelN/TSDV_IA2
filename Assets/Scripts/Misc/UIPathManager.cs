using UnityEngine;

namespace IA.FSM.UI
{
    public class UIPathManager : MonoBehaviour
    {
        [SerializeField] Pathfinding.PathManager controller;
        
        public void RefreshTerrainFromColliders()
        {
            if (controller == null) return;
            controller.QueueAllTerrainCellsForRescan();
            controller.SetCellsForRecalculation();
        }
    }
}
