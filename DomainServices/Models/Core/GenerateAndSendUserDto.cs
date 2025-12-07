namespace DomainServices.Models.Core
{
    public class GenerateAndSendUserRequest
    {
        public string UserId { get; set; }
    }
    public class GenerateAndSendUserResponse
    {
        public string Status { get; set; }
        public string Message { get; set; }
    }
}
