namespace DomainServices.Models.Core
{
    public class IGetFileRequest
    {
        public string ComponentName { get; set; }
        public string FieldName { get; set; }
        public string SelectedId { get; set; }
    }
    public class IGetFileResultDto
    {
        public string FileName { get; set; }
        public string FileBase64 { get; set; }
        public string ExceptionError { get; set; }
    }

}
