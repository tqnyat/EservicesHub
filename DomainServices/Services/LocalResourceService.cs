using DomainServices.Data;
using DomainServices.Data.DataAnnotations;
using DomainServices.Models;
using DomainServices.Services.Interfaces;

namespace DomainServices.Services
{
    public class LocalResourceService : ILocalResourceService
    {
        private readonly LocalResourcesRepository _localResourcesRepository;
        
        public LocalResourceService(
            LocalResourcesRepository localResourcesRepository)
        {
            this._localResourcesRepository = localResourcesRepository;
            if (Localizer._localizationResources == null)
            {
                PrepareLocalResourceCache();
            }
        }

        public void PrepareLocalResourceCache()
        {
            Localizer._localizationResources = _localResourcesRepository.GetAll();
        }

        public string GetResource(string localResourceName, string langId = "")
        {
            if (Localizer._localizationResources == null)
            {
                PrepareLocalResourceCache();
            }

            var localResource = Localizer._localizationResources.FirstOrDefault(l => l.ResourceName.Trim().ToLower() == localResourceName.Trim().ToLower());
            var currentLanguage = langId == "" ? Thread.CurrentThread.CurrentCulture.DisplayName : langId;
            return (localResource == null) ? localResourceName : (currentLanguage.ToLower().Contains("en")) ? localResource.TextEn : localResource.TextAr;
        }

        public List<LocalResource> GetAll() {
            return _localResourcesRepository.GetAll();
        }

    }
}
