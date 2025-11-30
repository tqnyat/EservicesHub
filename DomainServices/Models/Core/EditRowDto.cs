namespace DomainServices.Models.Core
{
    public class EditRowResultDto
    {
        public List<Fields> ListDetial { get; set; }

        public Dictionary<string, object> UpdatedSession { get; set; }
            = new Dictionary<string, object>();
    }
    public class EditRowRequest
    {
        public string? ComponentName { get; set; }
        public string? SelectedId { get; set; }
        public string? FieldsCache { get; set; }
        public string? CoreDataView { get; set; }
    }


}
