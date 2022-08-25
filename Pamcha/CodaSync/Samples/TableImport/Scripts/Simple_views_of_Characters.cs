using UnityEngine;
using System.Collections.Generic;

namespace Com.DefaultCompany.Table {
    public class Simple_views_of_Characters : ScriptableObject {
         public string Name;
         public float Health;
        [TextArea] public string Info;
         public Characters Parent;
    }
}