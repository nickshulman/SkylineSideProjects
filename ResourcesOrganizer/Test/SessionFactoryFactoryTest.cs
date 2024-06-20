using ResourcesOrganizer.DataModel;

namespace Test
{
    [TestClass]
    public class SessionFactoryFactoryTest
    {
        public TestContext TestContext { get; set; }

        [TestMethod]
        public void TestCreateSessionFactory()
        {
            var sessionFactoryFactory = new SessionFactoryFactory();
            Assert.IsNotNull(TestContext.TestRunResultsDirectory);
            Assert.IsTrue(Directory.Exists(TestContext.TestRunResultsDirectory));
            var filePath = Path.Combine(TestContext.TestRunResultsDirectory, "test.db");

            using var sessionFactory = sessionFactoryFactory.CreateSessionFactory(filePath, true);
            Assert.IsTrue(File.Exists(filePath));
        }
    }
}