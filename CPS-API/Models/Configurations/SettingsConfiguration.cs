using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace CPS_API.Models.Configurations
{
    public sealed class SettingsConfiguration : IEntityTypeConfiguration<Settings>
    {
        public void Configure(EntityTypeBuilder<Settings> e)
        {
            e.ToTable("Settings");
            e.HasKey(x => x.Id).HasName("PK_Settings");

            e.Property(x => x.Id)
             .HasColumnType("BIGINT")
             .ValueGeneratedOnAdd()  // Identity kolom
             .IsRequired();

            e.Property(x => x.SequenceNumber)
             .HasColumnType("BIGINT")
             .IsRequired();
        }
    }
}