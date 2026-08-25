using Dujahit.Models.Database;

namespace Dujahit.ViewModels
{
    public class PlayerOptionViewModel : ViewModelBase
    {
        public string CharacterId { get; }
        public string CharacterName { get; }
        public string PlayerName { get; }
        public int Level { get; }
        public int CurrentHp { get; }
        public int MaxHp { get; }
        public string Race { get; }
        public string Class { get; }
        public bool IsOnline { get; set; }
        public bool Concentration { get; set; }
        public string? ColorHex { get; set; }
        public string? TokenImagePath { get; set; }
        public int InitiativeMod { get; set; }
        public int ConSaveBonus { get; set; }
        public int ArmorClass { get; set; }
        public string OnlineStatus => IsOnline ? "● Online" : "○ Offline";

        public MonsterOption? Monster { get; set; }

        public string DisplayLabel => Monster != null
            ? $"{CharacterName} ({Race}) - {PlayerName}"
            : $"{CharacterName} (Lv{Level} {Race} {Class}) - {PlayerName}";

        public PlayerOptionViewModel(
            string characterId, string characterName, string playerName,
            int level, int currentHp, int maxHp, string race, string @class)
        {
            CharacterId = characterId;
            CharacterName = characterName;
            PlayerName = playerName;
            Level = level;
            CurrentHp = currentHp;
            MaxHp = maxHp;
            Race = race;
            Class = @class;
        }
    }
}
