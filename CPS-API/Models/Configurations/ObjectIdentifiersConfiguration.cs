using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace CPS_API.Models.Configurations
{
    public sealed class ObjectIdentifiersConfiguration : IEntityTypeConfiguration<ObjectIdentifiers>
    {
        public void Configure(EntityTypeBuilder<ObjectIdentifiers> e)
        {
            e.ToTable("ObjectIdentifiers");

            e.HasKey(x => x.Id)
             .HasName("PK_ObjectIdentifiers");

            e.Property(x => x.Id)
             .HasColumnType("BIGINT")
             .ValueGeneratedOnAdd()  // Identity kolom
             .IsRequired();

            e.Property(x => x.ObjectId)
             .HasColumnType("NVARCHAR(255)")
             .HasMaxLength(255)
             .IsRequired();

            e.Property(x => x.AdditionalObjectId)
             .HasColumnType("NVARCHAR(255)")
             .HasMaxLength(255);

            e.Property(x => x.DriveId)
             .HasColumnType("NVARCHAR(100)")
             .HasMaxLength(100)
             .IsRequired();

            e.Property(x => x.DriveItemId)
             .HasColumnType("NVARCHAR(100)")
             .HasMaxLength(100)
             .IsRequired();

            e.Property(x => x.SiteId)
             .HasColumnType("NVARCHAR(50)")
             .HasMaxLength(50)
             .IsRequired();

            e.Property(x => x.ListId)
             .HasColumnType("NVARCHAR(50)")
             .HasMaxLength(50)
             .IsRequired();

            e.Property(x => x.ListItemId)
             .HasColumnType("NVARCHAR(50)")
             .HasMaxLength(50)
             .IsRequired();

            e.HasIndex(x => x.ObjectId)
             .IsUnique()
             .HasDatabaseName("UQ_ObjectIdentifiers_ObjectId");

            e.HasIndex(x => x.AdditionalObjectId)
             .HasDatabaseName("IX_ObjectIdentifiers_AdditionalObj");

            e.HasIndex(x => new { x.DriveId, x.DriveItemId })
             .HasDatabaseName("IX_ObjectIdentifiers_DriveItem");

            e.HasIndex(x => new { x.SiteId, x.ListId, x.ListItemId })
             .HasDatabaseName("IX_ObjectIdentifiers_ListItem");
        }
    }
}