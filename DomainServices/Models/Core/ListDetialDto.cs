namespace DomainServices.Models.Core
{
    public class ListDetialDto
    {
        public int Id { get; set; }
        public DateTime Created { get; set; }
        public string CreatedBy { get; set; }
        public DateTime? LastUpd { get; set; }
        public string LastUpdBy { get; set; }
        public decimal? GroupId { get; set; }

        public string Name { get; set; }
        public string TableName { get; set; }
        public string Lable { get; set; }
        public string ColumnName { get; set; }
        public string DataType { get; set; }
        public string? DefaultValue { get; set; }

        public bool Required { get; set; }
        public bool ReadOnly { get; set; }
        public bool DisplayInList { get; set; }
        public bool DisplayInForm { get; set; }

        public string? Comment { get; set; }

        public int ComponentId { get; set; }
        public decimal DisplaySequence { get; set; }

        public bool IsCalc { get; set; }
        public string? CalcExpr { get; set; }
        public string? HtmlStyle { get; set; }
        public decimal? LookUp { get; set; }
        public bool ImmediatePost { get; set; }
        public bool DisplayInPopup { get; set; }

        public string? FileDataColumn { get; set; }
        public int? FieldSize { get; set; }

        // Extra: Parent Component Info (top of query)
        public string ComponentName { get; set; }
        public string ComponentTitle { get; set; }
        public int PageSize { get; set; }

    }
    public class ListDetialResponse
    {
        public List<ListDetialDto> ListDetial { get; set; } = new List<ListDetialDto>();
        
    }

}
