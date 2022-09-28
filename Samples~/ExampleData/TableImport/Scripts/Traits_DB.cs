using UnityEngine;
using System.Collections.Generic;
namespace Com.DefaultCompany.Table {
    public class Traits_DB : ScriptableObject {
        public static Traits_DB _instance;
        public static Traits_DB Instance {
            get {
                if (_instance == null)
                    _instance = Resources.Load<Traits_DB>(typeof(Traits_DB).Name);
                return _instance;
            }
        }

        public List<Traits> List;
    }
}