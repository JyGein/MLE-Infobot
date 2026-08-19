using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Query;

namespace MLE_Infobot.Commands;

internal class Leaderboard : CommandBase
{
    const string COMMANDNAME = "leaderboard";

    const string SEASONNUMBEROPTIONNAME = "season-number";
    const string LEADERBOARDPAGEOPTIONNAME = "leaderboard-page";

    public override async Task SubscribeCommand(DiscordSocketClient client)
    {
        client.SlashCommandExecuted += CommandExecuted;
        client.ButtonExecuted += ButtonClicked;
    }
    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        await guild.CreateApplicationCommandAsync(new SlashCommandBuilder()
            .WithName(COMMANDNAME)
            .WithDescription("View the squads sorted by tiebreakers.")
            .AddOption(SEASONNUMBEROPTIONNAME, ApplicationCommandOptionType.Integer, "The season number you want to view. Defaults to the most recent season.")
            .AddOption(LEADERBOARDPAGEOPTIONNAME, ApplicationCommandOptionType.Integer, "The page of the leader board you want to view. Each page has 5 squads.")
            .Build());
    }

    internal async Task CommandExecuted(SocketSlashCommand slashCommand)
    {
        if (slashCommand.Data.Name != COMMANDNAME) return;
        await slashCommand.DeferAsync(ephemeral: true);
        LeagueDBContext dBContext = new();
        bool isAdmin = IsAdmin(slashCommand);
        if (isAdmin ? !await dBContext.Seasons.AnyAsync() : !await dBContext.Seasons.AnyAsync(s => s.State != Season.SeasonState.Unpublished))
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "There are no seasons to view.";
            });
            await dBContext.DisposeAsync();
            return;
        }
        List<Season> Seasons = await dBContext.Seasons
            .Include(s => s.Divisions)
            .ThenInclude(d => d.Squads)
            .ToListAsync();
        Season season = null!;
        if (slashCommand.Data.Options.FirstOrDefault(o => o.Name == SEASONNUMBEROPTIONNAME) is SocketSlashCommandDataOption seasonNumberOption)
        {
            if (Seasons.FirstOrDefault(s => s.SeasonNumber == (long)seasonNumberOption.Value) is not Season s || (s.State == Season.SeasonState.Unpublished && !isAdmin))
            {
                await slashCommand.ModifyOriginalResponseAsync((mp) =>
                {
                    mp.Content = "That season does not exist!";
                });
                await dBContext.DisposeAsync();
                return;
            }
            season = s;
        }
        else
        {
            season = Seasons.Where(s => isAdmin || s.State != Season.SeasonState.Unpublished).OrderByDescending(s => s.SeasonNumber).First();
        }
        long leaderboardPage = 0;
        if (slashCommand.Data.Options.FirstOrDefault(o => o.Name == LEADERBOARDPAGEOPTIONNAME) is SocketSlashCommandDataOption weekNumberOption)
        {
            leaderboardPage = (long)weekNumberOption.Value;
            if (season.Squads.Count / 5.0 + 1 < leaderboardPage)
            {
                await slashCommand.ModifyOriginalResponseAsync((mp) =>
                {
                    mp.Content = "That page does not exist.";
                });
                await dBContext.DisposeAsync();
                return;
            }
        }
        else leaderboardPage = 1;

        await ViewLeaderboardPage(slashCommand, season, leaderboardPage);

        await dBContext.DisposeAsync();
    }

    internal async Task ButtonClicked(SocketMessageComponent messageComponent)
    {
        if (!messageComponent.Data.CustomId.Contains(COMMANDNAME)) return; //interaction shouldn't have been for me
        System.Text.RegularExpressions.Match m = LeaderboardInteractionIDPattern().Match(messageComponent.Data.CustomId);
        if (!m.Success) return; //interaction couldn't be parsed
        await messageComponent.DeferAsync();
        LeagueDBContext dBContext = new();
        
        if (!long.TryParse(m.Groups[1].Value, out long seasonNumber)) return; //regex somehow captured a digit that couldn't be parsed to a long
        if (await dBContext.Seasons.Include(s => s.Divisions).ThenInclude(d => d.Squads).FirstOrDefaultAsync(s => s.SeasonNumber == seasonNumber) is not Season s) return; //the interaction had a season number that isn't there
        bool isAdmin = IsAdmin(messageComponent);
        if (!isAdmin && s.State == Season.SeasonState.Unpublished) return; //somehow a non-admin is viewing an unpublished season
        if (!long.TryParse(m.Groups[2].Value, out long pageNumber)) return; //regex somehow captured a digit that couldn't be parsed to a long

        await ViewLeaderboardPage(messageComponent, s, pageNumber);

        await dBContext.DisposeAsync();
    }

    public static async Task ViewLeaderboardPage(SocketMessageComponent smc, Season s, long page)
    {
        (Embed[], List<FileAttachment>, ComponentBuilder) messageComponents = await CreateLeaderboardPage(s, page);
        await smc.ModifyOriginalResponseAsync(mp =>
        {
            mp.Embeds = messageComponents.Item1;
            mp.Attachments = messageComponents.Item2;
            mp.Components = messageComponents.Item3.Build();
        });
    }

    public static async Task ViewLeaderboardPage(SocketSlashCommand ssc, Season s, long page)
    {
        (Embed[], List<FileAttachment>, ComponentBuilder) messageComponents = await CreateLeaderboardPage(s, page);
        await ssc.ModifyOriginalResponseAsync(mp =>
        {
            mp.Embeds = messageComponents.Item1;
            mp.Attachments = messageComponents.Item2;
            mp.Components = messageComponents.Item3.Build();
        });
    }

    static async Task<(Embed[], List<FileAttachment>, ComponentBuilder)> CreateLeaderboardPage(Season s, long page)
    {
        LeagueDBContext dBContext = new();
        Season season = (Season)(await dBContext.FindAsync(typeof(Season), s.SeasonId))!;
        await dBContext.Entry(season)
            .Collection(s => s.Divisions)
            .Query()
            .Include(d => d.Squads)
            .LoadAsync();
        await dBContext.Entry(season)
            .Collection(s => s.PlayoffWeeks)
            .LoadAsync();
        List<Squad> squads = [];
        if (season.PlayoffWeeks.Count == 0)
        {
            squads = await Squad.OrderByTiebreakers(season.Squads);
        }
        else
        {
            season.PlayoffWeeks.ForEach(async w => await dBContext.Entry(w).Collection(w => w.Matches).Query().Include(m => m.HomeSquad).Include(m => m.AwaySquad).LoadAsync());
            List<Squad> allSquads = await Squad.OrderByTiebreakers(season.Squads);
            foreach (Week pw in season.PlayoffWeeks)
            {
                switch (pw.State)
                {
                    case Week.WeekState.Unpublished:
                        continue;
                    case Week.WeekState.Finished:
                        List<Squad> winningSquads = await Squad.OrderByTiebreakers([.. pw.Matches.Select(m => m.WinningSquad!).Intersect(allSquads)]);
                        winningSquads.ForEach(s => allSquads.Remove(s));
                        squads = [.. squads.Concat(winningSquads)];
                        goto case Week.WeekState.Current;
                    case Week.WeekState.Current:
                        List<Squad> nextSquads = await Squad.OrderByTiebreakers([.. pw.Matches.SelectMany(m => m.Squads).Intersect(allSquads)]);
                        nextSquads.ForEach(s => allSquads.Remove(s));
                        squads = [.. squads.Concat(nextSquads)];
                        break;
                }
            }
            squads = [.. squads.Concat(allSquads)];
        }
        squads = [.. squads.Skip((int)((page - 1) * 5))];
        if (squads.Count > 5) squads = [.. squads.Take(5)];
        Embed[] embeds = [];
        List<FileAttachment> fileAttachments = [];
        foreach (Squad sq in squads)
        {
            (EmbedBuilder eb, FileAttachment fa) = await sq.GetDefaultEmbed(true);
            embeds = [.. embeds.Append(eb.Build())];
            if (fileAttachments.All(otherfa => otherfa.FileName != fa.FileName)) fileAttachments.Add(fa);
        }
        ComponentBuilder buttons = new();
        if (page != 1) buttons.WithButton("◀", $"{COMMANDNAME}:{season.SeasonNumber}:{page - 1}");
        if (s.Squads.Count > page * 5) buttons.WithButton("▶", $"{COMMANDNAME}:{season.SeasonNumber}:{page + 1}");
        await dBContext.DisposeAsync();
        return (embeds, fileAttachments, buttons);
    }
}
