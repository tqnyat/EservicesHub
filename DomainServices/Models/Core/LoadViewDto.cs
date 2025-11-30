using System.Data;

namespace DomainServices.Models.Core
{
    public class LoadViewListItemDto
    {
        public decimal Seq { get; set; }
        public string ClientURL { get; set; } = "";
        public string ViewTitle { get; set; } = "";
        public decimal ViewId { get; set; }
        public int ViewStyle { get; set; }
        public int ViewDataAccess { get; set; }
        public decimal CompId { get; set; }
        public string CompName { get; set; } = "";
        public string CompTitle { get; set; } = "";
        public decimal? CompFieldId { get; set; }
        public decimal? ParCompId { get; set; }
        public decimal? ParCompFieldId { get; set; }
        public bool ReadOnly { get; set; }
        public bool ExportExcel { get; set; }
        public bool NoInsert { get; set; }
        public bool NoUpdate { get; set; }
        public bool NoDelete { get; set; }
        public string CompFieldName { get; set; } = "";
        public string ParCompName { get; set; } = "";
        public string ParCompFieldName { get; set; } = "";
        public string ParCompFieldValue { get; set; } = "";
        public int HasDetail { get; set; }
        public string ViewDescription { get; set; } = "";
        public string QueryString { get; set; } = "";
        public string QueryStringCount { get; set; } = "";
        public string QueryStringCmd { get; set; } = "";

        // NEW — professional structure replacing InitComponents JSON
        public Component Component { get; set; }
    }

    public class LoadViewResponse
    {
        public List<LoadViewListItemDto> ListDetial { get; set; } = new List<LoadViewListItemDto>();
        public string ViewDescription { get; set; } = string.Empty;
        public string ApplicationTheme { get; set; } = string.Empty;
        public Dictionary<string, object> UpdatedSession { get; set; } = new Dictionary<string, object>();
    }
}
