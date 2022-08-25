using UnityEngine;
using System.Collections.Generic;
namespace Com.DefaultCompany.Table {
    public class Enemies_DB : ScriptableObject {
        public static Enemies_DB _instance;
        public static Enemies_DB Instance {
            get {
                if (_instance == null)
                    _instance = Resources.Load<Enemies_DB>(typeof(Enemies_DB).Name);
                return _instance;
            }
        }

        public List<Enemies> List;
    }
}