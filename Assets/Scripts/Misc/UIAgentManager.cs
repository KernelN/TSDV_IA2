using UnityEngine;

namespace IA.FSM.UI
{
public class UIAgentManager : MonoBehaviour
{
    [SerializeField] AgentManager controller;

    public void SpawnMiner()
    {
        controller.SpawnMiner();
    }
    public void SpawnCaravan()
    {
        controller.SpawnCaravan();
    }
    public void ToggleEmergency()
    {
        controller.SetEmergency();
    }
}
}