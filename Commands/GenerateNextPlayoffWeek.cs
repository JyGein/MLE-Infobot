using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace MLE_Infobot.Commands;

/// <summary>
/// just for copy pasting to make more comamnds
/// </summary>
internal class GenerateNextPlayoffWeek : CommandBase
{
    const string COMMANDNAME = "generate-next-playoff-week";

    const string ONETOOPTIONNAME = "one-to";
    const string TWOTOOPTIONNAME = "two-to";
    const string THREETOOPTIONNAME = "three-to";
    const int TOPCUTSIZE = 8;

    public override async Task SubscribeCommand(DiscordSocketClient client)
    {
        client.SlashCommandExecuted += CommandExecuted;
    }
    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        await guild.CreateApplicationCommandAsync(new SlashCommandBuilder()
            .WithName(COMMANDNAME)
            .WithDescription($"Generates the games for the next playoff week's matches. {Messages.REQUIRESADMIN}")
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

        if (await dBContext.Seasons.Include(s => s.PlayoffWeeks).ThenInclude(w => w.Matches).ThenInclude(m => m.Games).FirstOrDefaultAsync(s => s.State == Season.SeasonState.Started) is not Season season)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "There is no current season.";
            });
            await dBContext.DisposeAsync();
            return;
        }

        List<long> playerMappingInts = [];
        if (slashCommand.Data.Options.FirstOrDefault(op => op.Name == ONETOOPTIONNAME) is SocketSlashCommandDataOption oneToOptionData)
        {
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
            playerMappingInts.Add(oneTo);
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
            playerMappingInts.Add(twoTo);
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
            playerMappingInts.Add(threeTo);
        }
        else
        {
            Random rnd = new(DateTime.Today.GetHashCode());
            playerMappingInts = [.. new List<long> { 1, 2, 3 }.OrderBy(_ => rnd.Next())];
        }

        PlayoffWeek newWeek = null!;
        if (season.PlayoffWeeks.Count <= 0)
        {
            await dBContext.Entry(season).Collection(s => s.SeasonWeeks).Query().Include(w => w.Matches).ThenInclude(m => m.Games).LoadAsync();
            if (season.SeasonWeeks.Count == 0)
            {
                await slashCommand.ModifyOriginalResponseAsync((mp) =>
                {
                    mp.Content = "There are no weeks.";
                });
                await dBContext.DisposeAsync();
                return;
            }
            if (!season.SeasonWeeks.SelectMany(w => w.Matches).All(m => m.Winner != Match.MatchState.Undecided))
            {
                await slashCommand.ModifyOriginalResponseAsync((mp) =>
                {
                    mp.Content = "All matches must be submitted in the season weeks.";
                });
                await dBContext.DisposeAsync();
                return;
            }
            List<Squad> playoffSquads = [];
            List<Squad> allSquads = [.. (await dBContext.Entry(season).Collection(s => s.Divisions).Query().Include(d => d.Squads).ThenInclude(s => s.Team).ToListAsync()).SelectMany(d => d.Squads)];
            foreach (Division division in season.Divisions)
            {
                if (division.Squads.Count == 0) continue;
                Squad bestDivisionSquad = (await Squad.OrderByTiebreakers(division.Squads)).First();
                playoffSquads.Add(bestDivisionSquad);
                allSquads.Remove(bestDivisionSquad);
            }
            playoffSquads = await Squad.OrderByTiebreakers(playoffSquads);
            allSquads = await Squad.OrderByTiebreakers(allSquads);
            playoffSquads.AddRange(allSquads.Take(TOPCUTSIZE - playoffSquads.Count));
            //playoffSquads = await Squad.OrderByTiebreakers(playoffSquads); //division squads should be higher seeds
            for (int i = 1; i <= TOPCUTSIZE; i++)
            {
                playoffSquads[i - 1].PlayoffSeed = i;
            }
            List<int> seeds = [1];
            while (seeds.Count < TOPCUTSIZE) //this only works if topcutsize is a power of 2
            {
                int newSize = seeds.Count * 2;
                for (int i = 0; i < newSize; i += 2)
                {
                    seeds.Insert(i + 1, newSize - (seeds[i] - 1));
                }
            }
            //seeds.ForEach(i => Program.BotLog(i.ToString())); //debug line
            newWeek = new() { Season = season, State = Week.WeekState.Unpublished, WeekNumber = season.AllWeeks.OrderByDescending(w => w.WeekNumber).First().WeekNumber + 1, Players123Mappings = [.. playerMappingInts.Select(i => (i, newWeek))] };
            while (seeds.Count > 1)
            {
                newWeek.Matches.Add(new() { Week = newWeek, HomeSquad = playoffSquads[seeds.Pop() - 1], AwaySquad = playoffSquads[seeds.Pop() - 1] });
            }
        }
        else
        {
            if (season.PlayoffWeeks.OrderByDescending(w => w.WeekNumber).First().Matches.Count <= 1)
            {
                await slashCommand.ModifyOriginalResponseAsync((mp) =>
                {
                    mp.Content = "Cannot generate a playoff week after finals.";
                });
                await dBContext.DisposeAsync();
                return;
            }
            if (!season.PlayoffWeeks.SelectMany(w => w.Matches).All(m => m.Winner != Match.MatchState.Undecided))
            {
                await slashCommand.ModifyOriginalResponseAsync((mp) =>
                {
                    mp.Content = "All matches must be submitted in the previous week.";
                });
                await dBContext.DisposeAsync();
                return;
            }
            if (!season.PlayoffWeeks.SelectMany(w => w.Matches).All(m => m.Winner != Match.MatchState.Tie))
            {
                await slashCommand.ModifyOriginalResponseAsync((mp) =>
                {
                    mp.Content = "Playoff matches cannot be ties.";
                });
                await dBContext.DisposeAsync();
                return;
            }
            Week previousWeek = season.PlayoffWeeks.OrderByDescending(w => w.WeekNumber).First();
            foreach (Match match in previousWeek.Matches)
            {
                await dBContext.Entry(match).Reference(m => m.HomeSquad).LoadAsync();
                await dBContext.Entry(match).Reference(m => m.AwaySquad).LoadAsync();
            }
            List<Squad> winningSquads = [.. previousWeek.Matches.Select(m => m.WinningSquad!)];
            newWeek = new() { Season = season, State = Week.WeekState.Unpublished, Players123Mappings = [.. playerMappingInts.Select(i => (i, newWeek))], WeekNumber = previousWeek.WeekNumber + 1 };
            while (winningSquads.Count > 1)
            {
                List<Squad> nextSquads = [winningSquads.Pop(), winningSquads.Pop()];
                if (nextSquads.All(s => s.PlayoffSeed != null)) nextSquads = [.. nextSquads.OrderBy(s => s.PlayoffSeed)]; //makes sure higher seed is home (if they are both seeded)
                newWeek.Matches.Add(new() { Week = newWeek, HomeSquad = nextSquads.Pop(), AwaySquad = nextSquads.Pop() });
            }
        }

        foreach (Match match in newWeek.Matches)
        {
            foreach (Squad squad in match.Squads)
            {
                await dBContext.Entry(squad).Collection(s => s.PlayerIDs).LoadAsync();
            }
            foreach (int i in newWeek.Players123Mappings)
            {
                match.Games.Add(new() { Match = match, HomePlayerID = match.HomeSquad.PlayerIDs[match.Games.Count], AwayPlayerID = match.AwaySquad.PlayerIDs[i - 1] });
            }
        }
        newWeek.HasBeenGenerated = true;

        season.PlayoffWeeks.Add(newWeek);

        await dBContext.SaveChangesAsync();

        string message = $"Generated season {season.SeasonNumber} Week {newWeek.WeekNumber}.";
        Console.WriteLine(message);
        Embed[] embeds = [.. (await newWeek.GetEmbed()).Select(eb => eb.Build())];
        await slashCommand.ModifyOriginalResponseAsync((mp) =>
        {
            mp.Content = message;
            mp.Embeds = embeds;
        });
        await dBContext.DisposeAsync();
    }
}
