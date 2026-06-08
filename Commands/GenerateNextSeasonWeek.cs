using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MLE_Infobot.Commands;

/// <summary>
/// just for copy pasting to make more comamnds
/// </summary>
internal class GenerateNextSeasonWeek : CommandBase
{
    const string COMMANDNAME = "generate-next-season-week";

    const string ONETOOPTIONNAME = "one-to";
    const string TWOTOOPTIONNAME = "two-to";
    const string THREETOOPTIONNAME = "three-to";

    public override async Task SubscribeCommand(DiscordSocketClient client)
    {
        client.SlashCommandExecuted += CommandExecuted;
    }
    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        await guild.CreateApplicationCommandAsync(new SlashCommandBuilder()
            .WithName(COMMANDNAME)
            .WithDescription($"Generates the games for the next season week's matches. {Messages.REQUIRESADMIN}")
            .AddOption(ONETOOPTIONNAME, ApplicationCommandOptionType.Integer, "Which player on the away squad player 1 on each home squad will face. Leave all blank for random.")
            .AddOption(TWOTOOPTIONNAME, ApplicationCommandOptionType.Integer, "Which player on the away squad player 2 on each home squad will face. Leave all blank for random.")
            .AddOption(THREETOOPTIONNAME, ApplicationCommandOptionType.Integer, "Which player on the away squad player 3 on each home squad will face. Leave all blank for random.")
            .Build());
    }

    internal async Task CommandExecuted(SocketSlashCommand slashCommand)
    {
        if (slashCommand.Data.Name != COMMANDNAME) return;
        if (!IsAdmin(slashCommand))
        {
            await slashCommand.RespondAsync("You must be an admin to run this command!", ephemeral: true);
            return;
        }
        await slashCommand.DeferAsync(ephemeral: true);

        LeagueDBContext dBContext = new();

        if (await dBContext.Seasons.FirstOrDefaultAsync(s => s.State == Season.SeasonState.Started) is not Season season)
        {
            if (await dBContext.Seasons.FirstOrDefaultAsync(s => s.State == Season.SeasonState.Unpublished) is not Season s)
            {
                await slashCommand.ModifyOriginalResponseAsync((mp) =>
                {
                    mp.Content = "There is no current or unpublished season.";
                });
                await dBContext.DisposeAsync();
                return;
            }
            season = s;
        }

        if (await dBContext.Entry(season).Collection(s => s.SeasonWeeks).Query().Include(w => w.Matches).Where(w => w.State == Week.WeekState.Unpublished).OrderBy(w => w.WeekNumber).FirstOrDefaultAsync() is not SeasonWeek week)
        {
            if (await dBContext.Entry(season).Collection(s => s.PlayoffWeeks).Query().AnyAsync())
            {
                await slashCommand.ModifyOriginalResponseAsync((mp) =>
                {
                    mp.Content = "Playoffs have already started for this season. If you are trying to generate the first week of a new season, make sure the previous one is marked as finished with /finish-season.";
                });
                await dBContext.DisposeAsync();
                return;
            }
            week = new SeasonWeek() { Season = season, State = Week.WeekState.Unpublished, IsOnlyPartiallyFilled = true, WeekNumber = (await dBContext.Entry(season).Collection(s => s.SeasonWeeks).Query().OrderByDescending(w => w.WeekNumber).FirstAsync()).WeekNumber + 1 };
            await dBContext.Entry(season).Collection(s => s.SeasonWeeks).LoadAsync();
            season.SeasonWeeks.Add(week);
        }

        if (week.HasBeenGenerated == true)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "The next week has already been generated!";
            });
            await dBContext.DisposeAsync();
            return;
        }

        if (await dBContext.Entry(season).Collection(s => s.SeasonWeeks).Query().FirstOrDefaultAsync(w => w.WeekNumber == week.WeekNumber - 1) is Week previousWeek)
        {
            if (await dBContext.Entry(previousWeek).Collection(w => w.Matches).Query().AnyAsync(m => m.Winner == Match.MatchState.Undecided))
            {
                await slashCommand.ModifyOriginalResponseAsync((mp) =>
                {
                    mp.Content = "All matches in the previous week must be decided.";
                });
                await dBContext.DisposeAsync();
                return;
            }
        }

        if (slashCommand.Data.Options.FirstOrDefault(op => op.Name == ONETOOPTIONNAME) is SocketSlashCommandDataOption oneToOptionData)
        {
            List<MappingVal> playerMappingVals = [];
            List<long> testCase = [1, 2, 3];
            long oneTo = (long)oneToOptionData.Value;
            if (!testCase.Contains(oneTo))
            {
                await slashCommand.ModifyOriginalResponseAsync((mp) =>
                {
                    mp.Content = "Must input one each of 1 2 and 3.";
                });
                await dBContext.DisposeAsync();
                return;
            }
            playerMappingVals.Add((oneTo, week));
            testCase.Remove(oneTo);
            if (slashCommand.Data.Options.FirstOrDefault(op => op.Name == TWOTOOPTIONNAME) is not SocketSlashCommandDataOption twoToOptionData)
            {
                await slashCommand.ModifyOriginalResponseAsync((mp) =>
                {
                    mp.Content = "You must input either all 3 values or none.";
                });
                await dBContext.DisposeAsync();
                return;
            }
            long twoTo = (long)twoToOptionData.Value;
            if (!testCase.Contains(twoTo))
            {
                await slashCommand.ModifyOriginalResponseAsync((mp) =>
                {
                    mp.Content = "Must input one each of 1 2 and 3.";
                });
                await dBContext.DisposeAsync();
                return;
            }
            playerMappingVals.Add((twoTo, week));
            testCase.Remove(twoTo);
            if (slashCommand.Data.Options.FirstOrDefault(op => op.Name == THREETOOPTIONNAME) is not SocketSlashCommandDataOption threeToOptionData)
            {
                await slashCommand.ModifyOriginalResponseAsync((mp) =>
                {
                    mp.Content = "You must input either all 3 values or none.";
                });
                await dBContext.DisposeAsync();
                return;
            }
            long threeTo = (long)threeToOptionData.Value;
            if (!testCase.Contains(threeTo))
            {
                await slashCommand.ModifyOriginalResponseAsync((mp) =>
                {
                    mp.Content = $"Must input one each of 1 2 and 3.";
                });
                await dBContext.DisposeAsync();
                return;
            }
            playerMappingVals.Add((threeTo, week));
            week.Players123Mappings = playerMappingVals;
        }
        else
        {
            Random rnd = new(week.Matches.Select(m => m.MatchId).Sum());
            week.Players123Mappings = [.. new List<MappingVal> { (1, week), (2, week), (3, week) }.OrderBy(_ => rnd.Next())];
        }

        await dBContext.Entry(week).Collection(w => w.Matches).Query().Include(m => m.HomeSquad).Include(m => m.AwaySquad).LoadAsync();
        if (week.IsOnlyPartiallyFilled)
        {
            List<Squad> pairableSquads = [.. (await dBContext.Entry(season).Collection(s => s.Divisions).Query().Include(d => d.Squads).ThenInclude(s => s.Team).ToListAsync()).SelectMany(d => d.Squads).Where(s => !week.Matches.Any(m => m.HomeSquadId == s.SquadId || m.AwaySquadId == s.SquadId))];
            List<Squad> rankedSquads = [..(await Squad.OrderByTiebreakers(pairableSquads)).OrderByDescending(s => s.NumByes)];
            while (rankedSquads.Count > 1)
            {
                Squad squad = rankedSquads.Pop();
                Squad opposingSquad = rankedSquads.OrderBy(s => s.Matches.Count(m => m.Squads.Contains(squad))).OrderBy(s => s.Team == squad.Team ? 1 : 0).First();
                rankedSquads.Remove(opposingSquad);
                week.Matches.Add(new() { HomeSquad = squad, AwaySquad = opposingSquad, Week = week });
            }
            week.IsOnlyPartiallyFilled = false;
        }

        foreach (Match match in week.Matches)
        {
            match.Squads.ForEach(async s => await dBContext.Entry(s).Collection(s => s.PlayerIDs).LoadAsync());
            await dBContext.Entry(match).Collection(m => m.Games).LoadAsync();
            match.Games.ForEach(g => dBContext.Entry(g).State = EntityState.Deleted);
            match.Games.Clear();
            foreach (int i in week.Players123Mappings)
            {
                match.Games.Add(new() { Match = match, HomePlayerID = match.HomeSquad.PlayerIDs[match.Games.Count], AwayPlayerID = match.AwaySquad.PlayerIDs[i-1] });
            }
        }
        week.HasBeenGenerated = true;

        await dBContext.SaveChangesAsync();

        Console.WriteLine($"Generated season {season.SeasonNumber} Week {week.WeekNumber}.");
        Embed[] embeds = [.. (await week.GetEmbed()).Select(eb => eb.Build())];
        await slashCommand.ModifyOriginalResponseAsync((mp) =>
        {
            mp.Content = $"Generated season {season.SeasonNumber} Week {week.WeekNumber}.";
            mp.Embeds = embeds;
        });
        await dBContext.DisposeAsync();
    }
}
