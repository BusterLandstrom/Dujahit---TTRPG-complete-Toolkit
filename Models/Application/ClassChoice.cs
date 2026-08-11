using System.Collections.Generic;

namespace Dujahit.Models.Application
{
    public class ChoiceOption
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";

        public ChoiceOption() { }

        public ChoiceOption(string id, string name, string description = "")
        {
            Id = id;
            Name = name;
            Description = description;
        }
    }
}
