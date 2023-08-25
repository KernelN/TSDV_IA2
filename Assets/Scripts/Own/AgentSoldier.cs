using System;
using UnityEngine;

namespace IA.FSM.Soldier
{
    public class AgentSoldier : MonoBehaviour
    {
        /*
         ** Ir a un target
         ** Patrol
         ** Idle
         ** Atack
         ** Morir
         ** recivir daño
         ** retirarse
         ** chase
         */
        enum States
        {
            Idle,
            GoToTarget,
            Chase,
            Attack,
            Patrol,
            Flee,
            ReceiveDmg,
            Dead
        }

        enum Flags
        {
            OnStateComplete,
            OnTargetAssigned,
            OnSeeEnemy,
            OnNearEnemy,
            OnLostEnemy,
            OnKilledEnemy,
            OnDmgReceived,
            OnLowHealth,
            OnDeath
        }

        public Transform Enemy;
        public Transform Target;
        public bool goToTarget = false;
        public bool receivedDmg = false;
        public float enemyHealth = 10;
        public int health = 10;

        public float Lambda => health;
        public float NotLambda { get { return health; } }

        private float speed = 5;

        private FSM fsm;

        private void Start()
        {
            fsm = new FSM(Enum.GetValues(typeof(States)).Length, Enum.GetValues(typeof(Flags)).Length);

            fsm.SetRelation((int)States.Idle, (int)Flags.OnTargetAssigned, (int)States.GoToTarget);
            fsm.SetRelation((int)States.Idle, (int)Flags.OnSeeEnemy, (int)States.Chase);
            fsm.SetRelation((int)States.Idle, (int)Flags.OnNearEnemy, (int)States.Attack);
            fsm.SetRelation((int)States.Idle, (int)Flags.OnDmgReceived, (int)States.ReceiveDmg);

            fsm.SetRelation((int)States.GoToTarget, (int)Flags.OnStateComplete, (int)States.Idle);
            fsm.SetRelation((int)States.GoToTarget, (int)Flags.OnDmgReceived, (int)States.ReceiveDmg);

            fsm.SetRelation((int)States.Chase, (int)Flags.OnTargetAssigned, (int)States.GoToTarget);
            fsm.SetRelation((int)States.Chase, (int)Flags.OnLostEnemy, (int)States.Patrol);
            fsm.SetRelation((int)States.Chase, (int)Flags.OnKilledEnemy, (int)States.Idle);
            fsm.SetRelation((int)States.Chase, (int)Flags.OnNearEnemy, (int)States.Attack);
            fsm.SetRelation((int)States.Chase, (int)Flags.OnDmgReceived, (int)States.ReceiveDmg);

            fsm.SetRelation((int)States.Attack, (int)Flags.OnTargetAssigned, (int)States.GoToTarget);
            fsm.SetRelation((int)States.Attack, (int)Flags.OnSeeEnemy, (int)States.Chase);
            fsm.SetRelation((int)States.Attack, (int)Flags.OnKilledEnemy, (int)States.Idle);
            fsm.SetRelation((int)States.Attack, (int)Flags.OnDmgReceived, (int)States.ReceiveDmg);

            fsm.SetRelation((int)States.Patrol, (int)Flags.OnTargetAssigned, (int)States.GoToTarget);
            fsm.SetRelation((int)States.Patrol, (int)Flags.OnSeeEnemy, (int)States.Chase);
            fsm.SetRelation((int)States.Patrol, (int)Flags.OnNearEnemy, (int)States.Attack);
            fsm.SetRelation((int)States.Patrol, (int)Flags.OnDmgReceived, (int)States.ReceiveDmg);

            fsm.SetRelation((int)States.Flee, (int)Flags.OnLostEnemy, (int)States.Idle);
            fsm.SetRelation((int)States.Flee, (int)Flags.OnDmgReceived, (int)States.ReceiveDmg);

            fsm.SetRelation((int)States.ReceiveDmg, (int)Flags.OnStateComplete, (int)States.Idle);
            fsm.SetRelation((int)States.ReceiveDmg, (int)Flags.OnTargetAssigned, (int)States.GoToTarget);
            fsm.SetRelation((int)States.ReceiveDmg, (int)Flags.OnSeeEnemy, (int)States.Chase);
            fsm.SetRelation((int)States.ReceiveDmg, (int)Flags.OnLowHealth, (int)States.Flee);
            fsm.SetRelation((int)States.ReceiveDmg, (int)Flags.OnDeath, (int)States.Dead);


            fsm.SetRelation((int)States.Flee, (int)Flags.OnTargetAssigned, (int)States.GoToTarget);

            Action lowHealthCheck = () =>
            { if (health < 5)
                  fsm.SetFlag((int)Flags.OnLowHealth); };
            Action lowEnemyHealthCheck = () =>
            { if (enemyHealth <= 0)
                  fsm.SetFlag((int)Flags.OnKilledEnemy); };
            Action enemyOnSightCheck = () =>
            { if (Vector3.Distance(transform.position, Enemy.transform.position) < 3.0f)
            {
                speed = Mathf.Abs(speed);
                fsm.SetFlag((int)Flags.OnSeeEnemy);
            } };
            Action enemyOnReachCheck = () =>
            { if (Vector3.Distance(transform.position, Enemy.transform.position) < 1.0f)
            {
                speed = Mathf.Abs(speed);
                fsm.SetFlag((int)Flags.OnNearEnemy);
            } };
            Action enemyOutOfReachCheck = () =>
            { if (Vector3.Distance(transform.position, Enemy.transform.position) >= 1.0f)
            {
                speed = Mathf.Abs(speed);
                fsm.SetFlag((int)Flags.OnSeeEnemy);
            } };
            Action enemyLostCheck = () =>
            { if (Vector3.Distance(transform.position, Enemy.transform.position) > 5.0f)
            {
                speed = Mathf.Abs(speed);
                fsm.SetFlag((int)Flags.OnNearEnemy);
            } };

            // fsm.AddBehaviour((int)States.Idle, enemyOnSightCheck);
            // fsm.AddBehaviour((int)States.Idle, enemyOnReachCheck);
            //
            // fsm.AddBehaviour((int)States.GoToTarget, () =>
            // { transform.position += (Target.position - transform.position).normalized * speed * Time.deltaTime;
            //   if (Vector3.Distance(transform.position, Target.position) < 1.0f)
            //   {
            //       fsm.SetFlag((int)Flags.OnStateComplete);
            //   } });
            //
            // fsm.AddBehaviour((int)States.Chase,
            //     () =>
            //     { transform.position += (Enemy.position - transform.position).normalized * speed * Time.deltaTime; });
            // fsm.AddBehaviour((int)States.Chase, enemyOnReachCheck);
            // fsm.AddBehaviour((int)States.Chase, enemyLostCheck);
            //
            // fsm.AddBehaviour((int)States.Attack, () => { enemyHealth -= Time.deltaTime; });
            // fsm.AddBehaviour((int)States.Attack, lowEnemyHealthCheck);
            // fsm.AddBehaviour((int)States.Attack, enemyOutOfReachCheck);
            // fsm.AddBehaviour((int)States.Attack, enemyLostCheck);
            //
            //
            // fsm.AddBehaviour((int)States.Patrol, () =>
            // { transform.position += Vector3.right * Time.deltaTime * speed;
            //   if (Mathf.Abs(transform.position.x) > 10.0f)
            //       speed *= -1;
            //
            //   if (Vector3.Distance(transform.position, Enemy.transform.position) < 3.0f)
            //   {
            //       speed = Mathf.Abs(speed);
            //       fsm.SetFlag((int)Flags.OnSeeEnemy);
            //   }
            //
            //   if (Vector3.Distance(transform.position, Enemy.transform.position) < 1.0f)
            //   {
            //       speed = Mathf.Abs(speed);
            //       fsm.SetFlag((int)Flags.OnNearEnemy);
            //   } });
            //
            // fsm.AddBehaviour((int)States.Flee, () =>
            // { transform.position += (transform.position - Enemy.position).normalized * speed * Time.deltaTime;
            //
            //   if (Vector3.Distance(transform.position, Enemy.position) > 5.0f)
            //   {
            //       fsm.SetFlag((int)Flags.OnLostEnemy);
            //   } });
            //
            // fsm.AddBehaviour((int)States.ReceiveDmg, () =>
            // { health--;
            //   if (health < 5)
            //       fsm.SetFlag((int)Flags.OnLowHealth);
            //   else
            //       fsm.SetFlag((int)Flags.OnStateComplete);
            //   if (health <= 0)
            //       fsm.SetFlag((int)Flags.OnDeath); });
            //
            //
            // fsm.AddOnEnterBehaviour((int)States.Idle, () => Debug.Log("IDLE"));
            // fsm.AddOnEnterBehaviour((int)States.GoToTarget, () => Debug.Log("GO TO TARGET"));
            // fsm.AddOnEnterBehaviour((int)States.Chase, () => Debug.Log("CHASE"));
            // fsm.AddOnEnterBehaviour((int)States.Chase, lowHealthCheck);
            // fsm.AddOnEnterBehaviour((int)States.Chase, lowEnemyHealthCheck);
            // fsm.AddOnEnterBehaviour((int)States.Attack, () => Debug.Log("ATTACK"));
            // fsm.AddOnEnterBehaviour((int)States.Attack, lowHealthCheck);
            // fsm.AddOnEnterBehaviour((int)States.Attack, lowEnemyHealthCheck);
            // fsm.AddOnEnterBehaviour((int)States.Patrol, () => Debug.Log("PATROL"));
            // fsm.AddOnEnterBehaviour((int)States.Flee, () => Debug.Log("FLEE"));
            // fsm.AddOnEnterBehaviour((int)States.Dead, () => Debug.Log("F"));

            fsm.SetCurrentStateForced((int)States.Idle);
        }

        private void Update()
        {
            if (goToTarget)
            {
                fsm.SetFlag((int)Flags.OnTargetAssigned);
                goToTarget = false;
            }

            if (receivedDmg)
            {
                fsm.SetFlag((int)Flags.OnDmgReceived);
                receivedDmg = false;
            }

            fsm.Update();
        }
    }
}