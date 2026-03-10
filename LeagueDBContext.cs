using Discord;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MLE_Infobot;

internal class LeagueDBContext : DbContext
{
    public DbSet<Season> Seasons { get; set; }
    public DbSet<Team> Teams { get; set; }

    public string DbPath { get; }

    public LeagueDBContext()
    {
        Environment.SpecialFolder folder = Environment.SpecialFolder.LocalApplicationData;
        string path = Environment.GetFolderPath(folder);
        path = System.IO.Path.Join(path, "league.db");
        Console.WriteLine($"Database loaded from: {path}");
        DbPath = path;
    }
    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={DbPath}");
}

internal class Season
{
    public int SeasonId { get; set; }
}

internal class Team
{
    public int TeamId { get; set; }
    public required ulong TeamRoleID { get; set; }
    public required string TeamLogoURL { get; set; }
    public required ulong TeamCaptainID { get; set; }
}
