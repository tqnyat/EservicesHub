using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DomainServices.Data
{
	internal class UsersEntityConfiguration : IEntityTypeConfiguration<Users>
    {
        public void Configure(EntityTypeBuilder<Users> builder)
        {
            builder.Property(u => u.IdNo).HasMaxLength(100).IsRequired();
            builder.Property(u => u.FirstName).HasMaxLength(100).IsRequired();
            builder.Property(u => u.MidName).HasMaxLength(100);
            builder.Property(u => u.ThirdName).HasMaxLength(100);
            builder.Property(u => u.LastName).HasMaxLength(100).IsRequired();
            builder.Property(u => u.Nationality).HasMaxLength(100).IsRequired();
            builder.Property(u => u.Address).HasMaxLength(1000);
            builder.Property(u => u.Comment).HasMaxLength(1000);
            builder.Property(u => u.JobTitle).HasMaxLength(100);
            builder.Property(u => u.Title).HasMaxLength(100);
            builder.Property(u => u.WorkPlace).HasMaxLength(200);
        }
    }
}