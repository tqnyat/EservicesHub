namespace DomainServices.Models.Core
{
    public class ViewListItemDto
    {
        public decimal Id { get; set; }
        public decimal Type { get; set; }
        public int? ViewStyle { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public decimal ViewSequence { get; set; }
        public decimal? MainCategory { get; set; }
        public string ViewIcon { get; set; } = string.Empty;
        public bool ReadOnly { get; set; }
    }
    public class GetUserViewsResponse
    {
        public List<ViewListItemDto> ListDetial { get; set; } = new List<ViewListItemDto>();
        public string UserFullName { get; set; } = string.Empty;
        public string ApplicationTheme { get; set; } = string.Empty;
    }
}
