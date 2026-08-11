using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dujahit.Models
{
    // I need to place more enums here and move and organize them
    public enum ItemKind { Generic, Weapon, Armor, Consumable }

    public record WeaponDamage(string TypeId, string DiceId, int Count = 1, int Flat = 0);
}
