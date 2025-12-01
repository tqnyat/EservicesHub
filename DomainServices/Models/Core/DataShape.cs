namespace DomainServices.Models.Core
{
    public record DataShape
    {
        public List<ColumnShape> Columns { get; init; } = new();
        public List<Dictionary<string, object>> Rows { get; init; } = new();
    }

    public record ColumnShape
    {
        public string Name { get; init; } = "";
        public string DataType { get; init; } = "";
        public bool ReadOnly { get; init; }
        public bool Required { get; init; }
    }

}
