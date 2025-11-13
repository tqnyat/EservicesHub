namespace DomainServices.Models
{
    public class SignedInUsers
    {
        public int Id { get; set; }
        public string CreatedBy { get; set; }

        public DateTime LastUpd { get; set; }
        public DateTime Created { get; set; }
        public DateTime? EndSession { get; set; }
        public string IPAddress { get; set; }
        public string Location { get; set; }
        public string Country { get; set; }
        public string Region { get; set; }
        public int Source { get; set; }
        public string SessionId { get; set; }
        public string SessionKey { get; set; }
        public string? DeviceModel { get; set; }
        public bool Active { get; set; }
    }
    public class LocationResponse
    {
        public string longitude { get; set; }
        public string latitude { get; set; }
        public string country_capital { get; set; }
        public string country_name { get; set; }
    }
}
