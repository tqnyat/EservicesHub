namespace DomainServices.Models.Core
{
    public class LoadDetailDataResponse
    {
        public List<LoadDetailItemDto> ListDetial { get; set; }
        public string UpdatedSession { get; set; }
    }

    public class LoadDetailItemDto
    {
        public decimal Seq { get; set; }
        public string ClientURL { get; set; }
        public string ViewTitle { get; set; }
        public decimal ViewId { get; set; }
        public decimal CompId { get; set; }
        public string CompName { get; set; }
        public string CompTitle { get; set; }

        public decimal? CompFieldId { get; set; }
        public decimal? ParCompId { get; set; }
        public string ParCompName { get; set; }
        public decimal? ParCompFieldId { get; set; }
        public string ParCompFieldName { get; set; }
        public string ParCompFieldValue { get; set; }

        public bool ReadOnly { get; set; }
        public Component Component { get; set; }
    }

}
