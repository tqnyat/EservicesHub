using DomainServices.Data.Repository;
using DomainServices.Models;

namespace DomainServices.Data
{
	public class LocalResourcesRepository
    {
        private readonly DomainDBContext _context;

        public LocalResourcesRepository(DomainDBContext context)
        {
            _context = context;
        }

        public List<LocalResource> GetAll()
        {
            return _context.LocalResources.ToList();
        }

        public LocalResource GetUserById(string resourceName)
        {
            return _context.LocalResources.FirstOrDefault(u => u.ResourceName == resourceName);
        }
    }
}
