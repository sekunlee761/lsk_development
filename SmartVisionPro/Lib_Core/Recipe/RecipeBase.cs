using System;
using System;

namespace Core
{
    // Base class for all recipes. Use inheritance to implement specific recipe types.
    public abstract class RecipeBase
    {
        // Unique identifier for the recipe (could be GUID or user defined)
        public string Id { get; set; }

        // Human readable name
        public string Name { get; set; }

        // Optional version string
        public string Version { get; set; }

        // Creation or last-modified time
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        protected RecipeBase()
        {
            Id = Guid.NewGuid().ToString();
            Name = "NewRecipe";
            Version = "1.0";
        }

        // Validate recipe content. Throw exception or return false when invalid.
        public virtual bool Validate(out string message)
        {
            // Basic validation: ensure name exists
            if (string.IsNullOrWhiteSpace(Name))
            {
                message = "레시피 이름이 비어 있습니다.";
                return false;
            }

            message = "정상";
            return true;
        }

        // Load recipe data from a source (implementation defined by derived classes)
        public abstract void Load(string source);

        // Save recipe data to a destination (implementation defined by derived classes)
        public abstract void Save(string destination);

        // Optional: provide a shallow clone method for switching/modifying active recipe safely
        public virtual RecipeBase ShallowClone()
        {
            return (RecipeBase)this.MemberwiseClone();
        }
    }
}
