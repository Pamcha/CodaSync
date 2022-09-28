using UnityEngine;
using System.Collections.Generic;
namespace Com.DefaultCompany.Table {
    public class Simple_views_of_Characters_DB : ScriptableObject {
        public static Simple_views_of_Characters_DB _instance;
        public static Simple_views_of_Characters_DB Instance {
            get {
                if (_instance == null)
                    _instance = Resources.Load<Simple_views_of_Characters_DB>(typeof(Simple_views_of_Characters_DB).Name);
                return _instance;
            }
        }

        public List<Simple_views_of_Characters> List;
    }
}