using DomainServices.Models;

namespace DomainServices.Services.Interfaces
{
    public interface ILocalResourceService
    {
        string GetResource(string localResourceName, string langId = "");
        void PrepareLocalResourceCache();
        List<LocalResource> GetAll();
    }
}