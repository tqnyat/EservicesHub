namespace DomainServices.Models.Core
{
    public class ViewListItemDto
    {
        public decimal Id { get; set; }
        public decimal Type { get; set; }
        public int? ViewStyle { get; set; }
        public string Name { get; set; }
        public string Title { get; set; }
        public decimal ViewSequence { get; set; }
        public decimal? MainCategory { get; set; }
        public string ViewIcon { get; set; }
        public bool ReadOnly { get; set; }
    }
    public class GetUserViewsResponse
    {
        public List<ViewListItemDto> ListDetial { get; set; }
        public string UserFullName { get; set; }
        public string ApplicationTheme { get; set; }
    }
}
