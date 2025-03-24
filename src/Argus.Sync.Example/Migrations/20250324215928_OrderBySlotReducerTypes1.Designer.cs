﻿// <auto-generated />
using Argus.Sync.Example.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Argus.Sync.Example.Migrations
{
    [DbContext(typeof(TestDbContext))]
    [Migration("20250324215928_OrderBySlotReducerTypes1")]
    partial class OrderBySlotReducerTypes1
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasDefaultSchema("public")
                .HasAnnotation("ProductVersion", "9.0.3")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("Argus.Sync.Data.Models.ReducerState", b =>
                {
                    b.Property<string>("Name")
                        .HasColumnType("text");

                    b.Property<decimal>("Slot")
                        .HasColumnType("numeric(20,0)");

                    b.Property<string>("Hash")
                        .IsRequired()
                        .HasColumnType("text");

                    b.HasKey("Name", "Slot");

                    b.HasIndex("Slot")
                        .IsDescending();

                    b.HasIndex("Name", "Slot")
                        .IsDescending(false, true);

                    b.ToTable("ReducerStates", "public");
                });

            modelBuilder.Entity("Argus.Sync.Example.Models.OrderBySlot", b =>
                {
                    b.Property<string>("TxHash")
                        .HasColumnType("text");

                    b.Property<decimal>("Index")
                        .HasColumnType("numeric(20,0)");

                    b.Property<string>("AssetName")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("OwnerAddress")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("PolicyId")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<decimal>("Quantity")
                        .HasColumnType("numeric(20,0)");

                    b.Property<byte[]>("RawData")
                        .IsRequired()
                        .HasColumnType("bytea");

                    b.Property<decimal>("Slot")
                        .HasColumnType("numeric(20,0)");

                    b.Property<int>("Status")
                        .HasColumnType("integer");

                    b.HasKey("TxHash", "Index");

                    b.ToTable("OrdersBySlot", "public");
                });
#pragma warning restore 612, 618
        }
    }
}
