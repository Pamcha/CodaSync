using UnityEngine;
using System.Collections.Generic;
namespace Com.DefaultCompany.Table {
    public class Armor_DB : ScriptableObject {
        public static Armor_DB _instance;
        public static Armor_DB Instance {
            get {
                if (_instance == null)
                    _instance = Resources.Load<Armor_DB>(typeof(Armor_DB).Name);
                return _instance;
            }
        }

        public List<Armor> List;
    }
}