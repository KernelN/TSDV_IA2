using UnityEngine;

namespace IA.FSM.UI
{
    public class UIPathManager : MonoBehaviour
    {
        [SerializeField] Pathfinding.PathManager controller;
        
        [ContextMenu("Refresh Terrain From Colliders")]
        public void RefreshTerrainFromColliders()
        {
            if (controller == null) return;
            controller.QueueAllTerrainCellsForRescan();
            controller.SetCellsForRecalculation();
        }
    }
}
