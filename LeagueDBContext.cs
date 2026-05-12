using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace MLE_Infobot;

internal sealed class LeagueDBContext : DbContext
{
    public DbSet<Season> Seasons { get; set; }
    public DbSet<Team> Teams { get; set; }

    public string DbPath { get; }

    public LeagueDBContext()
    {
        Environment.SpecialFolder folder = Environment.SpecialFolder.LocalApplicationData;
        string path = Environment.GetFolderPath(folder);
        path = Path.Join(path, "league.db");
        Console.WriteLine($"Database loaded from: {path}");
        DbPath = path;
    }
    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={DbPath}");
}

internal class Season
{
    public enum SeasonState
    {
        Unpublished,
        Started,
        Finished
    }
    public int SeasonId { get; set; }
    public required long SeasonNumber { get; set; }
    [NotMapped]
    public List<Squad> Squads => Divisions.SelectMany(d => d.Squads).ToList();
    public List<Division> Divisions { get; } = [];
    [NotMapped]
    public List<Week> AllWeeks => [.. SeasonWeeks, .. PlayoffWeeks];
    public List<SeasonWeek> SeasonWeeks { get; } = [];
    public List<PlayoffWeek> PlayoffWeeks { get; } = [];
    public required long NumberOfSeasonWeeks { get; set; }
    public required SeasonState State { get; set; }

    public async Task RandomizeMatches()
    {
        //this should never happen but just in case, since this is dangerous to do on an in-progress season as it clears all season weeks
        if (State != SeasonState.Unpublished) return;
        LeagueDBContext dBContext = new();
        SeasonWeeks.Clear();
        //making seed from squad ids as it will be usually be unique each time they are randomized
        Random rnd = new(Squads.Select(s => s.SquadId).Sum());
        for (int i = 1; i <= NumberOfSeasonWeeks; i++)
        {
            SeasonWeek week = new() { Season = this, WeekNumber = i, State = Week.WeekState.Unpublished };
            List<Squad> unmatchedSquads = [.. Squads];
            unmatchedSquads = [..unmatchedSquads.Shuffle(rnd)];
            while (unmatchedSquads.Count > 1)
            {
                week.Matches.Add(new() { HomeSquad = unmatchedSquads.Pop(), AwaySquad = unmatchedSquads.Pop(), Week = week });
            }
            //reshuffling matches where two squads from the same team play each other if possible
            foreach (Match doubleTeamMatch in week.Matches.Where(m => m.AwaySquad == m.HomeSquad))
            {
                //making sure that the match still has a double team issue as another duped team could have swapped with this match before we've iterated to this one
                if (doubleTeamMatch.HomeSquad.Team != doubleTeamMatch.AwaySquad.Team) continue;
                Team dupedTeam = doubleTeamMatch.HomeSquad.Team;
                //if there is match where neither squad is from the team swap with it, otherwise check if there is an unpaired squad to swap with
                if (week.Matches.Shuffle(rnd).FirstOrDefault(m => m.AwaySquad.Team != dupedTeam && m.HomeSquad.Team != dupedTeam) is Match targetMatch)
                {
                    bool swappingAwaySquads = rnd.Next() % 2 == 0;
                    Squad tempSquad = swappingAwaySquads ? targetMatch.AwaySquad : targetMatch.HomeSquad;
                    if (swappingAwaySquads) targetMatch.AwaySquad = swappingAwaySquads ? doubleTeamMatch.AwaySquad : doubleTeamMatch.HomeSquad;
                    else targetMatch.HomeSquad = swappingAwaySquads ? doubleTeamMatch.AwaySquad : doubleTeamMatch.HomeSquad;
                    if (swappingAwaySquads) doubleTeamMatch.AwaySquad = tempSquad;
                    else doubleTeamMatch.HomeSquad = tempSquad;
                }
                else if (unmatchedSquads.Count > 0 && unmatchedSquads.First().Team != dupedTeam)
                {
                    bool swappingAwaySquad = rnd.Next() % 2 == 0;
                    Squad tempSquad = swappingAwaySquad ? doubleTeamMatch.AwaySquad : doubleTeamMatch.HomeSquad;
                    if (swappingAwaySquad) doubleTeamMatch.AwaySquad = unmatchedSquads.Pop();
                    else doubleTeamMatch.HomeSquad = unmatchedSquads.Pop();
                    unmatchedSquads.Add(tempSquad);
                }
            }
            SeasonWeeks.Add(week);
        }
        await dBContext.SaveChangesAsync();
    }

    public Week GetCurrentOrFirstWeek()
    {
        List<Week> allWeeks = [..SeasonWeeks, ..PlayoffWeeks];
        if (allWeeks.FirstOrDefault(w => w.State == Week.WeekState.Current) is Week currentWeek) return currentWeek;
        return allWeeks.First();
    }
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
            .WithThumbnailUrl(TeamLogoURL)
            .WithDescription($"Team Captain: {teamCaptain.Nickname ?? teamCaptain.Username}");
    }
}

internal class Division
{
    public int DivisionId { get; set; }
    public required string DivisionName { get; set; }
    public List<Squad> Squads { get; } = [];
    public int SeasonId { get; set; }
    public required Season Season { get; set; }
}

internal class Squad
{
    public int SquadId { get; set; }
    public required int SquadNumber { get; set; }
    public int TeamId { get; set; }
    public required Team Team { get; set; }
    public int DivisionId { get; set; }
    public required Division Division { get; set; }
    public List<ulong> PlayerIDs { get; } = [];
    public List<ulong> SubstituteIDs { get; } = [];
    [NotMapped]
    public int MatchWins => Matches.Count(m => m.WinningSquad == this);
    [NotMapped]
    public int MatchLosses => Matches.Count(m => m.Winner != Match.MatchState.Undecided && m.WinningSquad != this);
    [NotMapped]
    public int MatchTies => Matches.Count(m => m.Winner == Match.MatchState.Tie);
    [NotMapped]
    public int GameWins => Matches.Sum(m => m.HomeSquad == this ? m.HomeGameWins : m.AwayGameWins);
    [NotMapped]
    public int GameLosses => Matches.Sum(m => m.HomeSquad == this ? m.AwayGameWins : m.HomeGameWins);
    [NotMapped]
    public Season Season => Division.Season;
    [NotMapped]
    public List<Match> Matches => Season.AllWeeks.SelectMany(w => w.Matches.Where(m => m.AwaySquad == this || m.HomeSquad == this)).ToList();

    public async Task<EmbedBuilder> GetDefaultEmbed()
    {
        List<EmbedFieldBuilder> fields = [new EmbedFieldBuilder()
            .WithName("Players:")
            .WithValue(string.Join("\n", PlayerIDs.Select(id => Program.Guild.GetUser(id).DisplayName)))];
        if (SubstituteIDs.Count > 0)
        {
            fields.Add(new EmbedFieldBuilder()
            .WithName("Substitutes:")
            .WithValue(string.Join("\n", SubstituteIDs.Select(id => Program.Guild.GetUser(id).DisplayName))));
        }
        return new EmbedBuilder()
            .WithTitle($"{Team.TeamName} - Squad {SquadNumber}")
            .WithColor((await Program.Guild.GetRoleAsync(Team.TeamRoleID)).Color)
            .WithThumbnailUrl(Team.TeamLogoURL)
            .WithFields(fields);
    }
}

/// <summary>
/// A Week of a <see cref="Season"/>
/// </summary>
internal class Week
{
    public enum WeekState
    {
        Unpublished,
        Current,
        Finished
    }
    public int WeekId { get; set; }
    public required int WeekNumber { get; set; }
    public int SeasonId { get; set; }
    public required Season Season { get; set; }
    public List<Match> Matches { get; } = [];
    /// <summary>
    /// This week's random mapping of a matches 1st squad's 1st, 2nd, and 3rd players to the 2nd squad's players.
    /// </summary>
    // I feel like this could be phrased better.
    public int[] Players123Mappings { get; } = [1, 2, 3];
    public required WeekState State { get; set; }

    public EmbedBuilder GetDefaultEmbed()
    {
        List<EmbedFieldBuilder> fields = [];
        foreach (Match match in Matches)
        {
            fields.Add(new EmbedFieldBuilder()
                .WithName($"{match.HomeSquad.Team.TeamName} - Squad {match.HomeSquad.SquadNumber} {(match.Winner == Match.MatchState.Undecided ? "vs" : $"{match.HomeGameWins}-{match.AwayGameWins}")} {match.AwaySquad.Team.TeamName} - Squad {match.AwaySquad.SquadNumber}")
                .WithValue(string.Join("\n", match.Games.Select(g => $"{Program.Guild.GetUser(g.HomePlayerID).DisplayName} {(match.Winner == Match.MatchState.Undecided ? "vs" : $"{g.HomePlayerWins}-{g.AwayPlayerWins}")} {Program.Guild.GetUser(g.AwayPlayerID).DisplayName}"))));
        }
        return new EmbedBuilder()
            .WithTitle($"Season {Season.SeasonNumber} - Week {WeekNumber}")
            .WithFields(fields);
    }
}

/// <summary>
/// A regular season <see cref="Week"/>
/// </summary>
internal class SeasonWeek : Week
{

}

/// <summary>
/// A playoff <see cref="Week"/>
/// </summary>
internal class PlayoffWeek : Week
{

}

/// <summary>
/// Three bo3s between two <see cref="Squad"/>s
/// </summary>
internal class Match
{
    public enum MatchState
    {
        Undecided,
        Home,
        Away,
        Tie
    }
    public int MatchId { get; set; }
    public int WeekId { get; set; }
    public required Week Week { get; set; }
    public int HomeSquadId { get; set; }
    [NotMapped]
    public List<Squad> Squads => [HomeSquad, AwaySquad];
    public required Squad HomeSquad { get; set; }
    public int AwaySquadId { get; set; }
    public required Squad AwaySquad { get; set; }
    public List<Game> Games { get; } = [];
    public List<Substitution> Substitutions { get; } = [];
    public MatchState Winner { get; set; } = MatchState.Undecided;
    [NotMapped]
    public Squad? WinningSquad => Winner == MatchState.Home ? HomeSquad : Winner == MatchState.Away ? AwaySquad : null;
    [NotMapped]
    public int HomeGameWins => Games.Count(g => g.State == Game.GameState.Home);
    [NotMapped]
    public int AwayGameWins => Games.Count(g => g.State == Game.GameState.Away);
    [NotMapped]
    public int HomeClashWins => Games.Select(g => g.HomePlayerWins).Sum();
    [NotMapped]
    public int AwayClashWins => Games.Select(g => g.AwayPlayerWins).Sum();
}

/// <summary>
/// A player Substitustion for a <see cref="Match"/>
/// </summary>
internal class Substitution
{
    public int SubstitutionId { get; set; }
    public required ulong PlayerID { get; set; }
    public required ulong SubstituteID { get; set; }
}

/// <summary>
/// A single bo3 between two players
/// </summary>
internal class Game
{
    public enum GameState
    {
        Undecided,
        Home,
        Away,
        DoubleLoss
    }
    public int GameId { get; set; }
    public GameState State = GameState.Undecided;
    public required ulong HomePlayerID { get; set; }
    public required ulong AwayPlayerID { get; set; }
    public int HomePlayerWins { get; set; } = 0;
    public int AwayPlayerWins { get; set; } = 0;
}