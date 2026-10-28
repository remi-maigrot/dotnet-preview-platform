using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sample.Domain.Employees;

namespace Sample.Infrastructure.Persistence.Configurations;

internal sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("employees");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.FullName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Email).HasMaxLength(320).IsRequired();
        builder.HasIndex(e => e.Email).IsUnique();
        builder.HasIndex(e => e.ManagerId);
        builder.HasOne<Employee>().WithMany().HasForeignKey(e => e.ManagerId).OnDelete(DeleteBehavior.Restrict);
    }
}
