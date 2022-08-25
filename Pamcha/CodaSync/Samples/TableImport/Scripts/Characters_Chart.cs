using UnityEngine;
using System.Collections.Generic;

namespace Com.DefaultCompany.Table {
    public class Characters_Chart : ScriptableObject {
         public string Name;
         public float Health;
        [TextArea] public string Info;
         public Traits[] Traits;
         public Weaknesses[] Weaknesses;
         public Characters Parent;
         public Armor[] Armor;
         public Weapons Weapons;
         public Sprite CharacterSprites;
         public string Description;
         public float Armor_Attack_Bonus;
    }
}