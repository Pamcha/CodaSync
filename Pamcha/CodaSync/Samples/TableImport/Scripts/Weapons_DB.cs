using UnityEngine;
using System.Collections.Generic;
namespace Com.DefaultCompany.Table {
    public class Weapons_DB : ScriptableObject {
        public static Weapons_DB _instance;
        public static Weapons_DB Instance {
            get {
                if (_instance == null)
                    _instance = Resources.Load<Weapons_DB>(typeof(Weapons_DB).Name);
                return _instance;
            }
        }

        public List<Weapons> List;
    }
}