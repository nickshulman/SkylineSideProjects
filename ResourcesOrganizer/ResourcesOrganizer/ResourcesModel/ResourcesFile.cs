using System.Xml.Linq;

namespace ResourcesOrganizer.ResourcesModel
{
    public class ResourcesFile
    {
        public List<ResourceEntry> Entries { get; private set; } = [];
        public string? XmlContent { get; set; }
        public static ResourcesFile Read(string filePath, string relativePath)
        {
            string? fileKey;
            if (Path.GetFileNameWithoutExtension(relativePath)
                .EndsWith("Resources", StringComparison.OrdinalIgnoreCase))
            {
                fileKey = null;
            }
            else
            {
                fileKey = relativePath;
            }
            var resourcesFile = new ResourcesFile();
            var entriesByName = new Dictionary<string, ResourceEntry>();
            var otherElements = new List<XElement>();
            var document = XDocument.Load(filePath);
            foreach (var element in document.Root!.Elements())
            {
                if (element.Name != "data")
                {
                    otherElements.Add(element);
                    continue;
                }
                var key = new InvariantResourceKey
                {
                    Comment = element.Element("comment")?.Value,
                    Name = (string)element.Attribute("name")!,
                    File = fileKey,
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
            document.Root.RemoveAll();
            document.Root.Add(otherElements.Cast<object>().ToArray());
            var stringWriter = new StringWriter();
            document.Save(stringWriter);
            resourcesFile.XmlContent = stringWriter.ToString();

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
            var clone = (ResourcesFile) MemberwiseClone();
            clone.Entries = [..Entries.Select(entry => entry.Clone())];
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

        public void ExportResx(Stream stream, string? language)
        {
            var document = XDocument.Load(new StringReader(XmlContent));
            foreach (var entry in Entries)
            {
                string value;
                if (string.IsNullOrEmpty(language))
                {
                    value = entry.Invariant.Value;
                }
                else
                {
                    if (!entry.LocalizedValues.TryGetValue(language, out value))
                    {
                        continue;
                    }
                }

                var data = new XElement("data");
                data.SetAttributeValue("name", entry.Name);
                data.Add(new XElement("value", value));
                if (entry.Invariant.Comment != null)
                {
                    data.Add(new XElement("comment", entry.Invariant.Comment));
                }
                document.Root!.Add(data);
            }
            document.Save(stream);
        }
    }
}
