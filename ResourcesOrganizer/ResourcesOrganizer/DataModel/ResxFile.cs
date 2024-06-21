using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NHibernate.Mapping.Attributes;

namespace ResourcesOrganizer.DataModel
{
    [Class(Lazy = false)]
    public class ResxFile : Entity<ResxFile>
    {
        [Property]
        public string? FilePath { get; set; }
        [Property]
        public string? XmlContent { get; set; }
    }
}
