﻿// <auto-generated />
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Argus.Sync.Example.Migrations
{
    [DbContext(typeof(TestDbContext))]
    [Migration("20250326221455_UpdateComplexType")]
    partial class UpdateComplexType
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
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
#pragma warning restore 612, 618
        }
    }
}
