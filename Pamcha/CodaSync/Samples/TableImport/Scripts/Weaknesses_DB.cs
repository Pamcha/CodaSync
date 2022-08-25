using UnityEngine;
using System.Collections.Generic;
namespace Com.DefaultCompany.Table {
    public class Weaknesses_DB : ScriptableObject {
        public static Weaknesses_DB _instance;
        public static Weaknesses_DB Instance {
            get {
                if (_instance == null)
                    _instance = Resources.Load<Weaknesses_DB>(typeof(Weaknesses_DB).Name);
                return _instance;
            }
        }

        public List<Weaknesses> List;
    }
}