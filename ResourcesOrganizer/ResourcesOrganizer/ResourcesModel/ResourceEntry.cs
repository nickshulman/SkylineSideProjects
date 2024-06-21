namespace ResourcesOrganizer.ResourcesModel
{
    public class ResourceEntry(string name, InvariantResourceKey key)
    {
        public string Name { get; } = name;
        public InvariantResourceKey Invariant { get; } = key;
        public Dictionary<string, string> LocalizedValues { get; } = [];

        public ResourceEntry Clone()
        {
            var clone = new ResourceEntry(Name, Invariant);
            foreach (var entry in LocalizedValues)
            {
                clone.LocalizedValues.Add(entry.Key, entry.Value);
            }

            return clone;
        }
    }
}
