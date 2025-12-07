namespace DomainServices.Models.Core
{
    public class ApplicationThemeRequest
    {
        public string ApplicationTheme { get; set; }
    }
    public class ApplicationThemeResponse
    {
        public List<Dictionary<string,object>> ListDetial { get; set; }
    }
}
