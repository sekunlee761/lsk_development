using System;

namespace Core
{
    // Example concrete recipe implementation
    public class SampleRecipe : RecipeBase
    {
        // Example property specific to this recipe type
        public int Threshold { get; set; } = 100;

        public SampleRecipe()
        {
            Name = "SampleRecipe";
        }

        public override void Load(string source)
        {
            // Placeholder: implement loading from file/db as needed
            // For example, load threshold/parameters from file content
            // This is a minimal implementation for demonstration.
            if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("source is null or empty", nameof(source));
            // Simulate loading by setting timestamp
            Timestamp = DateTime.UtcNow;
        }

        public override void Save(string destination)
        {
            // Placeholder: implement save to file/db as needed
            if (string.IsNullOrWhiteSpace(destination)) throw new ArgumentException("destination is null or empty", nameof(destination));
            Timestamp = DateTime.UtcNow;
        }

        public override bool Validate(out string message)
        {
            // Use base validation first
            if (!base.Validate(out message)) return false;

            if (Threshold < 0)
            {
                message = "Threshold는 0 이상이어야 합니다.";
                return false;
            }

            message = "정상";
            return true;
        }
    }
}
