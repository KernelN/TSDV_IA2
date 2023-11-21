using UnityEngine;
using UnityEngine.UI;

namespace IA.Game
{
    public class SimulationScreen : MonoBehaviour
    {
        [System.Serializable]
        class PopulationUI
        {
            public Text generationsCountTxt;
            public Text bestFitnessTxt;
            public Text avgFitnessTxt;
            public Text worstFitnessTxt;   
            
            string genCountText;
            string bestFitText;
            string avgFitText;
            string worstFitText;

            Population.PopulationManager popManager;
            int lastGeneration = 0;

            public void Set(Population.PopulationManager pop)
            {
                popManager = pop; 
                
                if (string.IsNullOrEmpty(genCountText))
                    genCountText = generationsCountTxt.text;
                if (string.IsNullOrEmpty(bestFitText))
                    bestFitText = bestFitnessTxt.text;
                if (string.IsNullOrEmpty(avgFitText))
                    avgFitText = avgFitnessTxt.text;
                if (string.IsNullOrEmpty(worstFitText))
                    worstFitText = worstFitnessTxt.text;

                generationsCountTxt.text = string.Format(genCountText, 0);
                bestFitnessTxt.text = string.Format(bestFitText, 0);
                avgFitnessTxt.text = string.Format(avgFitText, 0);
                worstFitnessTxt.text = string.Format(worstFitText, 0);
            }
            public void Update()
            {
                if (lastGeneration == popManager.generation) return;

                lastGeneration = popManager.generation;
                generationsCountTxt.text = string.Format(genCountText, popManager.generation);
                bestFitnessTxt.text = string.Format(bestFitText, popManager.bestFitness);
                avgFitnessTxt.text = string.Format(avgFitText, popManager.avgFitness);
                worstFitnessTxt.text = string.Format(worstFitText, popManager.worstFitness);
            }
        }
        
        [SerializeField] PopulationUI pop1UI;
        [SerializeField] PopulationUI pop2UI;
        public VerticalLayoutGroup[] layouts;
        public Text turnTxt;
        public Text timerTxt;
        public Slider timerSlider;
        public Button pauseBtn;
        public Button stopBtn;
        public InputField saveInput;
        public Button saveBtn;
        public GameObject startConfigurationScreen;

        string timerText;
        string turnText;
        int lastTurn = 0;
        Population.PopulationsManager popsManager;

        // Start is called before the first frame update
        void Start()
        {
            for (int i = 0; i < layouts.Length; i++)
            {
                layouts[i].enabled = false;
            }
            
            timerSlider.onValueChanged.AddListener(OnTimerChange);
            timerText = timerTxt.text;
            turnText = turnTxt.text;

            popsManager = Population.PopulationsManager.Instance;
            timerTxt.text = string.Format(timerText, popsManager.TurnsPerSecond);

            pop1UI.Set(popsManager.Pop1);
            pop2UI.Set(popsManager.Pop2);

            pauseBtn.onClick.AddListener(OnPauseButtonClick);
            stopBtn.onClick.AddListener(OnStopButtonClick);
            saveBtn.onClick.AddListener(OnSaveButtonClick);
        }
        void OnEnable()
        {
            if(!popsManager) return;
            pop1UI.Set(popsManager.Pop1);
            pop2UI.Set(popsManager.Pop2);
        }
        void LateUpdate()
        {
            if(!popsManager) return;
            if (lastTurn != popsManager.Turn)
            {
                lastTurn = popsManager.Turn;
                turnTxt.text = string.Format(turnText, popsManager.Turn);
            }
            pop1UI.Update();
            pop2UI.Update();
        }

        void OnTimerChange(float value)
        {
            if(value <= 0) return;
            popsManager.TurnsPerSecond = (int)value;
            timerTxt.text = string.Format(timerText, popsManager.TurnsPerSecond);
        }
        void OnPauseButtonClick()
        {
            popsManager.PauseSimulation();
        }
        void OnStopButtonClick()
        {
            popsManager.StopSimulation();
            this.gameObject.SetActive(false);
            startConfigurationScreen.SetActive(true);
            lastTurn = 0;
        }
        void OnSaveButtonClick()
        {
            if (saveInput.text.Length == 0 || saveInput.text == "")
            {
                Debug.LogError("INVALID SAVE NAME");
                return;
            }
            
            popsManager.SavePopulations(saveInput.text);
        }
    }
}