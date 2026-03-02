using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace IA.Game
{
    public class SimulationScreen : MonoBehaviour
    {
        [System.Serializable]
        class PopulationUI
        {
            public Text stageTxt;
            public Text generationsCountTxt;
            public Text populationTxt;
            public Text bestFitnessTxt;
            public Text avgFitnessTxt;
            public Text worstFitnessTxt;   
            
            string stageText;
            string genCountText;
            string populationText;
            string bestFitText;
            string avgFitText;
            string worstFitText;

            Population.PopulationManager popManager;
            int lastGeneration = 0;

            public void Set(Population.PopulationManager pop)
            {
                popManager = pop; 
                
                if (string.IsNullOrEmpty(stageText))
                    stageText = stageTxt.text;
                if (string.IsNullOrEmpty(genCountText))
                    genCountText = generationsCountTxt.text;
                if (string.IsNullOrEmpty(populationText))
                    populationText = populationTxt.text;
                if (string.IsNullOrEmpty(bestFitText))
                    bestFitText = bestFitnessTxt.text;
                if (string.IsNullOrEmpty(avgFitText))
                    avgFitText = avgFitnessTxt.text;
                if (string.IsNullOrEmpty(worstFitText))
                    worstFitText = worstFitnessTxt.text;

                stageTxt.text = stageText + " " + nameof(Population.Stage.Tutorial);
                generationsCountTxt.text = string.Format(genCountText, 0);
                populationTxt.text = string.Format(populationText, 0);
                bestFitnessTxt.text = string.Format(bestFitText, 0);
                avgFitnessTxt.text = string.Format(avgFitText, 0);
                worstFitnessTxt.text = string.Format(worstFitText, 0);
            }
            public void Update()
            {
                if (lastGeneration == popManager.generation) return;
                
                stageTxt.text = stageText + " " + popManager.Stage;
                lastGeneration = popManager.generation;
                generationsCountTxt.text = string.Format(genCountText, popManager.generation);
                populationTxt.text = string.Format(populationText, popManager.populationCount);
                bestFitnessTxt.text = string.Format(bestFitText, popManager.bestFitness);
                avgFitnessTxt.text = string.Format(avgFitText, popManager.avgFitness);
                worstFitnessTxt.text = string.Format(worstFitText, popManager.worstFitness);
            }
        }

        [System.Serializable]
        struct TpsPerPopulation
        {
            public float tps;
            public float pops;
        }

        
        [SerializeField] PopulationUI pop1UI;
        [SerializeField] PopulationUI pop2UI;
        public VerticalLayoutGroup[] layouts;
        public Text turnTxt;
        public Text timerTxt;
        public Slider timerSlider;
        float currentTimeRatio = 1;
        [SerializeField] List<TpsPerPopulation> tpsPerPopulation;
        AnimationCurve tpsPerPopulationCurve;
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
                layouts[i].enabled = false;
            
            timerSlider.onValueChanged.AddListener(OnTimerChange);
            currentTimeRatio = timerSlider.value;
            tpsPerPopulationCurve = new AnimationCurve();
            Keyframe[] keys = new Keyframe[tpsPerPopulation.Count];
            for (int i = 0; i < tpsPerPopulation.Count; i++) 
                keys[i] = new Keyframe(tpsPerPopulation[i].pops, tpsPerPopulation[i].tps, 0, 0);
            tpsPerPopulationCurve.keys = keys;
            timerText = timerTxt.text;
            turnText = turnTxt.text;

            popsManager = Population.PopulationsManager.Instance;
            popsManager.SimulationStarted += UpdateTime;
            popsManager.GenerationChanged += UpdateTime;
            timerTxt.text = string.Format(timerText, popsManager.TurnsPerSecond);

            pop1UI.Set(popsManager.Pop1);
            pop2UI.Set(popsManager.Pop2);

            pauseBtn.onClick.AddListener(OnPauseButtonClick);
            stopBtn.onClick.AddListener(OnStopButtonClick);
            saveBtn.onClick.AddListener(OnSaveButtonClick);
        }
        void UpdateTime()
        {
            int pop = popsManager.Pop1.populationCount + popsManager.Pop2.populationCount;
            int tps = (int)(tpsPerPopulationCurve.Evaluate(pop) * currentTimeRatio);
            if(tps < 5) tps = 5;
            popsManager.TurnsPerSecond = tps;
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
            timerTxt.text = string.Format(timerText, popsManager.TurnsPerSecond);
        }

        void OnTimerChange(float value)
        {
            if(value <= 0) return;
            currentTimeRatio = value;
            UpdateTime();
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