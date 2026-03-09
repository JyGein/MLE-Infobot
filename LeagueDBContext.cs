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

}

internal class Team
{
    public required IRole TeamRole { get; set; }
    public required IAttachment TeamLogo { get; set; }
    public required IUser TeamCaptain { get; set; }
}
