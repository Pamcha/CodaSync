using UnityEngine;
using System.Collections.Generic;
namespace Com.DefaultCompany.Table {
    public class Characters_DB : ScriptableObject {
        public static Characters_DB _instance;
        public static Characters_DB Instance {
            get {
                if (_instance == null)
                    _instance = Resources.Load<Characters_DB>(typeof(Characters_DB).Name);
                return _instance;
            }
        }

        public List<Characters> List;
    }
}