using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NHibernate.Mapping.Attributes;

namespace ResourcesOrganizer.DataModel
{
    [Class(Lazy = false)]
    public class ResourceLocation : Entity<ResourceLocation>
    {
        [Property]
        public long InvariantResourceId { get; set; }
        [Property]
        public string? FilePath { get; set; }
        [Property]
        public int SortIndex { get; set; }
        [Property]
        public string? Name { get; set; }
        [Property]
        public string? VersionTag { get; set; }
    }
}
