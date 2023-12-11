using System;
using IA.FSM.States.Caravan;
using UnityEngine;

namespace IA.FSM.Caravan
{
    
    public enum States
    {
        Idle,
        GoToSource,
        PackLoad,
        GoToDeposit,
        Deposit,
        GoToSafePlace,
        Hide,
        
        _count
    }
    
    public enum Flags
    {
        OnNearTarget,
        OnMoveFailed,
        OnInventoryEmpty,
        OnInventoryFull,
        OnEmergency,
        
        OnMapUpdated,
        
        _count
    }
    
    [Serializable]
    public class ACaravan
    {
        [Header("Set Values")]
        public float moveSpeed;
        public float loadDuration;
        public float depositDuration;
        Transform transform;
        //[Header("Runtime Values")]
        FSM fsm;
        States lastStateBeforeEmergency;
        float deltaTime;
        Vector3 pos;
        Vector3 nextPos;
        Vector2Int minePos;

        public Action<Vector2Int> onDepositSuccess;
        
        //Unity Methods
        public void Set(Pathfinding.PathManager pathManager, int pathfinderIndex, 
                    Transform safePlace, Transform depositPlace, Transform transform)
        {
            this.transform = transform;
            pos = transform.position;
            nextPos = pos;
            
            fsm = new FSM((int)States._count, (int)Flags._count);

            fsm.SetRelation((int)States.Idle, (int)Flags.OnMapUpdated, (int)States.GoToDeposit);
            
            fsm.SetRelation((int)States.GoToSource, (int)Flags.OnNearTarget, (int)States.PackLoad);
            fsm.SetRelation((int)States.GoToSource, (int)Flags.OnMoveFailed, (int)States.Idle);
            fsm.SetRelation((int)States.GoToSource, (int)Flags.OnEmergency, (int)States.GoToSafePlace);
            fsm.SetRelation((int)States.GoToSource, (int)Flags.OnMapUpdated, (int)States.GoToSource);

            fsm.SetRelation((int)States.PackLoad, (int)Flags.OnInventoryFull, (int)States.GoToDeposit);
            fsm.SetRelation((int)States.PackLoad, (int)Flags.OnEmergency, (int)States.GoToSafePlace);

            fsm.SetRelation((int)States.GoToDeposit, (int)Flags.OnNearTarget, (int)States.Deposit);
            fsm.SetRelation((int)States.GoToDeposit, (int)Flags.OnMoveFailed, (int)States.Idle);
            fsm.SetRelation((int)States.GoToDeposit, (int)Flags.OnEmergency, (int)States.GoToSafePlace);
            fsm.SetRelation((int)States.GoToDeposit, (int)Flags.OnMapUpdated, (int)States.GoToDeposit);

            fsm.SetRelation((int)States.Deposit, (int)Flags.OnInventoryEmpty, (int)States.GoToSource);
            fsm.SetRelation((int)States.Deposit, (int)Flags.OnEmergency, (int)States.GoToSafePlace);

            fsm.SetRelation((int)States.GoToSafePlace, (int)Flags.OnNearTarget, (int)States.Hide);
            fsm.SetRelation((int)States.GoToSafePlace, (int)Flags.OnMoveFailed, (int)States.Idle);
            fsm.SetRelation((int)States.GoToSafePlace, (int)Flags.OnMapUpdated, (int)States.GoToSafePlace);

            Action<Vector3> OnGotNewPos = 
                newPos => nextPos = newPos;
            Action<Vector2Int> GetMineGridPos = 
                gridPos => minePos = gridPos;
            Action OnDepositSuccess = 
                () => onDepositSuccess?.Invoke(minePos);
            float nodeDiamater = pathManager.GetNodeDiameter(pathfinderIndex);
            Vector3 safePos = safePlace.position;
            Vector3 sourcePos = depositPlace.position;
            
            fsm.AddState<IA.FSM.States.FollowPathState>(
                (int)States.GoToSource,
                () => new object[] { deltaTime, pos, OnGotNewPos },
                () => new object[] 
                { pathManager, pathfinderIndex, pos, moveSpeed, nodeDiamater,
                    (int)Flags.OnNearTarget, (int)Flags.OnMoveFailed, sourcePos }
                );
            fsm.AddState<IA.FSM.States.FollowPathState>(
                (int)States.GoToSafePlace,
                () => new object[] { deltaTime, pos, OnGotNewPos },
                () => new object[] 
                { pathManager, pathfinderIndex, pos, moveSpeed, nodeDiamater,
                    (int)Flags.OnNearTarget, (int)Flags.OnMoveFailed, safePos }
                );
            fsm.AddState<IA.FSM.States.FollowPathState>(
                (int)States.GoToDeposit,
                () => new object[] { deltaTime, pos, OnGotNewPos },
                () => new object[]
                { pathManager, pathfinderIndex, pos, moveSpeed, nodeDiamater,
                    (int)Flags.OnNearTarget, (int)Flags.OnMoveFailed },
                () => new object[] { GetMineGridPos }
                );
            fsm.AddState<PackLoadState>(
                (int)States.PackLoad,
                () => new object[] { deltaTime },
                () => new object[] { loadDuration });
            fsm.AddState<IA.FSM.States.DepositState>(
                (int)States.Deposit,
                () => new object[] { deltaTime },
                () => new object[] { depositDuration, (int)Flags.OnInventoryEmpty },
                () => new object[] { OnDepositSuccess });

            fsm.SetCurrentStateForced((int)States.Idle);
        }
        public void UpdateFSM(float dt)
        {
            deltaTime = dt;
            fsm.Update();
        }
        public void UpdateTransform()
        {
            pos = nextPos;
            transform.position = pos;
        }
        
        //Methods
        public void Emergency()
        {
            //Save the right state before emergency, so it doesn't do actions in the middle of nowhere
            //(This could be done in the fsm, polish later)
            switch ((States)fsm.currentStateIndex)
            {
                case States.Deposit:
                    lastStateBeforeEmergency = States.GoToDeposit;
                    break;
                case States.PackLoad:
                    lastStateBeforeEmergency = States.GoToSource;
                    break;
                default:
                    lastStateBeforeEmergency = (States)fsm.currentStateIndex;
                    break;                    
            }
            fsm.SetFlag((int)Flags.OnEmergency);
        }
        public void EmergencyOver()
        {
            fsm.SetCurrentStateForced((int)lastStateBeforeEmergency);
        }
        public void OnMapUpdated()
        {
            fsm.SetFlag((int)Flags.OnMapUpdated);
        }
        public void OnNoMoreMines()
        {
            fsm.SetCurrentStateForced((int)States.Idle);
        }
    }
}