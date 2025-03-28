﻿// <auto-generated />
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Argus.Sync.Example.Migrations
{
    [DbContext(typeof(TestDbContext))]
    partial class TestDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasDefaultSchema("public")
                .HasAnnotation("ProductVersion", "9.0.0")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("Argus.Sync.Data.Models.ReducerState", b =>
                {
                    b.Property<string>("Name")
                        .HasColumnType("text");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("LatestIntersectionsJson")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("StartIntersectionJson")
                        .IsRequired()
                        .HasColumnType("text");

                    b.HasKey("Name");

                    b.ToTable("ReducerStates", "public");
                });

            modelBuilder.Entity("Argus.Sync.Example.Models.BlockTest", b =>
                {
                    b.Property<string>("BlockHash")
                        .HasColumnType("text");

                    b.Property<decimal>("BlockNumber")
                        .HasColumnType("numeric(20,0)");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<decimal>("Slot")
                        .HasColumnType("numeric(20,0)");

                    b.HasKey("BlockHash");

                    b.ToTable("BlockTests", "public");
                });

            modelBuilder.Entity("Argus.Sync.Example.Models.OutputBySlot", b =>
                {
                    b.Property<string>("TxHash")
                        .HasColumnType("text");

                    b.Property<long>("TxIndex")
                        .HasColumnType("bigint");

                    b.Property<string>("Address")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<byte[]>("DatumData")
                        .IsRequired()
                        .HasColumnType("bytea");

                    b.Property<byte[]>("RawCbor")
                        .IsRequired()
                        .HasColumnType("bytea");

                    b.Property<byte[]>("ReferenceScript")
                        .HasColumnType("bytea");

                    b.Property<decimal>("Slot")
                        .HasColumnType("numeric(20,0)");

                    b.Property<decimal?>("SpentSlot")
                        .HasColumnType("numeric(20,0)");

                    b.Property<int>("UtxoStatus")
                        .HasColumnType("integer");

                    b.HasKey("TxHash", "TxIndex");

                    b.ToTable("OutputBySlot", "public");
                });

            modelBuilder.Entity("Argus.Sync.Example.Models.PriceByToken", b =>
                {
                    b.Property<string>("OutRef")
                        .HasColumnType("text");

                    b.Property<decimal>("Slot")
                        .HasColumnType("numeric(20,0)");

                    b.Property<string>("TokenXSubject")
                        .HasColumnType("text");

                    b.Property<string>("TokenYSubject")
                        .HasColumnType("text");

                    b.Property<int>("PlatformType")
                        .HasColumnType("integer");

                    b.Property<decimal>("TokenXPrice")
                        .HasColumnType("numeric(20,0)");

                    b.Property<decimal>("TokenYPrice")
                        .HasColumnType("numeric(20,0)");

                    b.HasKey("OutRef", "Slot", "TokenXSubject", "TokenYSubject", "PlatformType");

                    b.ToTable("PricesByToken", "public");
                });

            modelBuilder.Entity("Argus.Sync.Example.Models.TransactionTest", b =>
                {
                    b.Property<string>("TxHash")
                        .HasColumnType("text");

                    b.Property<decimal>("TxIndex")
                        .HasColumnType("numeric(20,0)");

                    b.Property<string>("BlockHash")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<decimal>("BlockNumber")
                        .HasColumnType("numeric(20,0)");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<byte[]>("RawTx")
                        .IsRequired()
                        .HasColumnType("bytea");

                    b.Property<decimal>("Slot")
                        .HasColumnType("numeric(20,0)");

                    b.HasKey("TxHash", "TxIndex");

                    b.ToTable("TransactionTests", "public");
                });

            modelBuilder.Entity("Argus.Sync.Example.Models.TxBySlot", b =>
                {
                    b.Property<string>("TxHash")
                        .HasColumnType("text");

                    b.Property<decimal>("Index")
                        .HasColumnType("numeric(20,0)");

                    b.Property<byte[]>("Raw")
                        .IsRequired()
                        .HasColumnType("bytea");

                    b.Property<decimal>("Slot")
                        .HasColumnType("numeric(20,0)");

                    b.HasKey("TxHash", "Index");

                    b.ToTable("TxBySlot", "public");
                });
#pragma warning restore 612, 618
        }
    }
}
