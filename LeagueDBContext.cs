using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Principal;
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
    public enum SeasonState
    {
        Unpublished,
        Started,
        Finished
    }
    public int SeasonId { get; set; }
    public required long SeasonNumber { get; set; }
    public List<Squad> Squads { get; } = [];
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
        await Program.LeagueDatabase.SaveChangesAsync();
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

internal class Squad
{
    public int SquadId { get; set; }
    public required int SquadNumber { get; set; }
    public int TeamId { get; set; }
    public required Team Team { get; set; }
    public int SeasonId { get; set; }
    public required Season Season { get; set; }
    public List<ulong> PlayerIDs { get; } = [];
    public List<ulong> SubstituteIDs { get; } = [];

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
}

internal class SeasonWeek : Week
{

}

internal class PlayoffWeek : Week
{

}

internal class Match
{
    public enum MatchState
    {
        Undecided,
        Home,
        Away
    }
    public int MatchId { get; set; }
    public int WeekId { get; set; }
    public required Week Week { get; set; }
    public int HomeSquadId { get; set; }
    public required Squad HomeSquad { get; set; }
    public int AwaySquadId { get; set; }
    public required Squad AwaySquad { get; set; }
    public List<Game> Games { get; } = [];
    public List<Substitution> Substitutions { get; } = [];
    public MatchState Winner { get; set; } = MatchState.Undecided;
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