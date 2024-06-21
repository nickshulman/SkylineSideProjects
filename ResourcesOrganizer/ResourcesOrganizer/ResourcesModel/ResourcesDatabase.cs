using NHibernate;
using NHibernate.Context;
using ResourcesOrganizer.DataModel;

namespace ResourcesOrganizer.ResourcesModel
{
    public class ResourcesDatabase
    {
        private Dictionary<string, List<ResourcesFile>> _resourcesFiles = new Dictionary<string, List<ResourcesFile>>();

        public void AddResourcesFiles(string versionTag, IEnumerable<ResourcesFile> files)
        {
            _resourcesFiles.Add(versionTag, files.ToList());
        }

        public void Read(string databasePath)
        {
            using var sessionFactory = SessionFactoryFactory.CreateSessionFactory(databasePath, false);
            using var session = sessionFactory.OpenStatelessSession();

            Read(session);
        }

        public void Read(IStatelessSession session)
        {
            var invariantResources = session.Query<InvariantResource>()
                .ToDictionary(resource => resource.Id!.Value, resource => resource.GetKey());
            var localizedResources = session.Query<LocalizedResource>()
                .ToLookup(localizedResource => localizedResource.InvariantResourceId);
            foreach (var versionTagGroup in session.Query<ResourceLocation>().GroupBy(location => location.VersionTag!))
            {
                var files = new List<ResourcesFile>();
                foreach (var fileGroup in versionTagGroup.GroupBy(location => location.FilePath!))
                {
                    var resourcesFile = new ResourcesFile(fileGroup.Key);

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
                    files.Add(resourcesFile);
                }
                _resourcesFiles.Add(versionTagGroup.Key, files);
            }
        }

        public void Save(string path)
        {
            using var sessionFactory = SessionFactoryFactory.CreateSessionFactory(path, true);
            using var session = sessionFactory.OpenStatelessSession();
            var transaction = session.BeginTransaction();
            var invariantResources = SaveInvariantResources(session);
            SaveLocalizedResources(session, invariantResources);
            foreach (var entry in _resourcesFiles)
            {
                foreach (var resourceFile in entry.Value)
                {
                    SaveResourcesFile(session, invariantResources, entry.Key, resourceFile);
                }
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
            foreach (var entryGroup in _resourcesFiles.Values.SelectMany(list => list)
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

        private void SaveResourcesFile(IStatelessSession session, Dictionary<InvariantResourceKey, long> invariantResources, string versionTag, ResourcesFile resourcesFile)
        {
            int sortIndex = 0;
            var filePath = resourcesFile.RelativePath.ToString();
            foreach (var entry in resourcesFile.Entries)
            {
                var resourceLocation = new ResourceLocation
                {
                    FilePath = filePath,
                    InvariantResourceId = invariantResources[entry.Invariant],
                    Name = entry.Name,
                    SortIndex = ++sortIndex,
                    VersionTag = versionTag
                };
                session.Insert(resourceLocation);
            }
        }

        public List<InvariantResourceKey> GetInvariantResources()
        {
            var invariantResources = _resourcesFiles.Values.SelectMany(list => list)
                .SelectMany(resources => resources.Entries.Select(entry => entry.Invariant)).ToList();
            invariantResources.Sort();
            return invariantResources;
        }
    }
}
