using CPS_API.Models;
using CPS_API.Models.Configurations;
using Microsoft.EntityFrameworkCore;

namespace CPS_API.Database
{
    public partial class CpsDbContext : DbContext
    {
        public CpsDbContext()
        {
        }

        public CpsDbContext(DbContextOptions<CpsDbContext> options)
            : base(options)
        {
        }

        public virtual DbSet<Settings> Settings { get; set; }

        public virtual DbSet<WebhookSubscription> WebhookSubscription { get; set; }

        public virtual DbSet<ObjectIdentifiers> ObjectIdentifiers { get; set; }

        public virtual DbSet<ToBePublished> ToBePublished { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseSqlServer("");
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new SettingsConfiguration());
            modelBuilder.ApplyConfiguration(new ObjectIdentifiersConfiguration());
            modelBuilder.ApplyConfiguration(new ToBePublishedConfiguration());
            modelBuilder.ApplyConfiguration(new WebhookSubscriptionConfiguration());
        }
    }
}
