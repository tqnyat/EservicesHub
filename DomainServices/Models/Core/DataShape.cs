namespace DomainServices.Models.Core
{
    public record DataShape
    {
        public List<ColumnShape> Columns { get; set; } = new();
        public List<Dictionary<string, object>> Rows { get; set; } = new();
    }

    public record ColumnShape
    {
        public string Name { get; set; } = "";
        public string DataType { get; set; } = "";
        public bool ReadOnly { get; set; }
        public bool Required { get; set; }
    }

}
