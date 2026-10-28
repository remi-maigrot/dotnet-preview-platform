using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sample.Domain.Absences;
using Sample.Domain.Employees;

namespace Sample.Infrastructure.Persistence.Configurations;

internal sealed class AbsenceRequestConfiguration : IEntityTypeConfiguration<AbsenceRequest>
{
    public void Configure(EntityTypeBuilder<AbsenceRequest> builder)
    {
        builder.ToTable("absence_requests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.ComplexProperty(r => r.Period, period =>
        {
            period.Property(p => p.Start).HasColumnName("start_date");
            period.Property(p => p.End).HasColumnName("end_date");
        });

        builder.Property(r => r.Type).HasConversion<string>().HasMaxLength(32);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(r => r.Comment).HasMaxLength(AbsenceErrors.MaxCommentLength);
        builder.Property(r => r.RejectionReason).HasMaxLength(AbsenceErrors.MaxCommentLength);

        // Optimistic concurrency on PostgreSQL's system column xmin: no extra column to maintain.
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasOne<Employee>().WithMany().HasForeignKey(r => r.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Employee>().WithMany().HasForeignKey(r => r.DecidedBy).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(r => new { r.EmployeeId, r.Status });
        builder.HasIndex(r => r.SubmittedAt);
    }
}
