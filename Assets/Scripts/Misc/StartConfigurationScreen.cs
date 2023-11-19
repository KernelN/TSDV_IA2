using UnityEngine;

namespace IA.Game
{
    public class StartConfigurationScreen : MonoBehaviour
    {
        [SerializeField] GameObject simulationScreen;

        public void StartSimulation()
        {
            Population.PopulationsManager.Instance.StartSimulation();
            gameObject.SetActive(false);
            simulationScreen.SetActive(true);
        }
    }
}