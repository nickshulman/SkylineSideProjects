using NHibernate;
using ResourcesOrganizer.DataModel;

namespace ResourcesOrganizer.ResourcesModel
{
    public class ResourcesDatabase
    {
        public Dictionary<string, ResourcesFile> ResourcesFiles { get; }= [];

        public static ResourcesDatabase ReadFile(string path)
        {
            var database = new ResourcesDatabase();
            var extension = Path.GetExtension(path);
            if (extension.Equals(".db", StringComparison.OrdinalIgnoreCase))
            {
                database.ReadDatabase(path);
            }
            else if (extension.Equals(".resx", StringComparison.OrdinalIgnoreCase))
            {
                var resourcesFile = ResourcesFile.Read(path);
                database.ResourcesFiles.Add(path, resourcesFile);
            }
            else if (Directory.Exists(path))
            {
                database.AddFolder(path, path);
            }
            return database;
        }

        public void ReadDatabase(string databasePath)
        {
            using var sessionFactory = SessionFactoryFactory.CreateSessionFactory(databasePath, false);
            using var session = sessionFactory.OpenStatelessSession();

            ReadDatabase(session);
        }

        public void ReadDatabase(IStatelessSession session)
        {
            var invariantResources = session.Query<InvariantResource>()
                .ToDictionary(resource => resource.Id!.Value, resource => resource.GetKey());
            var localizedResources = session.Query<LocalizedResource>()
                .ToLookup(localizedResource => localizedResource.InvariantResourceId);
            foreach (var fileGroup in session.Query<ResourceLocation>().GroupBy(location => location.FilePath!))
            {
                var resourcesFile = new ResourcesFile();

                foreach (var resourceLocation in fileGroup.OrderBy(loc => loc.SortIndex))
                {
                    var entry = new ResourceEntry(resourceLocation.Name!,
                        invariantResources[resourceLocation.InvariantResourceId]);
                    foreach (var localizedResource in localizedResources[resourceLocation.InvariantResourceId])
                    {
                        entry.LocalizedValues.Add(localizedResource.Language!, localizedResource.Value!);
                    }
                    resourcesFile.Entries.Add(entry);
                }
                ResourcesFiles.Add(fileGroup.Key, resourcesFile);
            }
        }

        public void SaveAtomic(string path)
        {
            var tempFile = Path.GetTempFileName();
            Save(tempFile);
            File.Replace(tempFile, path, null);
        }

        public void Save(string path)
        {
            using var sessionFactory = SessionFactoryFactory.CreateSessionFactory(path, true);
            using var session = sessionFactory.OpenStatelessSession();
            var transaction = session.BeginTransaction();
            var invariantResources = SaveInvariantResources(session);
            SaveLocalizedResources(session, invariantResources);
            foreach (var resourceFile in ResourcesFiles)
            {
                SaveResourcesFile(session, invariantResources, resourceFile.Key, resourceFile.Value);
            }
            transaction.Commit();
        }

        private Dictionary<InvariantResourceKey, long> SaveInvariantResources(IStatelessSession session)
        {
            var result = new Dictionary<InvariantResourceKey, long>();
            foreach (var key in GetInvariantResources())
            {
                var invariantResource = new InvariantResource
                {
                    Comment = key.Comment,
                    Name = key.Name,
                    Type = key.Type,
                    Value = key.Value
                };
                session.Insert(invariantResource);
                result.Add(key, invariantResource.Id!.Value);
            }

            return result;
        }

        private void SaveLocalizedResources(IStatelessSession session,
            IDictionary<InvariantResourceKey, long> invariantResources)
        {
            foreach (var entryGroup in ResourcesFiles.Values
                         .SelectMany(resources => resources.Entries)
                         .GroupBy(entry => entry.Invariant))
            {
                var invariantResourceId = invariantResources[entryGroup.Key];
                foreach (var localizedEntryGroup in entryGroup.SelectMany(entry => entry.LocalizedValues).GroupBy(kvp => kvp.Key))
                {
                    var translations = localizedEntryGroup.Select(kvp => kvp.Value).Distinct().ToList();
                    if (translations.Count > 1)
                    {
                        Console.Error.WriteLine("{0} was translated into {1} as all of the following: {2}", entryGroup.Key, localizedEntryGroup.Key, string.Join(Environment.NewLine, translations));
                    }

                    var localizedResource = new LocalizedResource
                    {
                        InvariantResourceId = invariantResourceId,
                        Language = localizedEntryGroup.Key,
                        Value = translations[0]
                    };
                    session.Insert(localizedResource);
                }
            }
        }

        private void SaveResourcesFile(IStatelessSession session, Dictionary<InvariantResourceKey, long> invariantResources, string filePath, ResourcesFile resourcesFile)
        {
            int sortIndex = 0;
            foreach (var entry in resourcesFile.Entries)
            {
                var resourceLocation = new ResourceLocation
                {
                    FilePath = filePath,
                    InvariantResourceId = invariantResources[entry.Invariant],
                    Name = entry.Name,
                    SortIndex = ++sortIndex,
                };
                session.Insert(resourceLocation);
            }
        }

        public List<InvariantResourceKey> GetInvariantResources()
        {
            var invariantResources = ResourcesFiles.Values
                .SelectMany(resources => resources.Entries.Select(entry => entry.Invariant)).ToList();
            invariantResources.Sort();
            return invariantResources;
        }

        public void Add(ResourcesDatabase database)
        {
            foreach (var resourcesFile in database.ResourcesFiles)
            {
                if (ResourcesFiles.TryGetValue(resourcesFile.Key, out var existing))
                {
                    existing.Add(resourcesFile.Value);
                }
                else
                {
                    ResourcesFiles.Add(resourcesFile.Key, resourcesFile.Value.Clone());
                }
            }
        }

        public void Subtract(ResourcesDatabase database)
        {
            foreach (var resourcesFile in database.ResourcesFiles.ToList())
            {
                if (!ResourcesFiles.TryGetValue(resourcesFile.Key, out var existing))
                {
                    continue;
                }

                existing.Subtract(resourcesFile.Value);
                if (existing.Entries.Count == 0)
                {
                    database.ResourcesFiles.Remove(resourcesFile.Key);
                }
            }
        }

        public void Intersect(ResourcesDatabase database)
        {
            foreach (var entry in ResourcesFiles.ToList())
            {
                if (!database.ResourcesFiles.TryGetValue(entry.Key, out var other))
                {
                    ResourcesFiles.Remove(entry.Key);
                    continue;
                }
                entry.Value.Intersect(other);
                if (entry.Value.Entries.Count == 0)
                {
                    ResourcesFiles.Remove(entry.Key);
                }
            }
        }

        public void AddFolder(string fullPath, string relativePath)
        {
            var directoryInfo = new DirectoryInfo(fullPath);
            foreach (var file in directoryInfo.GetFiles())
            {
                if (!ResourcesFile.IsInvariantResourceFile(file.FullName))
                {
                    continue;
                }

                var resourcesFile = ResourcesFile.Read(file.FullName);
                ResourcesFiles[Path.Combine(relativePath, file.Name)] = resourcesFile;
            }

            foreach (var folder in directoryInfo.GetDirectories())
            {
                AddFolder(folder.FullName, Path.Combine(relativePath, folder.Name));
            }
        }
    }
}
