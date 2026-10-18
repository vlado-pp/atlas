using Atlas.Onboarding.Domain.Applicants;
using Atlas.Onboarding.Domain.Applications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atlas.Onboarding.Api.Persistence;

internal static class Converters
{
    public static PropertyBuilder<ApplicationId> HasApplicationIdConversion(this PropertyBuilder<ApplicationId> property) =>
        property.HasConversion(id => id.Value, value => ApplicationId.FromTrusted(value)).HasMaxLength(35).IsUnicode(false);
}

internal sealed class ApplicationConfiguration : IEntityTypeConfiguration<Application>
{
    public void Configure(EntityTypeBuilder<Application> b)
    {
        b.ToTable("Applications");
        b.HasKey(a => a.Id);
        b.Property(a => a.Id).HasApplicationIdConversion();
        b.Property(a => a.Market).HasMaxLength(2).IsUnicode(false);
        b.Property(a => a.SubmittedBy).HasMaxLength(200);
        b.Property(a => a.Status).HasConversion<string>().HasMaxLength(40);
        b.Property(a => a.RejectionReason).HasConversion<string>().HasMaxLength(40);
        b.Property(a => a.IdentityVerificationReference).HasMaxLength(100);
        b.Property(a => a.ScreeningCaseId).HasMaxLength(100);
        b.Property(a => a.ReviewedBy).HasMaxLength(200);
        b.Property(a => a.CardOrderReference).HasMaxLength(100);
        b.Property<byte[]>("Version").IsRowVersion();

        b.OwnsOne(a => a.BranchActivation, o =>
        {
            o.Property(x => x.StaffId).HasColumnName("BranchActivationStaffId").HasMaxLength(200);
            o.Property(x => x.BranchCode).HasColumnName("BranchActivationBranchCode").HasMaxLength(50);
            o.Property(x => x.SignedFormReference).HasColumnName("BranchActivationSignedFormReference").HasMaxLength(200);
            o.Property(x => x.ActivatedAt).HasColumnName("BranchActivationAt");
        });

        b.OwnsMany(a => a.History, h =>
        {
            h.ToTable("ApplicationStatusHistory");
            h.WithOwner().HasForeignKey("ApplicationId");
            h.Property<long>("Id");
            h.HasKey("Id");
            h.Property(x => x.From).HasConversion<string>().HasMaxLength(40);
            h.Property(x => x.To).HasConversion<string>().HasMaxLength(40);
            h.Property(x => x.Actor).HasMaxLength(200);
            h.Property(x => x.Note).HasMaxLength(500);
        });
        b.Navigation(a => a.History).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.Ignore(a => a.DomainEvents);
        b.Ignore(a => a.CustomerFacingStatus);

        // The workflow runner claims due work through this index.
        b.HasIndex(a => a.NextAttemptAt).HasFilter("[NextAttemptAt] IS NOT NULL");
    }
}

internal sealed class ApplicantConfiguration : IEntityTypeConfiguration<Applicant>
{
    public void Configure(EntityTypeBuilder<Applicant> b)
    {
        b.ToTable("Applicants");
        b.HasKey(a => a.ApplicationId);
        b.Property(a => a.ApplicationId).HasApplicationIdConversion();
        b.Property(a => a.FirstName).HasMaxLength(100);
        b.Property(a => a.LastName).HasMaxLength(100);
        b.Property(a => a.Email).HasMaxLength(254);
        b.Property(a => a.Phone).HasMaxLength(30);
        b.Property(a => a.Nationality).HasMaxLength(3).IsUnicode(false);

        // Identifier is an attribute, never a key or unique constraint (Annex B note 2).
        b.ComplexProperty(a => a.Identifier, i =>
        {
            i.Property(x => x.Scheme).HasColumnName("IdentifierScheme").HasMaxLength(40);
            i.Property(x => x.Value).HasColumnName("IdentifierValue").HasMaxLength(40);
        });

        b.OwnsMany(a => a.Documents, d =>
        {
            d.ToTable("ApplicantDocuments");
            d.WithOwner().HasForeignKey("ApplicationId");
            d.HasKey(x => x.Id);
            d.Property(x => x.Id).ValueGeneratedNever();
            d.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            d.Property(x => x.StorageKey).HasMaxLength(200);
            d.Property(x => x.ContentSha256).HasMaxLength(64).IsUnicode(false);
        });
        b.Navigation(a => a.Documents).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.Ignore(a => a.FullName);
        b.Ignore(a => a.IdentityDocument);
        b.Ignore(a => a.Selfie);
    }
}

internal sealed class AccessLogEntryConfiguration : IEntityTypeConfiguration<AccessLogEntry>
{
    public void Configure(EntityTypeBuilder<AccessLogEntry> b)
    {
        b.ToTable("AccessLog");
        b.HasKey(e => e.Id);
        b.Property(e => e.ActorType).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.ActorId).HasMaxLength(200);
        b.Property(e => e.Action).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.RecordType).HasMaxLength(50);
        b.Property(e => e.RecordId).HasMaxLength(100);
        b.Property(e => e.Purpose).HasMaxLength(100);
        b.Property(e => e.CorrelationId).HasMaxLength(200);
        b.HasIndex(e => new { e.RecordType, e.RecordId });
    }
}

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> b)
    {
        b.ToTable("IdempotencyRecords");
        b.HasKey(r => new { r.ActorId, r.Key });
        b.Property(r => r.ActorId).HasMaxLength(200);
        b.Property(r => r.Key).HasMaxLength(100);
        b.Property(r => r.RequestSha256).HasMaxLength(64).IsUnicode(false);
        b.Property(r => r.ApplicationId).HasMaxLength(35).IsUnicode(false);
    }
}
