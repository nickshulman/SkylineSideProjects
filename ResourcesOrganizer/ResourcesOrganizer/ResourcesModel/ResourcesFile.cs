using System.Xml.Linq;

namespace ResourcesOrganizer.ResourcesModel
{
    public class ResourcesFile
    {
        public readonly List<ResourceEntry> Entries = [];
        public static ResourcesFile Read(string filePath)
        {
            var resourcesFile = new ResourcesFile();
            var entriesByName = new Dictionary<string, ResourceEntry>();
            foreach (var element in XDocument.Load(filePath).Root!.Elements("data"))
            {
                var key = new InvariantResourceKey
                {
                    Comment = element.Element("comment")?.Value,
                    Name = (string)element.Attribute("name")!,
                    Value = element.Element("value")!.Value,
                    Type = (string?)element.Attribute("type")
                };
                if (entriesByName.ContainsKey(key.Name))
                {
                    Console.Error.WriteLine("Duplicate name {0} in file {1}", key.Name, filePath);
                    continue;
                }

                var entry = new ResourceEntry(key.Name, key);
                resourcesFile.Entries.Add(entry);
                entriesByName.Add(entry.Name, entry);
            }

            var baseName = Path.GetFileNameWithoutExtension(filePath);
            var baseExtension = Path.GetExtension(filePath);
            foreach (var file in Directory.GetFiles(Path.GetDirectoryName(filePath)!))
            {
                if (!baseExtension.Equals(Path.GetExtension(file), StringComparison.InvariantCultureIgnoreCase))
                {
                    continue;
                }
                var fileWithoutExtension = Path.GetFileNameWithoutExtension(file);
                var languageExtension = Path.GetExtension(fileWithoutExtension);
                if (string.IsNullOrEmpty(languageExtension))
                {
                    continue;
                }

                var language = languageExtension.Substring(1);
                if (!baseName.Equals(Path.GetFileNameWithoutExtension(fileWithoutExtension),
                        StringComparison.InvariantCultureIgnoreCase))
                {
                    continue;
                }

                foreach (var element in XDocument.Load(file).Root!.Elements("data"))
                {
                    var name = (string)element.Attribute("name")!;
                    if (!entriesByName.TryGetValue(name, out var entry))
                    {
                        continue;
                    }

                    entry.LocalizedValues[language] = element.Element("value")!.Value;
                }
            }

            return resourcesFile;
        }

        public void Add(ResourcesFile resourcesFile)
        {
            foreach (var newEntry in resourcesFile.Entries)
            {
                var existing = FindEntry(newEntry.Name);
                if (existing == null)
                {
                    Entries.Add(newEntry);
                    continue;
                }

                if (!Equals(existing.Invariant, newEntry.Invariant))
                {
                    continue;
                }
                foreach (var localizedValue in newEntry.LocalizedValues)
                {
                    if (!existing.LocalizedValues.ContainsKey(localizedValue.Key))
                    {
                        existing.LocalizedValues.Add(localizedValue.Key, localizedValue.Value);
                    }
                }
            }
        }

        public void Subtract(ResourcesFile resourcesFile)
        {
            var keys = resourcesFile.Entries.Select(entry => entry.Invariant).ToHashSet();
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                if (keys.Contains(Entries[i].Invariant))
                {
                    Entries.RemoveAt(i);
                }
            }
        }

        public void Intersect(ResourcesFile resourcesFile)
        {
            var keys = resourcesFile.Entries.Select(entry => entry.Invariant).ToHashSet();
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                if (!keys.Contains(Entries[i].Invariant))
                {
                    Entries.RemoveAt(i);
                }
            }
        }

        public ResourceEntry? FindEntry(string name)
        {
            return Entries.FirstOrDefault(entry => entry.Name == name);
        }

        public ResourcesFile Clone()
        {
            var clone = new ResourcesFile();
            clone.Entries.AddRange(Entries.Select(entry=>entry.Clone()));
            return clone;
        }

        public static bool IsInvariantResourceFile(string path)
        {
            var extension = Path.GetExtension(path);
            if (!".resx".Equals(extension, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var baseName = Path.GetFileNameWithoutExtension(path);
            var baseExtension = Path.GetExtension(baseName);
            if (string.IsNullOrEmpty(baseExtension))
            {
                return true;
            }

            if (baseExtension.Length <= 3 || baseExtension[3] == '-')
            {
                return false;
            }

            return true;
        }
    }
}
