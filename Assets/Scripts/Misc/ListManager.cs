using System.Collections.Generic;

namespace IA.Helpers
{
    public static class ListManager<T>
    {
        static List<List<T>> lists = new List<List<T>>();
        public static List<T> GetList()
        {
            if (lists.Count > 0)
            {
                List<T> list = lists[0];
                lists.RemoveAt(0);
                return list;
            }
        
            //Else
                return new List<T>();
        }
        public static List<T> GetList(T value)
        {
            List<T> list;
                
            if (lists.Count > 0)
            {
                list = lists[0];
                lists.RemoveAt(0);
            }
            else 
                list = new List<T>();

            list.Add(value);
            
            return list;
        }
        public static void ReturnList(List<T> list)
        {
            list.Clear();
            lists.Add(list);
        }
    }
}