using DomainServices.Models;

namespace DomainServices.Data.DataAnnotations
{
	public static class Localizer
    {
        public static List<LocalResource> _localizationResources;

        public static string GetLocalization(string ResourceName)
        {

            if (_localizationResources != null)
            {
                string currentLanguage = Thread.CurrentThread.CurrentUICulture.Name;
                var localResource = _localizationResources.FirstOrDefault(l => l.ResourceName == ResourceName);
                var retVal = localResource == null ? ResourceName : currentLanguage.ToLower().Contains("en") ? localResource.TextEn : localResource.TextAr;
                return retVal;
            }
            else
            {
                return ResourceName;
            }
        }
    }
}
