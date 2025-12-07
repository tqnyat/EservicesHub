namespace DomainServices.Models.Core
{
    public class ExportExcelRequest
    {
        public string ComponentName { get; set; } = "";
        public string CoreDataView { get; set; } = "";

    }
    public class ExportExcelResultDto
    {
        public string FileName { get; set; } = "";
        public DataShape Table { get; set; } = new DataShape();
        public string? ExceptionError { get; set; }
    }

}
