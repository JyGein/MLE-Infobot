using Discord;
using Discord.Audio.Streams;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MLE_Infobot;

internal sealed class LeagueDBContext : DbContext
{
    public DbSet<Season> Seasons { get; set; }
    public DbSet<Team> Teams { get; set; }
    public DbSet<PlayerName> PlayerNames { get; set; }

    public string DbPath { get; }

    public LeagueDBContext()
    {
        Environment.SpecialFolder folder = Environment.SpecialFolder.LocalApplicationData;
        string path = Environment.GetFolderPath(folder);
        path = Path.Join(path, "league.db");
        Console.WriteLine($"Database loaded from: {path}");
        DbPath = path;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Season>().HasMany(s => s.Divisions).WithOne(d => d.Season).HasForeignKey(d => d.SeasonId);
        modelBuilder.Entity<Season>().HasMany(s => s.SeasonWeeks).WithOne(sw => sw.Season).HasForeignKey(sw => sw.SeasonId);
        modelBuilder.Entity<Season>().HasMany(s => s.PlayoffWeeks).WithOne(pw => pw.Season).HasForeignKey(pw => pw.SeasonId);
        modelBuilder.Entity<Division>().HasMany(d => d.Squads).WithOne(s => s.Division).HasForeignKey(s => s.DivisionId);
        modelBuilder.Entity<Squad>().HasMany(s => s.PlayerIDs).WithOne(psp => psp.Squad).HasForeignKey(psp => psp.SquadId);
        modelBuilder.Entity<Squad>().HasMany(s => s.SubstituteIDs).WithOne(ssp => ssp.Squad).HasForeignKey(ssp => ssp.SquadId);
        modelBuilder.Entity<Week>().HasMany(w => w.Matches).WithOne(m => m.Week).HasForeignKey(m => m.WeekId);
        modelBuilder.Entity<Week>().HasMany(w => w.Players123Mappings).WithOne(mv => mv.Week).HasForeignKey(mv => mv.WeekId);
        modelBuilder.Entity<Match>().HasMany(m => m.Games).WithOne(g => g.Match).HasForeignKey(g => g.MatchId);
        modelBuilder.Entity<Match>().HasMany(m => m.Substitutions).WithOne(s => s.Match).HasForeignKey(s => s.MatchId);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseSqlite($"Data Source={DbPath}");
        options.EnableSensitiveDataLogging(true);
    }

    public async Task UpdateUserEntry(IUser user)
    {
        if (await PlayerNames.FirstOrDefaultAsync(pn => pn.PlayerUserID == user.Id) is not PlayerName playerName)
        {
            await PlayerNames.AddAsync(new() { PlayerUserID = user.Id, PlayerUsername = user.Username });
        }
        else
        {
            playerName.PlayerUsername = user.Username;
        }
    }

    public async Task<string> GetPlayerName(ulong playerId)
    {
        if (await PlayerNames.FirstOrDefaultAsync(pn => pn.PlayerUserID == playerId) is PlayerName playerName)
        {
            return playerName.GetPlayerName();
        }
        else return "UNKNOWN";
    }
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
    public List<Squad> Squads => [.. Divisions.SelectMany(d => d.Squads)];
    public List<Division> Divisions { get; set; } = [];
    [NotMapped]
    public List<Week> AllWeeks => [.. SeasonWeeks, .. PlayoffWeeks];
    public List<SeasonWeek> SeasonWeeks { get; set; } = [];
    public List<PlayoffWeek> PlayoffWeeks { get; set; } = [];
    public required SeasonState State { get; set; }

    public async Task RandomizeGuaranteedMatches()
    {
        //this should never happen but just in case, since this is dangerous to do on an in-progress season as it clears all season weeks
        if (State != SeasonState.Unpublished) return;
        LeagueDBContext dBContext = new();
        Season season = await dBContext.Seasons
            .Include(s => s.Divisions)
            .ThenInclude(d => d.Squads)
            .ThenInclude(s => s.Team)
            .Include(s => s.SeasonWeeks)
            .FirstAsync(s => s.SeasonId == SeasonId);
        foreach (SeasonWeek sw in season.SeasonWeeks)
        {
            dBContext.Entry(sw).State = EntityState.Deleted;
        }
        season.SeasonWeeks.Clear();
        int biggestDivisionSize = season.Divisions.Max(d => d.Squads.Count);
        if (biggestDivisionSize <= 1)
        {
            season.SeasonWeeks.Add(new() { IsOnlyPartiallyFilled = true, Season = season, State = Week.WeekState.Unpublished, WeekNumber = 1 });
            await dBContext.SaveChangesAsync();
            await dBContext.DisposeAsync();
            return;
        }

        Dictionary<Squad, List<Squad>> SquadsFaced = [];
        List<Squad>? DivisionSquadsFaced(Squad squad)
            => SquadsFaced.TryGetValue(squad, out List<Squad>? facedSquads) ? [.. facedSquads.Where(s => s.Division == squad.Division)] : null;
        void AddSquad(Squad squadOne, Squad squadTwo)
        {
            if (SquadsFaced.TryGetValue(squadOne, out List<Squad>? sfacedSquads)) sfacedSquads.Add(squadTwo); else SquadsFaced[squadOne] = [squadTwo];
            if (SquadsFaced.TryGetValue(squadTwo, out List<Squad>? ofacedSquads)) ofacedSquads.Add(squadOne); else SquadsFaced[squadTwo] = [squadOne];
        }

        //making seed from squad ids as it will be usually be unique each time they are randomized
        Random rnd = new(season.Squads.Select(s => s.SquadId).Sum());
        for (int i = 1; i <= biggestDivisionSize + biggestDivisionSize % 2 - 1; i++)
        {
            SeasonWeek week = new() { Season = season, WeekNumber = i, State = Week.WeekState.Unpublished, IsOnlyPartiallyFilled = false };
            List<Squad> outlierSquads = [];

            foreach (Division division in season.Divisions)
            {
                //await dBContext.Entry(division)
                //    .Collection(d => d.Squads)
                //    .LoadAsync();
                List<Squad> divisionSquads = division.Squads;
                if (divisionSquads.All(s => DivisionSquadsFaced(s) is List<Squad> facedSquads && facedSquads.Count >= divisionSquads.Count))
                {
                    week.IsOnlyPartiallyFilled = true;
                    continue;
                }
                List<Squad> unmatchedSquads = [.. divisionSquads.OrderByDescending(s => s.NumByes).ThenBy(s => rnd.Next())];
                while (unmatchedSquads.Count > 1)
                {
                    Squad squad = unmatchedSquads.Pop();
                    List<Squad> potentialOpponents = DivisionSquadsFaced(squad)?.Except(unmatchedSquads).ToList() ?? unmatchedSquads;
                    if (potentialOpponents.FirstOrDefault() is Squad opponent)
                    {
                        week.Matches.Add(new() { HomeSquad = squad, AwaySquad = opponent, Week = week });
                        unmatchedSquads.Remove(opponent);
                        AddSquad(squad, opponent);
                    }
                    else
                    {
                        outlierSquads.Add(squad);
                    }
                }
            }

            outlierSquads = [.. outlierSquads.OrderByDescending(s => s.NumByes).ThenBy(s => rnd.Next())];
            while (outlierSquads.Count > 1)
            {
                Squad squad = outlierSquads.Pop();
                List<Squad> potentialOpponents = [.. (SquadsFaced.TryGetValue(squad, out List<Squad>? squads) ? outlierSquads.OrderBy(s => squads.Count(ss => ss == s)) : outlierSquads.Order()).ThenBy(s => rnd.Next())];
                Squad opponent = potentialOpponents.Pop();
                week.Matches.Add(new() { HomeSquad = squad, AwaySquad = opponent, Week = week });
                outlierSquads.Remove(opponent);
                AddSquad(squad, opponent);
            }
            /*
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
            */
            season.SeasonWeeks.Add(week);
        }
        await dBContext.SaveChangesAsync();
        await dBContext.DisposeAsync();
    }

    public async Task<Week> GetCurrentOrFirstWeek()
    {
        LeagueDBContext dBContext = new();
        await dBContext.Entry(this)
            .Collection(s => s.SeasonWeeks)
            .LoadAsync();
        await dBContext.Entry(this)
            .Collection(s => s.PlayoffWeeks)
            .LoadAsync();
        if (AllWeeks.FirstOrDefault(w => w.State == Week.WeekState.Current) is Week currentWeek) return currentWeek;
        return AllWeeks.First();
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
        LeagueDBContext dBContext = new();
        string teamCaptain = await dBContext.GetPlayerName(TeamCaptainID);
        await dBContext.DisposeAsync();
        return new EmbedBuilder()
            .WithTitle(TeamName)
            .WithColor((await Program.Guild.GetRoleAsync(TeamRoleID)).Color)
            .WithThumbnailUrl(TeamLogoURL)
            .WithDescription($"Team Captain: {teamCaptain}");
    }
}

internal class Division
{
    public int DivisionId { get; set; }
    public required string DivisionName { get; set; }
    public List<Squad> Squads { get; set; } = [];
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
    public List<PlayerSquadPlayer> PlayerIDs { get; set; } = [];
    public List<SubstituteSquadPlayer> SubstituteIDs { get; set; } = [];
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
    public List<Match> Matches => [.. Season.AllWeeks.SelectMany(w => w.Matches.Where(m => m.AwaySquad == this || m.HomeSquad == this))];
    [NotMapped]
    public int NumByes => Season.AllWeeks.Count - Matches.Count;

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

internal class SquadPlayer
{
    public int SquadPlayerId { get; set; }
    public int SquadId { get; set; }
    public required Squad Squad { get; set; }
    public required ulong PlayerID { get; set; }
}

internal class PlayerSquadPlayer : SquadPlayer
{
    public int PlayerSquadPlayerId { get; set; }
    public static implicit operator PlayerSquadPlayer((ulong id, Squad squad) t) => new() { PlayerID = t.id, Squad = t.squad };
    public static implicit operator ulong(PlayerSquadPlayer sp) => sp.PlayerID;
}

internal class SubstituteSquadPlayer : SquadPlayer
{
    public int SubstituteSquadPlayerId { get; set; }
    public static implicit operator SubstituteSquadPlayer((ulong id, Squad squad) t) => new() { PlayerID = t.id, Squad = t.squad };
    public static implicit operator ulong(SubstituteSquadPlayer sp) => sp.PlayerID;
}

/// <summary>
/// A Week of a <see cref="Season"/>
/// </summary>
internal class Week
{
    public int WeekId { get; set; }
    public Week()
    {
        Players123Mappings = [(1, this), (2, this), (3, this)];
    }
    public enum WeekState
    {
        Unpublished,
        Current,
        Finished
    }
    public required int WeekNumber { get; set; }
    public int SeasonId { get; set; }
    public required Season Season { get; set; }
    public List<Match> Matches { get; set; } = [];
    /// <summary>
    /// This week's random mapping of a matches 1st squad's 1st, 2nd, and 3rd players to the 2nd squad's players.
    /// </summary>
    // I feel like this could be phrased better.
    public List<MappingVal> Players123Mappings { get; set; }
    public required WeekState State { get; set; }

    public async Task<EmbedBuilder> GetDefaultEmbed()
    {
        LeagueDBContext dBContext = new();
        Week week = (Week)(await dBContext.FindAsync(GetType(), WeekId))!;
        await dBContext.Entry(week)
            .Collection(w => w.Matches)
            .LoadAsync();
        await dBContext.Entry(week)
            .Reference(w => w.Season)
            .LoadAsync();
        List<EmbedFieldBuilder> fields = [];
        foreach (Match match in week.Matches)
        {
            await dBContext.Entry(match)
                .Collection(m => m.Games)
                .LoadAsync();
            await dBContext.Entry(match)
                .Collection(m => m.Substitutions)
                .LoadAsync();
            await dBContext.Entry(match)
                .Reference(m => m.HomeSquad)
                .LoadAsync();
            await dBContext.Entry(match.HomeSquad)
                .Reference(s => s.Team)
                .LoadAsync();
            await dBContext.Entry(match)
                .Reference(m => m.AwaySquad)
                .LoadAsync();
            await dBContext.Entry(match.AwaySquad)
                .Reference(s => s.Team)
                .LoadAsync();
            fields.Add(new EmbedFieldBuilder()
                .WithName($"{match.HomeSquad.Team.TeamName} - Squad {match.HomeSquad.SquadNumber} {(match.Winner == Match.MatchState.Undecided ? "vs" : $"{match.HomeGameWins}-{match.AwayGameWins}")} {match.AwaySquad.Team.TeamName} - Squad {match.AwaySquad.SquadNumber}")
                .WithValue(match.Games.Count > 0 ? string.Join("\n", match.Games.Select(g => $"{dBContext.GetPlayerName(g.HomePlayerIDWithSub)} {(match.Winner == Match.MatchState.Undecided ? "vs" : $"{g.HomePlayerWins}-{g.AwayPlayerWins}")} {dBContext.GetPlayerName(g.AwayPlayerIDWithSub)}")) : "Player matchups will be displayed when the week is published."));
        }
        return new EmbedBuilder()
            .WithTitle($"Season {week.Season.SeasonNumber} - Week {week.WeekNumber}")
            .WithFields(fields);
    }

    public virtual async Task<EmbedBuilder> GetEmbed()
        => await GetDefaultEmbed();
}

internal class MappingVal
{
    public int MappingValId { get; set; }
    public int WeekId { get; set; }
    public required Week Week { get; set; }
    public required int MappingValue { get; set; }
    public static implicit operator MappingVal((int i, Week week) t) => new() { MappingValue = t.i, Week = t.week };
    public static implicit operator int(MappingVal mv) => mv.MappingValue;
}

/// <summary>
/// A regular season <see cref="Week"/>
/// </summary>
internal class SeasonWeek : Week
{
    public int SeasonWeekId { get; set; }
    //so there can be a message (when the week is displayed in an embed) that when it gets published it will have matches for every squad to inform players
    public required bool IsOnlyPartiallyFilled { get; set; }

    public override async Task<EmbedBuilder> GetEmbed()
    {
        EmbedBuilder output = await base.GetEmbed();
        if (IsOnlyPartiallyFilled)
        {
            output.AddField(new EmbedFieldBuilder()
                .WithName("Does not contain all matches.")
                .WithValue("The rest of the matches for this week will be filled in week-of as they are dependent on previous results."));
        }
        //if there is an odd number of squads (and this is not a partially filled week) find the bye squad and append a field saying that
        return output;
    }
}

/// <summary>
/// A playoff <see cref="Week"/>
/// </summary>
internal class PlayoffWeek : Week
{
    public int PlayoffWeekId { get; set; }
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
    [NotMapped]
    public List<Squad> Squads => [HomeSquad, AwaySquad];
    public int HomeSquadId { get; set; }
    public required Squad HomeSquad { get; set; }
    public int AwaySquadId { get; set; }
    public required Squad AwaySquad { get; set; }
    public List<Game> Games { get; set; } = [];
    public List<Substitution> Substitutions { get; set; } = [];
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
    public int MatchId { get; set; }
    public required Match Match { get; set; }
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
    public int MatchId { get; set; }
    public required Match Match { get; set; }
    public required ulong HomePlayerID { get; set; }
    [NotMapped]
    public ulong HomePlayerIDWithSub => Match.Substitutions.FirstOrDefault(s => s.PlayerID == HomePlayerID) is Substitution sub ? sub.SubstituteID : HomePlayerID;
    public required ulong AwayPlayerID { get; set; }
    [NotMapped]
    public ulong AwayPlayerIDWithSub => Match.Substitutions.FirstOrDefault(s => s.PlayerID == AwayPlayerID) is Substitution sub ? sub.SubstituteID : AwayPlayerID;
    public int HomePlayerWins { get; set; } = 0;
    public int AwayPlayerWins { get; set; } = 0;
}

internal class PlayerName
{
    public int PlayerNameId { get; set; }
    public required ulong PlayerUserID { get; set; }
    public required string PlayerUsername { get; set; }
    public string GetPlayerName()
    {
        if (Program.Guild.GetUser(PlayerUserID) is SocketGuildUser user)
        {
            PlayerUsername = user.Username;
            return user.DisplayName;
        }
        return PlayerUsername;
    }
}