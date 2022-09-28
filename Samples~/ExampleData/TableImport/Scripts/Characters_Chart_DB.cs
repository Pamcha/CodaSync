using UnityEngine;
using System.Collections.Generic;
namespace Com.DefaultCompany.Table {
    public class Characters_Chart_DB : ScriptableObject {
        public static Characters_Chart_DB _instance;
        public static Characters_Chart_DB Instance {
            get {
                if (_instance == null)
                    _instance = Resources.Load<Characters_Chart_DB>(typeof(Characters_Chart_DB).Name);
                return _instance;
            }
        }

        public List<Characters_Chart> List;
    }
}