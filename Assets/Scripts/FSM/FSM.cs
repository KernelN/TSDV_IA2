using System;
using System.Collections.Generic;

namespace IA.FSM
{
    public class FSM
    {
        public int currentStateIndex = 0;
        Dictionary<int, States.State> states;
        Dictionary<int, Func<object[]>> enterParameters;
        Dictionary<int, Func<object[]>> mainParameters;
        Dictionary<int, Func<object[]>> exitParameters;
        private int[,] relations;

        public FSM(int states, int flags)
        {
            currentStateIndex = -1;
            relations = new int[states, flags];
            for (int i = 0; i < states; i++)
            {
                for (int j = 0; j < flags; j++)
                {
                    relations[i, j] = -1;
                }
            }
            
            this.states = new Dictionary<int, States.State>();
            enterParameters = new Dictionary<int, Func<object[]>>();
            mainParameters = new Dictionary<int, Func<object[]>>();
            exitParameters = new Dictionary<int, Func<object[]>>();
        }

        public void SetCurrentStateForced(int state)
        {
            if(states.ContainsKey(currentStateIndex))
                foreach (Action OnExit in states[currentStateIndex].GetOnExitBehaviours(exitParameters[currentStateIndex]?.Invoke()))
                    OnExit?.Invoke();
            
            currentStateIndex = state;
            
            if(states.ContainsKey(currentStateIndex))
                foreach (Action OnEnter in states[currentStateIndex].GetOnEnterBehaviours(enterParameters[currentStateIndex]?.Invoke()))
                    OnEnter?.Invoke();
        }

        public void SetRelation(int sourceState, int flag, int destinationState)
        {
            relations[sourceState, flag] = destinationState;
        }

        public void SetFlag(int flag)
        {
            if (relations[currentStateIndex, flag] != -1)
            {
                foreach (Action OnExit in states[currentStateIndex].GetOnExitBehaviours(exitParameters[currentStateIndex]?.Invoke()))
                    OnExit?.Invoke();

                currentStateIndex = relations[currentStateIndex, flag];

                foreach (Action OnEnter in states[currentStateIndex].GetOnEnterBehaviours(enterParameters[currentStateIndex]?.Invoke()))
                    OnEnter?.Invoke();
            }
        }

        public void AddState<T>(int stateIndex, Func<object[]> mainParams = null,
            Func<object[]> enterParams = null, Func<object[]> exitParams = null) where T : States.State, new()
        {
            if (!states.ContainsKey(stateIndex)) 
            {
                States.State newState = new T();
                newState.SetFlag += SetFlag;
                states.Add(stateIndex, newState);
                mainParameters.Add(stateIndex, mainParams);
                enterParameters.Add(stateIndex, enterParams);
                exitParameters.Add(stateIndex, exitParams);
            }
        }

        public void Update()
        {
            if (states.ContainsKey(currentStateIndex))
            {
                foreach (Action behaviour in states[currentStateIndex].GetBehaviours(mainParameters[currentStateIndex]?.Invoke()))
                {
                    behaviour?.Invoke();
                }
            }
        }
    }
}