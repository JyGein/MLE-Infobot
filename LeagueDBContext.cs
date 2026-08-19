using Discord;
using Discord.Audio.Streams;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.ValueGeneration.Internal;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
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
        string path = AppContext.BaseDirectory;

        path = Path.Combine(path, "Data");
        Directory.CreateDirectory(path);
        path = Path.Combine(path, "league.db");
        Console.WriteLine($"{DateTime.Now:T}::Database loaded from: {path}");
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
            return playerName.GetPlayerName().Replace("_", "\\_");
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

    //Known issues:
    // Outlier squad could have the same team, meaning they're forced to play each other.
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
            .ThenInclude(w => w.Matches)
            .FirstAsync(s => s.SeasonId == SeasonId);
        foreach (SeasonWeek sw in season.SeasonWeeks)
        {
            dBContext.Entry(sw).State = EntityState.Deleted;
        }
        season.SeasonWeeks.Clear();
        int biggestDivisionSize = season.Divisions.Count > 0 ? season.Divisions.Max(d => d.Squads.Count) : 0;
        if (biggestDivisionSize <= 1)
        {
            season.SeasonWeeks.Add(new() { IsOnlyPartiallyFilled = true, Season = season, State = Week.WeekState.Unpublished, WeekNumber = 1 });
            await dBContext.SaveChangesAsync();
            await dBContext.DisposeAsync();
            return;
        }

        Dictionary<Squad, List<Match>> squadMatches = [];
        void AddMatch(Match match)
        {
            squadMatches[match.HomeSquad] = squadMatches.TryGetValue(match.HomeSquad, out List<Match>? hMatches) ? [.. hMatches.Append(match)] : [match];
            squadMatches[match.AwaySquad] = squadMatches.TryGetValue(match.AwaySquad, out List<Match>? aMatches) ? [.. aMatches.Append(match)] : [match];
        }

        Random rnd = new(season.Squads.Select(s => s.SquadId).Sum() + DateTime.Now.Second + DateTime.Now.Day + DateTime.Now.Minute);

        for (int i = 1; i < 8; i++)
        {
            season.SeasonWeeks.Add(new() { IsOnlyPartiallyFilled = false, Season = season, State = Week.WeekState.Unpublished, WeekNumber = i });
        }
        Week tempWeek = season.SeasonWeeks.First();

        foreach (Division division in season.Divisions)
        {
            int count = 1;
            foreach (Squad squad in division.Squads)
            {
                foreach (Squad opposingSquad in division.Squads.Skip(count))
                {
                    List<Squad> theTwoSquads = [squad, opposingSquad];
                    theTwoSquads = [.. theTwoSquads.OrderBy(s => rnd.Next())];
                    Match match = new() { HomeSquad = theTwoSquads.First(), AwaySquad = theTwoSquads.Last(), Week = tempWeek };
                    AddMatch(match);
                }
                count++;
            }
        }

        Dictionary<Division, Division> rivalDivisions = [];
        foreach (Division division in season.Divisions)
        {
            if (!rivalDivisions.TryGetValue(division, out _))
            {
                Division rivalDivision = season.Divisions.Where(d => d != division && !rivalDivisions.TryGetValue(d, out _)).OrderBy(d => rnd.Next()).First();
                rivalDivisions[division] = rivalDivision;
                rivalDivisions[rivalDivision] = division;
            }
        }
        Dictionary<Division, List<Squad>> ASquads = season.Divisions.Select(d => new KeyValuePair<Division, List<Squad>>(d, [.. d.Squads.Where(s => s.ABCRank == Squad.ABCRanking.A)])).ToDictionary();
        Dictionary<Division, List<Squad>> BSquads = season.Divisions.Select(d => new KeyValuePair<Division, List<Squad>>(d, [.. d.Squads.Where(s => s.ABCRank == Squad.ABCRanking.B)])).ToDictionary();
        Dictionary<Division, List<Squad>> CSquads = season.Divisions.Select(d => new KeyValuePair<Division, List<Squad>>(d, [.. d.Squads.Where(s => s.ABCRank == Squad.ABCRanking.C)])).ToDictionary();
        Dictionary<Squad, int> timesPlayed = season.Divisions.SelectMany(d => d.Squads).Select(s => new KeyValuePair<Squad, int>(s, 0)).ToDictionary();

        foreach (Squad squad in ASquads.Values.SelectMany(s => s))
        {
            Division rivalDivision = rivalDivisions[squad.Division];
            Console.WriteLine($"Next squad's rival division is {rivalDivision.DivisionName}");
            while (timesPlayed[squad] < 3)
            {
                Console.WriteLine($"generating A Squad: {squad.Team.TeamName} squad {squad.SquadNumber}");
                Squad opposingSquad = rivalDivision.Squads.Where(s => timesPlayed[s] < 3 && !squadMatches[squad].Any(m => m.Squads.Contains(s)) && s.Team != squad.Team).Reverse().OrderBy(s => (int)s.ABCRank).First();
                Console.WriteLine($"they're playing: {opposingSquad.Team.TeamName} squad {opposingSquad.SquadNumber}");
                List<Squad> theTwoSquads = [squad, opposingSquad];
                theTwoSquads = [.. theTwoSquads.OrderBy(s => rnd.Next())];
                AddMatch(new() { HomeSquad = theTwoSquads.First(), AwaySquad = theTwoSquads.Last(), Week = tempWeek });
                timesPlayed[squad] += 1;
                timesPlayed[opposingSquad] += 1;
            }
        }

        foreach (Squad squad in CSquads.Values.SelectMany(s => s))
        {
            Division rivalDivision = rivalDivisions[squad.Division];
            Console.WriteLine($"Next squad's rival division is {rivalDivision.DivisionName}");
            while (timesPlayed[squad] < 3)
            {
                Console.WriteLine($"generating C Squad: {squad.Team.TeamName} squad {squad.SquadNumber}");
                Squad opposingSquad = rivalDivision.Squads.Where(s => timesPlayed[s] < 3 && !squadMatches[squad].Any(m => m.Squads.Contains(s)) && s.Team != squad.Team).Reverse().OrderByDescending(s => (int)s.ABCRank).First();
                Console.WriteLine($"they're playing: {opposingSquad.Team.TeamName} squad {opposingSquad.SquadNumber}");
                List<Squad> theTwoSquads = [squad, opposingSquad];
                theTwoSquads = [.. theTwoSquads.OrderBy(s => rnd.Next())];
                AddMatch(new() { HomeSquad = theTwoSquads.First(), AwaySquad = theTwoSquads.Last(), Week = tempWeek });
                timesPlayed[squad] += 1;
                timesPlayed[opposingSquad] += 1;
            }
        }

        foreach (Squad squad in BSquads.Values.SelectMany(s => s).Where(s => timesPlayed[s] < 3).OrderByDescending(s => rivalDivisions[s.Division].Squads.Any(rs => rs.Team == s.Team) ? 1 : 0))
        {
            Division rivalDivision = rivalDivisions[squad.Division];
            Console.WriteLine($"Next squad's rival division is {rivalDivision.DivisionName}");
            while (timesPlayed[squad] < 3)
            {
                Console.WriteLine($"generating B Squad: {squad.Team.TeamName} squad {squad.SquadNumber}");
                Squad opposingSquad = rivalDivision.Squads.Where(s => timesPlayed[s] < 3 && !squadMatches[squad].Any(m => m.Squads.Contains(s)) && s.Team != squad.Team).OrderBy(s => rnd.Next()).First();
                Console.WriteLine($"they're playing: {opposingSquad.Team.TeamName} squad {opposingSquad.SquadNumber}");
                List<Squad> theTwoSquads = [squad, opposingSquad];
                theTwoSquads = [.. theTwoSquads.OrderBy(s => rnd.Next())];
                AddMatch(new() { HomeSquad = theTwoSquads.First(), AwaySquad = theTwoSquads.Last(), Week = tempWeek });
                timesPlayed[squad] += 1;
                timesPlayed[opposingSquad] += 1;
            }
        }

        foreach (Week week in season.SeasonWeeks)
        {
            Console.WriteLine($"generating week {week.WeekNumber}");
            int count = 0;
            while (week.Matches.Count < 10 && count < 1000)
            {
                if (squadMatches.Values.SelectMany(m => m).Where(m => !week.Matches.Any(wm => wm.Squads.Contains(m.HomeSquad) || wm.Squads.Contains(m.AwaySquad))).OrderBy(_ => rnd.Next()).FirstOrDefault() is not Match match)
                {
                    foreach(Match m in week.Matches)
                    {
                        squadMatches[m.HomeSquad].Add(m);
                        squadMatches[m.AwaySquad].Add(m);
                    }
                    week.Matches.Clear();
                    count++;
                    continue;
                }
                week.Matches.Add(match);
                match.Week = week;
                squadMatches[match.HomeSquad].Remove(match);
                squadMatches[match.AwaySquad].Remove(match);
            }
            if (count > 1000)
            {
                Console.WriteLine("Could not generate properly within 1000 tries.");
                week.Matches.Clear();
                week.Matches.First();
            }
        }
        
        //foreach ((Squad squad, List<Match> matches) in squadMatches)
        //{
        //    foreach (Match match in matches)
        //    {
        //        if (season.SeasonWeeks.Any(w => w.Matches.Contains(match))) continue;
        //        Week week = season.SeasonWeeks.Where(w => !w.Matches.Any(m => m.Squads.Contains(match.HomeSquad) || m.Squads.Contains(match.AwaySquad))).OrderBy(w => rnd.Next()).First();
        //        week.Matches.Add(match);
        //        match.Week = week;
        //        squadMatches[match.Squads.Single(s => s != squad)].Remove(match);
        //    }
        //}

        foreach (Week week in season.SeasonWeeks)
        {
            week.Players123Mappings = [.. new List<MappingVal> { (1, week), (2, week), (3, week) }.OrderBy(_ => rnd.Next())];
            foreach (Match match in week.Matches)
            {
                match.Squads.ForEach(async s => await dBContext.Entry(s).Collection(s => s.PlayerIDs).LoadAsync());
                foreach (int i in week.Players123Mappings)
                {
                    match.Games.Add(new() { Match = match, HomePlayerID = match.HomeSquad.PlayerIDs[match.Games.Count], AwayPlayerID = match.AwaySquad.PlayerIDs[i - 1] });
                }
            }
            week.HasBeenGenerated = true;
        }

        //Dictionary<Squad, List<Squad>> SquadsFaced = [];
        //List<Squad>? DivisionSquadsFaced(Squad squad)
        //    => SquadsFaced.TryGetValue(squad, out List<Squad>? facedSquads) ? [.. facedSquads.Where(s => s.Division == squad.Division)] : null;
        //void AddSquad(Squad squadOne, Squad squadTwo)
        //{
        //    if (SquadsFaced.TryGetValue(squadOne, out List<Squad>? sfacedSquads)) sfacedSquads.Add(squadTwo); else SquadsFaced[squadOne] = [squadTwo];
        //    if (SquadsFaced.TryGetValue(squadTwo, out List<Squad>? ofacedSquads)) ofacedSquads.Add(squadOne); else SquadsFaced[squadTwo] = [squadOne];
        //}

        ////making seed from squad ids as it will be usually be unique each time they are randomized
        //Random rnd = new(season.Squads.Select(s => s.SquadId).Sum());
        //for (int i = 1; i <= biggestDivisionSize + biggestDivisionSize % 2 - 1; i++)
        //{
        //    SeasonWeek week = new() { Season = season, WeekNumber = i, State = Week.WeekState.Unpublished, IsOnlyPartiallyFilled = false };
        //    List<Squad> outlierSquads = [];

        //    foreach (Division division in season.Divisions)
        //    {
        //        List<Squad> divisionSquads = division.Squads;
        //        if (divisionSquads.Count == 0) continue;
        //        if (divisionSquads.All(s => DivisionSquadsFaced(s) is List<Squad> facedSquads && facedSquads.Count >= divisionSquads.Count - 1))
        //        {
        //            week.IsOnlyPartiallyFilled = true;
        //            continue;
        //        }
        //        List<Squad> unmatchedSquads = [.. divisionSquads.OrderByDescending(s => s.NumByes).ThenBy(s => rnd.Next())];
        //        while (unmatchedSquads.Count > 1)
        //        {
        //            Squad squad = unmatchedSquads.Pop();
        //            List<Squad> potentialOpponents = [.. unmatchedSquads.Where(s => !(DivisionSquadsFaced(squad) ?? []).Contains(s))];
        //            if (potentialOpponents.FirstOrDefault() is Squad opponent)
        //            {
        //                week.Matches.Add(new() { HomeSquad = squad, AwaySquad = opponent, Week = week });
        //                unmatchedSquads.Remove(opponent);
        //                AddSquad(squad, opponent);
        //            }
        //            else
        //            {
        //                //outlierSquads.Add(squad);
        //                week.IsOnlyPartiallyFilled = true;
        //            }
        //        }
        //        if (unmatchedSquads.Count == 1) outlierSquads.Add(unmatchedSquads.Single());
        //    }

        //    if (outlierSquads.Count >= 2) week.IsOnlyPartiallyFilled = true;
        //    //outlierSquads = [.. outlierSquads.OrderByDescending(s => s.NumByes).ThenBy(s => rnd.Next())];
        //    //while (outlierSquads.Count > 1)
        //    //{
        //    //    Squad squad = outlierSquads.Pop();
        //    //    List<Squad> potentialOpponents = [.. (SquadsFaced.TryGetValue(squad, out List<Squad>? squads) ? outlierSquads.OrderBy(s => squads.Count(ss => ss == s)) : outlierSquads.Order()).ThenBy(s => rnd.Next())];
        //    //    Squad opponent = potentialOpponents.Pop();
        //    //    week.Matches.Add(new() { HomeSquad = squad, AwaySquad = opponent, Week = week });
        //    //    outlierSquads.Remove(opponent);
        //    //    AddSquad(squad, opponent);
        //    //}
        //    season.SeasonWeeks.Add(week);
        //}
        for (int i = 8; i < 10; i++)
        {
            season.SeasonWeeks.Add(new() { IsOnlyPartiallyFilled = true, Season = season, State = Week.WeekState.Unpublished, WeekNumber = i });
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

    public async Task<EmbedBuilder> GetDivisionsEmbed()
    {
        LeagueDBContext dBContext = new();
        Season season = (Season)(await dBContext.FindAsync(GetType(), SeasonId))!;
        await dBContext.Entry(season)
            .Collection(s => s.Divisions)
            .LoadAsync();
        EmbedBuilder embedBuilder = new();
        embedBuilder.WithTitle($"Season {season.SeasonNumber}'s Divisions");
        if (season.Divisions.Count == 0)
        {
            embedBuilder.WithDescription("There are none!");
        }
        else
        {
            foreach (Division division in season.Divisions)
            {
                await dBContext.Entry(division)
                    .Collection(d => d.Squads)
                    .LoadAsync();
                embedBuilder.AddField(new EmbedFieldBuilder()
                    .WithName($"{division.DivisionName}")
                    .WithValue($"{division.Squads.Count} Squads"));
            }
        }
        return embedBuilder;
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

    public async Task<(EmbedBuilder, FileAttachment)> GetDefaultEmbed()
    {
        LeagueDBContext dBContext = new();
        string teamCaptain = await dBContext.GetPlayerName(TeamCaptainID);
        await dBContext.DisposeAsync();
        FileAttachment teamLogo = new(TeamLogoURL, isThumbnail: true);
        return (new EmbedBuilder()
            .WithTitle(TeamName)
            .WithColor((await Program.Guild.GetRoleAsync(TeamRoleID)).Color)
            .WithThumbnailUrl($"attachment://{teamLogo.FileName}")
            .WithDescription($"Team Captain: {teamCaptain}"), 
            teamLogo);
    }
}

internal class Division
{
    public int DivisionId { get; set; }
    public required string DivisionName { get; set; }
    public List<Squad> Squads { get; set; } = [];
    public int SeasonId { get; set; }
    public required Season Season { get; set; }

    public async Task<(List<EmbedBuilder>, List<FileAttachment>)> GetSquadsEmbeds(bool withABCRank = false, bool withoutMatchScore = false)
    {
        LeagueDBContext dBContext = new();
        Division division = (Division)(await dBContext.FindAsync(GetType(), DivisionId))!;
        await dBContext.Entry(division)
            .Collection(d => d.Squads)
            .LoadAsync();
        await dBContext.Entry(division)
            .Reference(d => d.Season)
            .LoadAsync();
        List<EmbedBuilder> embeds = [];
        embeds.Add(new EmbedBuilder()
            .WithTitle($"{DivisionName} Division - Season {division.Season.SeasonNumber}"));
        List<FileAttachment> teamLogos = [];
        List<Squad> potentiallySortedSquads = division.Squads;
        if (await dBContext.Entry(division.Season).Collection(s => s.SeasonWeeks).Query().CountAsync(w => w.State == Week.WeekState.Finished || w.State == Week.WeekState.Current) > 0) potentiallySortedSquads = await Squad.OrderByTiebreakers(potentiallySortedSquads);
        else potentiallySortedSquads = [.. potentiallySortedSquads.OrderBy(s => s.ABCRank)];
        foreach (Squad s in potentiallySortedSquads)
        {
            (EmbedBuilder embed, FileAttachment teamLogo) = await s.GetDefaultEmbed(withABCRank: withABCRank, withoutMatchScore: withoutMatchScore);
            embeds.Add(embed);
            teamLogos.Add(teamLogo);
        }
        return (embeds, teamLogos);
    }
}

internal class Squad
{
    public enum ABCRanking
    {
        A,
        B,
        C
    }
    public int SquadId { get; set; }
    public required int SquadNumber { get; set; }
    public int TeamId { get; set; }
    public required Team Team { get; set; }
    public int DivisionId { get; set; }
    public required Division Division { get; set; }
    public List<PlayerSquadPlayer> PlayerIDs { get; set; } = [];
    public List<SubstituteSquadPlayer> SubstituteIDs { get; set; } = [];
    public int? PlayoffSeed { get; set; } = null;
    public ABCRanking ABCRank { get; set; } = ABCRanking.B;
    [NotMapped]
    public int Points => MatchWins * 3 + MatchTies;
    [NotMapped]
    public int MatchWins => Matches.Count(m => m.WinningSquad == this) + NumByes;
    [NotMapped]
    public int MatchLosses => Matches.Count(m => m.Winner != Match.MatchState.Undecided && m.Winner != Match.MatchState.Tie && m.WinningSquad != this);
    [NotMapped]
    public int MatchTies => Matches.Count(m => m.Winner == Match.MatchState.Tie);
    [NotMapped]
    public int GameWins => Matches.Sum(m => m.HomeSquad == this ? m.HomeGameWins : m.AwayGameWins);
    [NotMapped]
    public int ClashWins => Matches.Sum(m => m.HomeSquad == this ? m.HomeClashWins : m.AwayClashWins);
    [NotMapped]
    public Season Season => Division.Season;
    [NotMapped]
    public List<Match> Matches => [.. Season.AllWeeks.SelectMany(w => w.Matches.Where(m => m.AwaySquad == this || m.HomeSquad == this))];
    [NotMapped]
    public List<Match> CompletedMatches => [.. Matches.Where(m => m.Winner != Match.MatchState.Undecided)];
    [NotMapped]
    public int NumByes => Season.SeasonWeeks.Count(w => w.HasBeenGenerated == true) - Season.SeasonWeeks.SelectMany(w => w.Matches.Where(m => m.AwaySquad == this || m.HomeSquad == this)).Count();

    public async Task<(EmbedBuilder, FileAttachment)> GetDefaultEmbed(bool withDivision = false, bool withABCRank = false, bool withoutMatchScore = false)
    {
        LeagueDBContext dBContext = new();
        Squad squad = (Squad)(await dBContext.FindAsync(GetType(), SquadId))!;
        await dBContext.Entry(squad)
            .Collection(s => s.PlayerIDs)
            .LoadAsync();
        await dBContext.Entry(squad)
            .Collection(s => s.SubstituteIDs)
            .LoadAsync();
        await dBContext.Entry(squad)
            .Reference(s => s.Team)
            .LoadAsync();
        await dBContext.Entry(squad)
            .Reference(s => s.Division)
            .LoadAsync();
        await dBContext.Entry(squad.Division)
            .Reference(d => d.Season)
            .LoadAsync();
        bool showRecord = false;
        if (squad.Season.State != Season.SeasonState.Unpublished && !withoutMatchScore)
        {
            await dBContext.Entry(squad.Season).Collection(s => s.SeasonWeeks).Query().Include(w => w.Matches).LoadAsync();
            await dBContext.Entry(squad.Season).Collection(s => s.PlayoffWeeks).Query().Include(w => w.Matches).LoadAsync();
            foreach (Match m in squad.Season.AllWeeks.SelectMany(w => w.Matches))
            {
                await dBContext.Entry(m).Reference(m => m.HomeSquad).LoadAsync();
                await dBContext.Entry(m).Reference(m => m.AwaySquad).LoadAsync();
                await dBContext.Entry(m).Collection(m => m.Games).LoadAsync();
            }
            showRecord = true;
        }
        List<EmbedFieldBuilder> fields = [new EmbedFieldBuilder()
            .WithName("Players:")
            .WithValue(string.Join("\n", squad.PlayerIDs.Select(async id => await dBContext.GetPlayerName(id.PlayerID)).Select(t => t.Result)))];
        if (squad.SubstituteIDs.Count > 0)
        {
            fields.Add(new EmbedFieldBuilder()
            .WithName("Substitutes:")
            .WithValue(string.Join("\n", squad.SubstituteIDs.Select(async id => await dBContext.GetPlayerName(id.PlayerID)).Select(t => t.Result))));
        }
        string record = showRecord ? $"\n{squad.MatchWins}-{squad.MatchLosses}{(squad.MatchTies > 0 ? $"-{squad.MatchTies}" : "")}" : "";
        FileAttachment teamLogo = new(squad.Team.TeamLogoURL, isThumbnail: true);
        Color color = Color.Default;
        try
        {
            color = (await Program.Guild.GetRoleAsync(squad.Team.TeamRoleID)).Color;
        }
        catch { }
        return (new EmbedBuilder()
            .WithTitle($"{squad.Team.TeamName} - Squad {squad.SquadNumber}" + (withDivision ? $" - {squad.Division.DivisionName} Division" : "") + record + (withABCRank ? $"\nRank {squad.ABCRank}" : ""))
            .WithColor(color)
            .WithThumbnailUrl($"attachment://{teamLogo.FileName}")
            .WithFields(fields), teamLogo);
    }

    public async Task<(List<EmbedBuilder>, FileAttachment)> GetWholeSeasonEmbed()
    {
        LeagueDBContext dBContext = new();
        Squad squad = (Squad)(await dBContext.FindAsync(GetType(), SquadId))!;
        await dBContext.Entry(squad).Reference(s => s.Division).Query().Include(d => d.Season).LoadAsync();
        await dBContext.Entry(squad).Reference(s => s.Team).LoadAsync();
        Season season = squad.Season;
        await dBContext.Entry(season).Collection(s => s.SeasonWeeks).LoadAsync();
        await dBContext.Entry(season).Collection(s => s.PlayoffWeeks).LoadAsync();
        season.AllWeeks.ForEach(async w => await dBContext.Entry(w).Collection(w => w.Matches).LoadAsync());
        List<Match> matches = [.. season.AllWeeks.SelectMany(w => w.Matches.Where(m => m.HomeSquad == squad || m.AwaySquad == squad))];
        List<List<EmbedFieldBuilder>> fieldss = [[]];
        foreach (Match match in matches)
        {
            if (fieldss.Last().Count > 20) fieldss.Add([]);
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
            fieldss.Last().Add(new EmbedFieldBuilder()
                .WithName($"{match.HomeSquad.Team.TeamName} - Squad {match.HomeSquad.SquadNumber}")
                .WithValue(match.Games.Count > 0 ? string.Join("\n", match.Games.Select(async g => $"{await dBContext.GetPlayerName(g.HomePlayerIDWithSub)}").Select(t => t.Result)) : "Player matchups will be")
                .WithIsInline(true));
            fieldss.Last().Add(new EmbedFieldBuilder()
                .WithName($"{(match.Winner == Match.MatchState.Undecided ? "vs" : $" {match.HomeGameWins}-{match.AwayGameWins} ")}")
                .WithValue(match.Games.Count > 0 ? string.Join("\n", match.Games.Select(async g => $"{(match.Winner == Match.MatchState.Undecided ? "vs" : $" {g.HomePlayerWins}-{g.AwayPlayerWins} ")}").Select(t => t.Result)) : "displayed")
                .WithIsInline(true));
            fieldss.Last().Add(new EmbedFieldBuilder()
                .WithName($"{match.AwaySquad.Team.TeamName} - Squad {match.AwaySquad.SquadNumber}")
                .WithValue(match.Games.Count > 0 ? string.Join("\n", match.Games.Select(async g => $"{await dBContext.GetPlayerName(g.AwayPlayerIDWithSub)}").Select(t => t.Result)) : "when the week is published.")
                .WithIsInline(true));
        }
        foreach (SeasonWeek week in season.SeasonWeeks.Where(w => w.IsOnlyPartiallyFilled = true && !w.Matches.Intersect(matches).Any()))
        {
            if (fieldss.Last().Count > 24) fieldss.Add([]);
            fieldss.Last().Add(new EmbedFieldBuilder()
                .WithName($"Week {week.WeekNumber}")
                .WithValue($"Matchup is based on previous results and will be generated week-of."));
        }
        FileAttachment teamLogo = new(squad.Team.TeamLogoURL, isThumbnail: true);
        List<EmbedBuilder> embeds = [];
        bool doneFirstEmbed = false;
        if (fieldss.First().Count != 0) fieldss.Last().Add(new EmbedFieldBuilder().WithName("=========================================================").WithValue("_ _"));
        foreach (List<EmbedFieldBuilder> fields in fieldss)
        {
            EmbedBuilder embed = new EmbedBuilder().WithFields(fields);
            if (!doneFirstEmbed)
            {
                embed.WithTitle($"{squad.Team.TeamName} - Squad {squad.SquadNumber}\nSeason {season.SeasonNumber}{(fieldss.First().Count != 0 ? "\n==================================================" : "")}")
                    .WithThumbnailUrl($"attachment://{teamLogo.FileName}");
                doneFirstEmbed = true;
            }
            embed.WithColor((await Program.Guild.GetRoleAsync(squad.Team.TeamRoleID)).Color);
            embeds.Add(embed);
        }
        return (embeds, teamLogo);
    }

    public static async Task<List<Squad>> OrderByTiebreakers(List<Squad> yourContextSquadsToOrder)
    {
        if (yourContextSquadsToOrder.Count == 0) return yourContextSquadsToOrder;
        LeagueDBContext dBContext = new();
        List<Squad> squadsToOrder = [.. yourContextSquadsToOrder.Select(async s => (Squad)(await dBContext.FindAsync(s.GetType(), s.SquadId))!).Select(t => t.Result)];
        Dictionary<int, List<Squad>> rankedSquads = [];
        //prep the dbcontext
        List<int> divisionsLoaded = [];
        List<int> seasonsLoaded = [];
        foreach (Squad squad in squadsToOrder)
        {
            if (!divisionsLoaded.Contains(squad.DivisionId))
            {
                Division division = await dBContext.Entry(squad).Reference(s => s.Division).Query().SingleAsync();
                divisionsLoaded.Add(squad.DivisionId);
                if (!seasonsLoaded.Contains(division.SeasonId))
                {
                    List<Week> weeks = [.. (await dBContext.Entry(division).Reference(d => d.Season).Query().Include(s => s.SeasonWeeks).Include(s => s.PlayoffWeeks).ToListAsync()).SelectMany(s => s.AllWeeks)];
                    foreach (Week week in weeks) await dBContext.Entry(week).Collection(w => w.Matches).Query().Include(m => m.Games).Include(m => m.HomeSquad).Include(m => m.AwaySquad).LoadAsync();
                    foreach (Squad s in weeks.SelectMany(w => w.Matches).SelectMany(m => m.Squads.Where(s => !squadsToOrder.Contains(s)))) await dBContext.Entry(s).Reference(s => s.Division).Query().Include(d => d.Season).LoadAsync();
                    seasonsLoaded.Add(division.SeasonId);
                }
            }
        }
        //tiebreaker 1 and 2; points (series wins = 3 series ties = 1), Opp Win %
        static float GetOpponentWinPer(Squad squad) 
            => squad.CompletedMatches.Select(m => m.HomeSquad == squad ? m.AwaySquad : m.HomeSquad).Select(s => s.CompletedMatches.Count != 0 ? s.MatchWins / (float)s.CompletedMatches.Count : 0).Average();
        squadsToOrder = [.. squadsToOrder.OrderByDescending(s => s.Points).ThenByDescending(GetOpponentWinPer)];
        Squad? previousSquad = null;
        foreach (Squad squad in squadsToOrder)
        {
            if (previousSquad == null)
            {
                rankedSquads[1] = [squad];
            }
            else
            {
                KeyValuePair<int, List<Squad>> previousGroup = rankedSquads.First(kv => kv.Value.Contains(previousSquad));
                if (previousSquad.Points == squad.Points && GetOpponentWinPer(previousSquad) == GetOpponentWinPer(squad))
                {
                    rankedSquads[previousGroup.Key].Add(squad);
                }
                else
                {
                    rankedSquads[previousGroup.Key + previousGroup.Value.Count] = [squad];
                }
            }
            previousSquad = squad;
        }
        Random rnd = new(squadsToOrder.Sum(s => s.SquadId));
        //tiebreaker 3; head-to-head
        if (!rankedSquads.All(kv => kv.Value.Count == 1))
        {
            rankedSquads = rankedSquads.OrderBy(kv => kv.Key).ToDictionary();
            Dictionary<int, List<Squad>> rankedSquadsToIterate = rankedSquads.ToDictionary();
            foreach(KeyValuePair<int, List<Squad>> kv in rankedSquadsToIterate)
            {
                if (kv.Value.Count == 1) continue;
                Dictionary<Squad, int> headToHeadRecord = [];
                int count = 0;
                bool goToCommonOpponentRecord = false;
                foreach (Squad squad in kv.Value)
                {
                    foreach (Squad opposingSquad in kv.Value.Skip(count+1))
                    {
                        if (squad.CompletedMatches.Any(m => m.Squads.Contains(opposingSquad)))
                        {
                            foreach (Match match in squad.CompletedMatches.Where(m => m.Squads.Contains(opposingSquad)))
                            {
                                if (match.WinningSquad == null) continue;
                                headToHeadRecord[squad] = headToHeadRecord.GetValueOrDefault(squad, 0) + (match.WinningSquad == squad ? 1 : 0);
                                headToHeadRecord[opposingSquad] = headToHeadRecord.GetValueOrDefault(opposingSquad, 0) + (match.WinningSquad == opposingSquad ? 1 : 0);
                            }
                        }
                        else
                        {
                            goToCommonOpponentRecord = true;
                            break;
                        }
                    }
                    if (goToCommonOpponentRecord) break;
                    count++;
                }
                if (!goToCommonOpponentRecord)
                {
                    count = kv.Key;
                    IOrderedEnumerable<Squad> orderedSquads = headToHeadRecord.Keys.OrderByDescending(s => headToHeadRecord[s]);
                    previousSquad = null;
                    foreach (Squad squad in orderedSquads)
                    {
                        if (previousSquad == null)
                        {
                            rankedSquads[count] = [squad];
                        }
                        else
                        {
                            KeyValuePair<int, List<Squad>> previousGroup = rankedSquads.First(kv => kv.Value.Contains(previousSquad));
                            if (headToHeadRecord[squad] == headToHeadRecord[previousSquad])
                            {
                                rankedSquads[previousGroup.Key].Add(squad);
                            }
                            else
                            {
                                rankedSquads[previousGroup.Key + previousGroup.Value.Count] = [squad];
                                count++;
                            }
                        }
                        previousSquad = squad;
                    }
                    //foreach (Squad squad in orderedSquads)
                    //{
                    //    rankedSquads[count] = [squad];
                    //    count++;
                    //}
                }
                else
                {
                    Dictionary<Squad, List<float>> commonOpponentsRecordAveragedOnce = [];
                    count = 0;
                    bool noCommonOpponents = false;
                    foreach(Squad squad in kv.Value)
                    {
                        foreach (Squad opposingSquad in kv.Value.Skip(count+1))
                        {
                            List<Squad> commonOpponents = [.. squad.CompletedMatches.Select(m => m.HomeSquad == squad ? m.AwaySquad : m.HomeSquad).Intersect(opposingSquad.CompletedMatches.Select(m => m.HomeSquad == opposingSquad ? m.AwaySquad : m.HomeSquad))];
                            if (commonOpponents.Count == 0)
                            {
                                noCommonOpponents = true;
                                break;
                            }
                            List<Match> squadMatches = [.. commonOpponents.SelectMany(cos => cos.CompletedMatches.Where(m => m.Squads.Contains(squad)))];
                            float squadAverage = squadMatches.Count(m => m.WinningSquad != null) != 0 ? squadMatches.Count(m => m.WinningSquad == squad) / (float)squadMatches.Count(m => m.WinningSquad != null) : 0;
                            commonOpponentsRecordAveragedOnce[squad] = commonOpponentsRecordAveragedOnce.TryGetValue(squad, out List<float>? squadAverages) ? [.. squadAverages.Append(squadAverage)] : [squadAverage];
                            
                            List<Match> opposingSquadMatches = [.. commonOpponents.SelectMany(cos => cos.CompletedMatches.Where(m => m.Squads.Contains(opposingSquad)))];
                            float opposingSquadAverage = opposingSquadMatches.Count(m => m.WinningSquad != null) != 0 ? opposingSquadMatches.Count(m => m.WinningSquad == opposingSquad) / (float)opposingSquadMatches.Count(m => m.WinningSquad != null) : 0;
                            commonOpponentsRecordAveragedOnce[opposingSquad] = commonOpponentsRecordAveragedOnce.TryGetValue(opposingSquad, out List<float>? opposingSquadAverages) ? [.. opposingSquadAverages.Append(opposingSquadAverage)] : [opposingSquadAverage];
                        }
                        if (noCommonOpponents)
                        {
                            break;
                        }
                        count++;
                    }
                    if (noCommonOpponents)
                    {
                        continue;
                    }
                    count = kv.Key;
                    IOrderedEnumerable<Squad> orderedSquads = commonOpponentsRecordAveragedOnce.Keys.OrderByDescending(s => commonOpponentsRecordAveragedOnce[s].Average());
                    previousSquad = null;
                    foreach (Squad squad in orderedSquads)
                    {
                        if (previousSquad == null)
                        {
                            rankedSquads[count] = [squad];
                        }
                        else
                        {
                            KeyValuePair<int, List<Squad>> previousGroup = rankedSquads.First(kv => kv.Value.Contains(previousSquad));
                            if (commonOpponentsRecordAveragedOnce[squad].Average() == commonOpponentsRecordAveragedOnce[previousSquad].Average())
                            {
                                rankedSquads[previousGroup.Key].Add(squad);
                            }
                            else
                            {
                                rankedSquads[previousGroup.Key + previousGroup.Value.Count] = [squad];
                                count++;
                            }
                        }
                        previousSquad = squad;
                    }
                    //foreach (Squad squad in orderedSquads)
                    //{
                    //    rankedSquads[count] = [squad];
                    //    count++;
                    //}
                }
            }
        }
        //tiebreaker 4 5 & 6; match wins, game wins, random
        if (!rankedSquads.All(kv => kv.Value.Count == 1))
        {
            rankedSquads = rankedSquads.OrderBy(kv => kv.Key).ToDictionary();
            Dictionary<int, List<Squad>> rankedSquadsToIterate = rankedSquads.ToDictionary();
            foreach (KeyValuePair<int, List<Squad>> kv in rankedSquadsToIterate)
            {
                if (kv.Value.Count == 1) continue;
                int count = kv.Key;
                IOrderedEnumerable<Squad> orderedSquads = kv.Value.OrderByDescending(s => s.GameWins).ThenByDescending(s => s.ClashWins).ThenBy(_ => rnd.Next());
                foreach (Squad squad in orderedSquads)
                {
                    rankedSquads[count] = [squad];
                    count++;
                }
            }
        }
        List<Squad> sortedSquads = [.. rankedSquads.OrderBy(kv => kv.Key).Select(kv => yourContextSquadsToOrder.First(s => s.SquadId == kv.Value.Single().SquadId))];
        //foreach (Squad squad in rankedSquads.OrderBy(kv => kv.Key).Select(kv => kv.Value.Single()))
        //{
        //    await dBContext.Entry(squad).Reference(s => s.Team).LoadAsync();
        //    Console.WriteLine($"{squad.Team.TeamName} - Squad {squad.SquadNumber}, Week Wins: {squad.MatchWins}, OW%: {GetOpponentWinPer(squad)}, Match wins: {squad.GameWins}, Game wins: {squad.ClashWins}");
        //}
        await dBContext.DisposeAsync();
        return sortedSquads;
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
    public bool HasBeenGenerated { get; set; } = false;
    public async Task<List<EmbedBuilder>> GetDefaultEmbed()
    {
        LeagueDBContext dBContext = new();
        Week week = (Week)(await dBContext.FindAsync(GetType(), WeekId))!;
        await dBContext.Entry(week)
            .Collection(w => w.Matches)
            .LoadAsync();
        await dBContext.Entry(week)
            .Reference(w => w.Season)
            .LoadAsync();
        List<List<EmbedFieldBuilder>> fieldss = [[]];
        foreach (Match match in week.Matches)
        {
            if (fieldss.Last().Count > 21) fieldss.Add([]);
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
            fieldss.Last().Add(new EmbedFieldBuilder()
                .WithName($"{match.HomeSquad.Team.TeamName} - Squad {match.HomeSquad.SquadNumber}")
                .WithValue(match.Games.Count > 0 ? string.Join("\n", match.Games.Select(async g => $"{await dBContext.GetPlayerName(g.HomePlayerIDWithSub)}").Select(t => t.Result)) : "Player matchups will be")
                .WithIsInline(true));
            fieldss.Last().Add(new EmbedFieldBuilder()
                .WithName($"{(match.Winner == Match.MatchState.Undecided ? "vs" : $" {match.HomeGameWins}-{match.AwayGameWins} ")}")
                .WithValue(match.Games.Count > 0 ? string.Join("\n", match.Games.Select(async g => $"{(match.Winner == Match.MatchState.Undecided ? "vs" : $" {g.HomePlayerWins}-{g.AwayPlayerWins} ")}").Select(t => t.Result)) : "displayed")
                .WithIsInline(true));
            fieldss.Last().Add(new EmbedFieldBuilder()
                .WithName($"{match.AwaySquad.Team.TeamName} - Squad {match.AwaySquad.SquadNumber}")
                .WithValue(match.Games.Count > 0 ? string.Join("\n", match.Games.Select(async g => $"{await dBContext.GetPlayerName(g.AwayPlayerIDWithSub)}").Select(t => t.Result)) : "when the week is published.")
                .WithIsInline(true));
        }
        List<EmbedBuilder> embeds = [];
        bool doneFirstEmbed = false;
        if (fieldss.First().Count != 0) fieldss.Last().Add(new EmbedFieldBuilder().WithName("===================================================================").WithValue("_ _"));
        foreach (List<EmbedFieldBuilder> fields in fieldss)
        {
            EmbedBuilder embed = new EmbedBuilder().WithFields(fields);
            if (!doneFirstEmbed)
            {
                embed.WithTitle($"Week {week.WeekNumber}\nSeason {week.Season.SeasonNumber}{(fieldss.First().Count != 0 ? "\n===========================================================" : "")}");
                doneFirstEmbed = true;
            }
            embeds.Add(embed);
        }
        return embeds;
    }

    public virtual async Task<List<EmbedBuilder>> GetEmbed()
        => await GetDefaultEmbed();
}

internal class MappingVal
{
    public int MappingValId { get; set; }
    public int WeekId { get; set; }
    public required Week Week { get; set; }
    public required long MappingValue { get; set; }
    public static implicit operator MappingVal((long i, Week week) t) => new() { MappingValue = t.i, Week = t.week };
    public static implicit operator long(MappingVal mv) => mv.MappingValue;
}

/// <summary>
/// A regular season <see cref="Week"/>
/// </summary>
internal class SeasonWeek : Week
{
    public int SeasonWeekId { get; set; }
    //so there can be a message (when the week is displayed in an embed) that when it gets published it will have matches for every squad to inform players
    public required bool IsOnlyPartiallyFilled { get; set; }

    public override async Task<List<EmbedBuilder>> GetEmbed()
    {
        List<EmbedBuilder> output = await base.GetEmbed();
        if (IsOnlyPartiallyFilled)
        {
            output.Add(new EmbedBuilder()/*.WithTitle("=================================================")*/.AddField(new EmbedFieldBuilder()
                .WithName("Does not contain all matches.")
                .WithValue("The the matches for this week will be filled in week-of as they are dependent on previous results.")));
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
    public override async Task<List<EmbedBuilder>> GetEmbed()
    {
        List<EmbedBuilder> output = await base.GetEmbed();
        LeagueDBContext dBContext = new();
        PlayoffWeek week = (PlayoffWeek)(await dBContext.FindAsync(typeof(Week), WeekId))!;
        await dBContext.Entry(week).Reference(w => w.Season).Query().Include(s => s.SeasonWeeks).LoadAsync();
        output.First().WithTitle($"Playoff Week {week.WeekNumber - week.Season.SeasonWeeks.Count}\nSeason {week.Season.SeasonNumber} - Week {week.WeekNumber}");
        return output;
    }
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

    public async Task<EmbedBuilder> GetDefaultEmbed()
    {
        LeagueDBContext dBContext = new();
        Match match = (Match)(await dBContext.FindAsync(GetType(), MatchId))!;
        await dBContext.Entry(match)
            .Reference(m => m.Week)
            .LoadAsync();
        await dBContext.Entry(match.Week)
            .Reference(w => w.Season)
            .LoadAsync();
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
        return new EmbedBuilder()
            .WithTitle($"Season {match.Week.Season.SeasonNumber} - Week {match.Week.WeekNumber}")
            .WithFields(new EmbedFieldBuilder()
                .WithName($"{match.HomeSquad.Team.TeamName} - Squad {match.HomeSquad.SquadNumber} {(match.Winner == Match.MatchState.Undecided ? "vs" : $" {match.HomeGameWins}-{match.AwayGameWins} ")} {match.AwaySquad.Team.TeamName} - Squad {match.AwaySquad.SquadNumber}")
                .WithValue(match.Games.Count > 0 ? string.Join("\n", match.Games.Select(async g => $"{await dBContext.GetPlayerName(g.HomePlayerIDWithSub)} {(match.Winner == Match.MatchState.Undecided ? "vs" : $" {g.HomePlayerWins}-{g.AwayPlayerWins} ")} {await dBContext.GetPlayerName(g.AwayPlayerIDWithSub)}").Select(t => t.Result)) : "Player matchups will be displayed when the week is published."));
    }
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
    public GameState State { get; set; } = GameState.Undecided;
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
            return user.Nickname ?? user.GlobalName ?? user.Username;
        }
        return PlayerUsername;
    }
}