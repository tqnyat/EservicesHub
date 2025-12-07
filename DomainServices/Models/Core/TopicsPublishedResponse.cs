namespace DomainServices.Models.Core
{
    public class TopicsPublishedResponse
    {
        public List<Dictionary<string, object>> DataList { get; set; }
        public string Status { get; set; }
    }
    public class TopicsDetailResponse
    {
        public List<Dictionary<string, object>> DataList { get; set; }
        public string Status { get; set; }
    }
    public class TopicDetailRequest
    {
        public string TopicName { get; set; }
    }
}
