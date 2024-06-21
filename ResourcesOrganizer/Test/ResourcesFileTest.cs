using ResourcesOrganizer.ResourcesModel;

namespace Test
{
    [TestClass]
    public class ResourcesFileTest : AbstractUnitTest
    {
        [TestMethod]
        public void TestResourcesFile()
        {
            string runDirectory = TestContext.TestRunDirectory!;
            SaveManifestResources(typeof(ResourcesFileTest), runDirectory);
            var file = ResourcesFile.Read(Path.Combine(runDirectory, "Resources.resx"), "Resources.resx");
            Assert.AreNotEqual(0, file.Entries.Count);
        }

        [TestMethod]
        public void TestResourcesDatabase()
        {
            string runDirectory = TestContext.TestRunDirectory!;
            SaveManifestResources(typeof(ResourcesFileTest), runDirectory);
            var file = ResourcesFile.Read(Path.Combine(runDirectory, "Resources.resx"), "Resources.resx");
            var resourcesDatabase = new ResourcesDatabase();
            resourcesDatabase.AddResourcesFiles("test", [file]);
            Assert.AreNotEqual(0, resourcesDatabase.GetInvariantResources().Count);
            var dbPath = Path.Combine(runDirectory, "resources.db");
            resourcesDatabase.Save(dbPath);
            var compare = new ResourcesDatabase();
            compare.Read(dbPath);
            
            CollectionAssert.AreEqual(resourcesDatabase.GetInvariantResources(), compare.GetInvariantResources());
        }
    }
}
