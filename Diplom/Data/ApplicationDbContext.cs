using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Diplom.Models;

namespace Diplom.Data
{
    public class ApplicationDbContext : IdentityDbContext<AppUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // DbSet для всех ваших моделей
        public DbSet<RawMaterial> RawMaterials { get; set; }
        public DbSet<WoodStack> WoodStacks { get; set; }
        public DbSet<WoodMovement> WoodMovements { get; set; }
        public DbSet<ProductionBatch> ProductionBatches { get; set; }
        public DbSet<FinishedGoodsStock> FinishedGoodsStocks { get; set; }
        public DbSet<ShipmentOrder> ShipmentOrders { get; set; }
        public DbSet<ShipmentItem> ShipmentItems { get; set; }
        public DbSet<Inventory> Inventories { get; set; }
        public DbSet<InventoryItem> InventoryItems { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Настройка уникальности для ShipmentOrder.InvoiceNumber
            builder.Entity<ShipmentOrder>()
                .HasIndex(o => o.InvoiceNumber)
                .IsUnique();
        }
    }
}