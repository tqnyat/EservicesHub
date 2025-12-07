namespace EservicesHub.Models.Core
{
    public class LookupList
    {
        public string LookupName { get; set; }
        public string LangId { get; set; } = "";
        public string CascadeValue { get; set; } = "";
        public string? LookupCode { get; set; }
        public bool GetNote { get; set; } = false ;
        public bool AddSelectFromList { get; set; } = true;
    }
}
