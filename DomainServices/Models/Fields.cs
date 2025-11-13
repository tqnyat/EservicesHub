namespace DomainServices.Models 
{
    [Serializable]
    public class Fields
    {
        public string? Name { get; set; }
        public object? Value { get; set; }
        public string? ColumnName { get; set; }
        public object? DefaultValue { get; set; }
        public bool Visible { get; set; }
        public bool ReadOnly { get; set; }
        public bool Required { get; set; }
        public string? Type { get; set; }
        public bool ImmediatePost { get; set; }
        public bool DisplayInPopup { get; set; }
        public string? LookUpQuery { get; set; }
        public string? label { get; set; }
        public List<Lookup>? LookUpValues { get; set; }
        public bool IsCalc { get; set; }
        public string? CalcExpr { get; set; }
        public string? FileDataColumn { get; set; }
        public string? FileDataValue { get; set; }
        public int? FieldSize { get; set; }
    }
}
