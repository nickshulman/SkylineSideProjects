using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NHibernate.Mapping.Attributes;

namespace ResourcesOrganizer.DataModel
{
    [Class(Lazy = false)]
    public class LocalizedResource : Entity<LocalizedResource>
    {
        [Property]
        public long InvariantResourceId { get; set; }
        [Property]
        public string? Language { get; set; }
        [Property]
        public string? Value { get; set; }
    }
}
