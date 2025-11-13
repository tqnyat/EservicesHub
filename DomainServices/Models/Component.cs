namespace DomainServices.Models
{
    public class Component
    {
        public string TableName { get; set; } = "";
        public string OnCreateProc { get; set; } = "";
        public string OnUpdateProc { get; set; } = "";
        public string OnDeleteProc { get; set; } = "";
        public string SearchSpec { get; set; } = "";
        public string SortSpec { get; set; } = "";
        public int PageSize { get; set; }
        public int Type { get; set; }
    }
}
