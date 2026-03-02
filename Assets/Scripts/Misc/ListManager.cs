using System.Collections.Generic;

namespace IA.Helpers
{
    public static class ListManager<T>
    {
        static List<List<T>> lists = new List<List<T>>();
        public static List<T> GetList()
        {
            if(lists.Count > 0) return lists[0];
        
            //Else
                return new List<T>();
        }
        public static List<T> GetList(T value)
        {
            List<T> list = lists.Count > 0 ? lists[0] : new List<T>();

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