namespace DomainServices.Models.Core
{
    public class DeleteRowRequest
    {
        public string ComponentName { get; set; }
        public string SelectedId { get; set; }
        public string CoreDataView { get; set; }
    }
    public class DeleteRowResultDto
    {
        public int EffectedRows { get; set; }
        public string ExceptionError { get; set; }
    }

}
