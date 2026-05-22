using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace CPS_API.Models.Configurations
{
    public sealed class ToBePublishedConfiguration : IEntityTypeConfiguration<ToBePublished>
    {
        public void Configure(EntityTypeBuilder<ToBePublished> e)
        {
            e.ToTable("ToBePublished");
            e.HasKey(x => x.Id).HasName("PK_ToBePublished");

            e.Property(x => x.Id)
             .HasColumnType("BIGINT")
             .ValueGeneratedOnAdd()  // Identity kolom
             .IsRequired();

            e.Property(x => x.ObjectId)
             .HasColumnType("NVARCHAR(255)")
             .HasMaxLength(255)
             .IsRequired();

            e.Property(x => x.PublicationDate)
             .HasColumnType("DATETIME2(7)")
             .IsRequired();

            e.HasIndex(x => x.ObjectId)
             .HasDatabaseName("IX_ToBePublished_ObjectId");

        }
    }
}