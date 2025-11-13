namespace DomainServices.Models
{
    public class ComponentDetail
    {
        public string FieldName { get; set; } = "";
        public string TableName { get; set; } = "";
        public string TableColumn { get; set; } = "";
        public string DataType { get; set; } = "";
        public bool ReadOnly { get; set; }
        public bool Required { get; set; }
        public object Value { get; set; }
        public bool DisplayInForm { get; set; }
        public bool DisplayInList { get; set; }
        public string DefaultValue { get; set; } = "";
        public string LookUp { get; set; } = "";
        public bool IsCalc { get; set; }
        public string CalcExpression { get; set; } = "";
        public string HtmlStyle { get; set; } = "";
    }
}
