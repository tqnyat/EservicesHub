namespace DomainServices.Models.Core
{
    public class SubmitDataRequest
    {
        public string ComponentName { get; set; } = "";
        public Dictionary<string, object> DataList { get; set; } = new(); 
        public string SelectedId { get; set; } = "-1";                    
        public string SaveType { get; set; } = "new";                    
        public string CoreDataView { get; set; } = "";                   

        public string? FieldsCache { get; set; }
    }

    public class SubmitDataResultDto
    {
        public string RowId { get; set; } = "0";
        public string SaveType { get; set; } = "";
        public string? ExceptionError { get; set; }
        public Dictionary<string, object> UpdatedSession { get; set; } = new();
    }
}
