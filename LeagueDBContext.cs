using Discord;
using Discord.WebSocket;
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
    public required int SeasonNumber { get; set; }
    public List<Squad> Squads { get; } = [];
    public List<SeasonWeek> SeasonWeeks { get; } = [];
    public List<PlayoffWeek> PlayoffWeeks { get; } = [];
    public required int NumberOfSeasonWeeks { get; set; }
}

internal class Team
{
    public int TeamId { get; set; }
    public required ulong TeamRoleID { get; set; }
    public required string TeamName { get; set; }
    public required string TeamLogoURL { get; set; }
    public required ulong TeamCaptainID { get; set; }
    public bool Unlinked { get; set; } = false;

    public async Task<EmbedBuilder> GetDefaultEmbed()
    {
        SocketGuildUser teamCaptain = Program.Guild.GetUser(TeamCaptainID);
        return new EmbedBuilder()
            .WithTitle(TeamName)
            .WithColor((await Program.Guild.GetRoleAsync(TeamRoleID)).Color)
            .WithImageUrl(TeamLogoURL)
            .WithDescription($"Team Captain: {teamCaptain.GlobalName ?? teamCaptain.Username}");
    }
}

internal class Squad
{
    public int SquadId { get; set; }
    public int TeamId { get; set; }
    public required Team Team { get; set; }
    public int SeasonId { get; set; }
    public required Season Season { get; set; }
    public List<ulong> PlayerIDs { get; } = [];
    public List<ulong> SubstituteIDs { get; } = [];
}

internal class Week
{
    public int WeekId { get; set; }
    public required int WeekNumber { get; set; }
    public int SeasonId { get; set; }
    public required Season Season { get; set; }
    public List<Match> Matches { get; } = [];
    /// <summary>
    /// This week's random mapping of a matches 1st squad's 1st, 2nd, and 3rd players to the 2nd squad's players.
    /// I feel like this could be phrased better.
    /// </summary>
    public int[] Players123Mappings { get; } = [1, 2, 3];
}

internal class SeasonWeek : Week
{

}

internal class PlayoffWeek : Week
{

}

internal class Match
{
    public int MatchId { get; set; }
    public int WeekId { get; set; }
    public required Week Week { get; set; }
    public int HomeSquadId { get; set; }
    public required Squad HomeSquad { get; set; }
    public int AwaySquadId { get; set; }
    public required Squad AwaySquad { get; set; }
    public List<Game> Games { get; } = [];
    public List<Substitution> Substitutions { get; } = [];
}

internal class Substitution
{
    public int SubstitutionId { get; set; }
    public required ulong PlayerID { get; set; }
    public required ulong SubstituteID { get; set; }
}

internal class Game
{
    public int GameId { get; set; }
    public required ulong HomePlayerID { get; set; }
    public required ulong AwayPlayerID { get; set; }
    public int HomePlayerWins { get; set; } = 0;
    public int AwayPlayerWins { get; set; } = 0;
}