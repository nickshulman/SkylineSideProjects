using System.Xml.Linq;

namespace ResourcesOrganizer.ResourcesModel
{
    public class ResourcesFile(string relativePath)
    {
        public string RelativePath { get; init; } = relativePath;
        public readonly List<ResourceEntry> Entries = new();
        public static ResourcesFile Read(string filePath, string relativePath)
        {
            var resourcesFile = new ResourcesFile(relativePath);
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
    }
}
