using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace DomainServices.Data.DataAnnotations
{
	public class LocalizedValidationMetadataProvider : IValidationMetadataProvider
    {
        public LocalizedValidationMetadataProvider()
        {
        }

        public void CreateValidationMetadata(ValidationMetadataProviderContext context)
        {

            var propertyName = context.Key.Name;

            if (string.IsNullOrEmpty(propertyName))
                return;

            if (context.Key.ModelType.GetTypeInfo().IsValueType && Nullable.GetUnderlyingType(context.Key.ModelType.GetTypeInfo()) == null && context.ValidationMetadata.ValidatorMetadata.Where(m => m.GetType() == typeof(RequiredAttribute)).Count() == 0)
            context.ValidationMetadata.ValidatorMetadata.Add(new RequiredAttribute());
            

            foreach (var attribute in context.ValidationMetadata.ValidatorMetadata)
            {
                var tAttr = attribute as ValidationAttribute;
                if (tAttr != null)
                {
                    var errorName = tAttr.GetType().Name;
                    var fallbackName = errorName + "_ValidationError";
                    var name = tAttr.ErrorMessage ?? fallbackName;
                    var localized = Localizer.GetLocalization(name);
                    var text = localized;

                    tAttr.ErrorMessage = text;
                    tAttr.ErrorMessageResourceName = "";
                }
            }
        }
    }
}
