using System.ComponentModel;

namespace DomainServices.Data.DataAnnotations
{
	public class LocalizedDisplayName : DisplayNameAttribute
    {
        private string DisplayNameKey { get; set; }
        private string ResourceSetName { get; set; }

        public LocalizedDisplayName(string displayNameKey)
            : base(displayNameKey)
        {
            DisplayNameKey = displayNameKey;
        }

        public LocalizedDisplayName(string displayNameKey, string resourceSetName)
            : base(displayNameKey)
        {
            DisplayNameKey = displayNameKey;
            ResourceSetName = resourceSetName;
        }

        public override string DisplayName
        {
            get
            {
                return Localizer.GetLocalization(DisplayNameKey);
            }
        }
    }
}
