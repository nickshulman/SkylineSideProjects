using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NHibernate;
using NHibernate.Cfg;
using NHibernate.Mapping.Attributes;

namespace ResourcesOrganizer.DataModel
{
    public class SessionFactoryFactory
    {
        public ISessionFactory CreateSessionFactory(string filePath, bool createSchema)
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = filePath
            }.ToString();
            var cfg = new Configuration()
                .SetProperty(@"dialect", typeof(NHibernate.Dialect.SQLiteDialect).AssemblyQualifiedName)
                .SetProperty(@"connection.connection_string", connectionString)
                .SetProperty(@"connection.driver_class", typeof(global::NHibernate.Driver.SQLite20Driver).AssemblyQualifiedName)
                .SetProperty(@"connection.provider", typeof(global::NHibernate.Connection.DriverConnectionProvider).AssemblyQualifiedName);
            var hbmSerializer = new HbmSerializer
            {
                Validate = true
            };
            MemoryStream memoryStream = new MemoryStream();
            {
                using var stream = hbmSerializer.Serialize(typeof(Entity).Assembly);
                stream.CopyTo(memoryStream);
            }
            memoryStream.Seek(0, SeekOrigin.Begin);
            var text = new StreamReader(memoryStream).ReadToEnd();
            Console.Out.WriteLine("Configuration: {0}", text);
            memoryStream.Seek(0, SeekOrigin.Begin);
            cfg.AddInputStream(memoryStream);
            if (createSchema)
            {
                cfg.SetProperty(@"hbm2ddl.auto", @"create");
            }
            return cfg.BuildSessionFactory();
        }
    }
}
