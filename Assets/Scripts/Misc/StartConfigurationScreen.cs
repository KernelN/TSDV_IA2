using UnityEngine;
using UnityEngine.UI;

namespace IA.Game
{
    public class StartConfigurationScreen : MonoBehaviour
    {
        [SerializeField] GameObject simulationScreen;
        [SerializeField] InputField loadInput;

        public void LoadFile()
        {
            if (loadInput.text.Length == 0 || loadInput.text == "")
            {
                Debug.LogError("INVALID SAVE NAME");
                return;
            }
            
            Population.PopulationsManager.Instance.LoadPopulations(loadInput.text);
            gameObject.SetActive(false);
            simulationScreen.SetActive(true);
        }
        public void StartSimulation()
        {
            Population.PopulationsManager.Instance.StartSimulation();
            gameObject.SetActive(false);
            simulationScreen.SetActive(true);
        }
    }
}