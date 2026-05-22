using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace CPS_API.Models.Configurations
{
    public sealed class WebhookSubscriptionConfiguration : IEntityTypeConfiguration<WebhookSubscription>
    {
        public void Configure(EntityTypeBuilder<WebhookSubscription> e)
        {
            e.ToTable("WebhookSubscription");
            e.HasKey(x => x.Id).HasName("PK_WebhookSubscription");

            e.Property(x => x.Id)
             .HasColumnType("BIGINT")
             .ValueGeneratedOnAdd()  // Identity kolom
             .IsRequired();

            e.Property(x => x.LastChangeToken)
             .HasColumnType("NVARCHAR(100)")
             .HasMaxLength(100);

            e.Property(x => x.SubscriptionExpirationDateTime)
             .HasColumnType("DATETIME2(7)")
             .IsRequired();

            e.Property(x => x.SubscriptionId)
             .HasColumnType("NVARCHAR(50)")
             .HasMaxLength(50)
             .IsRequired();

            e.Property(x => x.WebhookType)
             .HasConversion<byte>()
             .HasColumnType("TINYINT")
             .IsRequired();
        }
    }
}