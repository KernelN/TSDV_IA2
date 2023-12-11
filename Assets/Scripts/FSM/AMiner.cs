using System;
using IA.FSM.States.Miner;
using UnityEngine;

namespace IA.FSM.Miner
{
    public enum States
    {
        Idle,
        GoToMine,
        Mine,
        Eat,
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
        OnHungry,
        OnAte,
        OnMineEmpty,
        OnEmergency,
        
        OnMapUpdated,
        
        _count
    }
    
    [Serializable]
    public class AMiner
    {
        [Header("Set Values")]
        public float moveSpeed;
        public float mineInterval;
        public float eatDuration;
        public float depositDuration;
        Transform transform;
        //[Header("Runtime Values")]
        FSM fsm;
        States lastStateBeforeEmergency;
        float deltaTime;
        Vector3 pos;
        Vector3 nextPos;
        
        public Vector2Int minePos { get; private set; }
        public bool hasMine { get; private set; }

        
        //Unity Methods
        public void Set(Pathfinding.PathManager pathManager, int pathfinderIndex, 
                    Transform safePlace, Transform depositPlace, Transform transform,
                        Func<Vector2Int, bool> TryMine, Func<Vector2Int, bool> TryEat)
        {
            this.transform = transform;
            pos = transform.position;
            
            fsm = new FSM((int)States._count, (int)Flags._count);

            fsm.SetRelation((int)States.Idle, (int)Flags.OnMapUpdated, (int)States.GoToMine);

            fsm.SetRelation((int)States.GoToMine, (int)Flags.OnNearTarget, (int)States.Mine);
            fsm.SetRelation((int)States.GoToMine, (int)Flags.OnMineEmpty, (int)States.GoToMine);
            fsm.SetRelation((int)States.GoToMine, (int)Flags.OnMoveFailed, (int)States.GoToDeposit);
            fsm.SetRelation((int)States.GoToMine, (int)Flags.OnEmergency, (int)States.GoToSafePlace);

            fsm.SetRelation((int)States.Mine, (int)Flags.OnInventoryFull, (int)States.GoToDeposit);
            fsm.SetRelation((int)States.Mine, (int)Flags.OnHungry, (int)States.Eat);
            fsm.SetRelation((int)States.Mine, (int)Flags.OnMineEmpty, (int)States.GoToMine);
            fsm.SetRelation((int)States.Mine, (int)Flags.OnEmergency, (int)States.GoToSafePlace);

            fsm.SetRelation((int)States.Eat, (int)Flags.OnAte, (int)States.Mine);
            fsm.SetRelation((int)States.Eat, (int)Flags.OnMineEmpty, (int)States.GoToDeposit);
            fsm.SetRelation((int)States.Eat, (int)Flags.OnEmergency, (int)States.GoToSafePlace);

            fsm.SetRelation((int)States.GoToDeposit, (int)Flags.OnNearTarget, (int)States.Deposit);
            fsm.SetRelation((int)States.GoToDeposit, (int)Flags.OnMoveFailed, (int)States.GoToDeposit);
            fsm.SetRelation((int)States.GoToDeposit, (int)Flags.OnEmergency, (int)States.GoToSafePlace);

            fsm.SetRelation((int)States.Deposit, (int)Flags.OnInventoryEmpty, (int)States.GoToMine);
            fsm.SetRelation((int)States.Deposit, (int)Flags.OnEmergency, (int)States.GoToSafePlace);

            fsm.SetRelation((int)States.GoToSafePlace, (int)Flags.OnNearTarget, (int)States.Hide);
            //fsm.SetRelation((int)States.GoToSafePlace, (int)Flags.OnMoveFailed, (int)States.Idle);

            Action<Vector3> OnGotNewPos = newPos => 
                nextPos = newPos;
            Action<Vector2Int> GetMineGridPos = gridPos =>
            { minePos = gridPos; hasMine = true; };
            float nodeDiamater = pathManager.GetNodeDiameter(pathfinderIndex);
            Vector3 safePos = safePlace.position;
            Vector3 depositPos = depositPlace.position;
            
            fsm.AddState<IA.FSM.States.FollowPathState>(
                (int)States.GoToDeposit,
                () => new object[] { deltaTime, pos, OnGotNewPos },
                () => new object[] 
                { pathManager, pathfinderIndex, pos, moveSpeed, nodeDiamater,
                        (int)Flags.OnNearTarget, (int)Flags.OnMoveFailed, depositPos }
                );
            fsm.AddState<IA.FSM.States.FollowPathState>(
                (int)States.GoToSafePlace,
                () => new object[] { deltaTime, pos, OnGotNewPos },
                () => new object[] 
                { pathManager, pathfinderIndex, pos, moveSpeed, nodeDiamater,
                        (int)Flags.OnNearTarget, (int)Flags.OnMoveFailed, safePos }
                );
            fsm.AddState<IA.FSM.States.FollowPathState>(
                (int)States.GoToMine,
                () => new object[] { deltaTime, pos, OnGotNewPos },
                () => new object[] 
                { pathManager, pathfinderIndex, pos, moveSpeed, nodeDiamater,
                    (int)Flags.OnNearTarget, (int)Flags.OnMoveFailed },
                () => new object[] { GetMineGridPos }
                );
            fsm.AddState<MineState>(
                (int)States.Mine,
                () => new object[] { deltaTime, TryMine },
                () => new object[] { mineInterval, 3, 15, minePos }); //3 actions per food, 15 max minerals
            fsm.AddState<EatState>(
                (int)States.Eat,
                () => new object[] { deltaTime, TryEat },
                () => new object[] { eatDuration, minePos });
            fsm.AddState<IA.FSM.States.DepositState>(
                (int)States.Deposit,
                () => new object[] { deltaTime },
                () => new object[] { depositDuration, (int)Flags.OnInventoryEmpty });

            fsm.SetCurrentStateForced((int)States.GoToMine);
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
                case States.Eat:
                case States.Mine:
                    lastStateBeforeEmergency = States.GoToMine;
                    break;
                case States.Deposit:
                    lastStateBeforeEmergency = States.GoToDeposit;
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
        public void OnMineEmpty(Vector2Int emptyMinePos)
        {
            if (emptyMinePos != minePos) return;
            fsm.SetFlag((int)Flags.OnMineEmpty);
        }
    }
}