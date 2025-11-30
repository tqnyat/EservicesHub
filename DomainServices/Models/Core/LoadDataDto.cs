namespace DomainServices.Models.Core
{
    public class LoadDataFieldDto
    {
        public string FieldName { get; set; }
        public string TableName { get; set; }
        public string TableColumn { get; set; }
        public string DataType { get; set; }
        public bool ReadOnly { get; set; }
        public bool Required { get; set; }
        public bool DisplayInForm { get; set; }
        public bool DisplayInList { get; set; }
        public bool IsCalc { get; set; }
        public string CalcExpression { get; set; }
        public string LookUp { get; set; }
    }
    public class LoadDataResultDto
    {
        public List<Dictionary<string, object>> ListDetial { get; set; }
        public int RecordCount { get; set; }
        public int PageSize { get; set; }
        public string AppletDisable { get; set; }
        public Dictionary<string, object> UpdatedSession { get; set; }
    }

    public class LoadDataRequest
    {
        public string ComponentName { get; set; }
        public int PageNumber { get; set; }
        public string SelectedId { get; set; }
        public Dictionary<string, string> Search { get; set; }
        public string CoreDataView { get; set; }
    }


}
