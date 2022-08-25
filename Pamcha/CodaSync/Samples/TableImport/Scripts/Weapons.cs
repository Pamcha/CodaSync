using UnityEngine;
using System.Collections.Generic;

namespace Com.DefaultCompany.Table {
    public class Weapons : ScriptableObject {
         public string Name;
         public float Attack_Bonus;
         public float Defense_Bonus;
         public Sprite EquipementSprites;
         public float UseCount;
         public string type;
    }
}