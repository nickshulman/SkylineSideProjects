using NHibernate.Mapping.Attributes;

namespace ResourcesOrganizer.DataModel
{
    [Class(Lazy = false)]
    public class InvariantResource : Entity<InvariantResource>
    {
        [Property]
        public string? Name { get; set; }
        [Property]
        public string? Type { get; set; }
        [Property]
        public string? Value { get; set; }
        [Property]
        public string? Comment { get; set; }
    }
}
