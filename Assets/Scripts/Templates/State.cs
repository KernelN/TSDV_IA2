using System;
using System.Collections.Generic;

namespace IA.FSM
{
    public abstract class State
    {
        public Action<int> SetFlag;
        public abstract List<Action> GetOnEnterBehaviours(params object[] parameters);
        public abstract List<Action> GetBehaviours(params object[] parameters);
        public abstract List<Action> GetOnExitBehaviours(params object[] parameters);
        public abstract void Transition(int flag);
    }
}

//public class A 
//{
//    public virtual void DoSomething(params object[] parameters) { }
//}
//public class B : A 
//{
//    public void Hello() 
//    {
//        DoSomething(3, "s", false);
//    }
//
//    public override void DoSomething(params object[] parameters)
//    {
//        if (parameters[0] is int) { }
//
//        TypeConverter typeConverter = TypeDescriptor.GetConverter(parameters[0]);
//
//        int num = Convert.ToInt32(parameters[0]);
//        string str = Convert.ToString(parameters[0]);
//        bool boo = Convert.ToBoolean(parameters[0]);
//        foreach (object item in parameters)
//        {
//
//        }
//    }
//}