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

internal class ViewSeason : CommandBase
{
    const string COMMANDNAME = "view-season";

    const string SEASONNUMBEROPTIONNAME = "season-number";
    const string WEEKNUMBEROPTIONNAME = "week-number";

    public override async Task SubscribeCommand(DiscordSocketClient client)
    {
        client.SlashCommandExecuted += CommandExecuted;
        client.ButtonExecuted += ButtonClicked;
    }
    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        await guild.CreateApplicationCommandAsync(new SlashCommandBuilder()
            .WithName(COMMANDNAME)
            .WithDescription("View a season.")
            .AddOption(SEASONNUMBEROPTIONNAME, ApplicationCommandOptionType.Integer, "The season number you want to view. Defaults to the most recent season.")
            .AddOption(WEEKNUMBEROPTIONNAME, ApplicationCommandOptionType.Integer, "The week number of the season you want to intially view.")
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
            .Include(s => s.PlayoffWeeks)
            .Include(s => s.SeasonWeeks)
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
        Week week = null!;
        if (slashCommand.Data.Options.FirstOrDefault(o => o.Name == WEEKNUMBEROPTIONNAME) is SocketSlashCommandDataOption weekNumberOption)
        {
            if (season.AllWeeks.FirstOrDefault(w => w.WeekNumber == (long)weekNumberOption.Value) is not Week w)
            {
                await slashCommand.ModifyOriginalResponseAsync((mp) =>
                {
                    mp.Content = "That week does not exist!";
                });
                await dBContext.DisposeAsync();
                return;
            }
            week = w;
        }
        else
        {
            week = await season.GetCurrentOrFirstWeek();
        }


        await slashCommand.ModifyOriginalResponseAsync(async (mp) =>
        {
            await ViewSeasonPage(mp, week, isAdmin);
        });
        await dBContext.DisposeAsync();
    }

    internal async Task ButtonClicked(SocketMessageComponent messageComponent)
    {
        if (!messageComponent.Data.CustomId.Contains(COMMANDNAME)) return; //interaction shouldn't have been for me
        System.Text.RegularExpressions.Match m = ViewSeasonInteractionIDPattern().Match(messageComponent.Data.CustomId);
        if (!m.Success) return; //interaction couldn't be parsed
        await messageComponent.DeferAsync();
        LeagueDBContext dBContext = new();
        
        if (!long.TryParse(m.Groups[1].Value, out long seasonNumber)) return; //regex somehow captured a digit that couldn't be parsed to a long
        if (await dBContext.Seasons.Include(s => s.PlayoffWeeks).Include(s => s.SeasonWeeks).FirstOrDefaultAsync(s => s.SeasonNumber == seasonNumber) is not Season s) return; //the interaction had a season number that isn't there
        bool isAdmin = IsAdmin(messageComponent);
        if (!isAdmin && s.State == Season.SeasonState.Unpublished) return; //somehow a non-admin is viewing an unpublished season
        if (!long.TryParse(m.Groups[2].Value, out long weekNumber)) return; //regex somehow captured a digit that couldn't be parsed to a long
        if (s.AllWeeks.FirstOrDefault(w => w.WeekNumber == weekNumber) is not Week w) return; //the interaction had a week number that isn't there

        await messageComponent.ModifyOriginalResponseAsync(async (mp) =>
        {
            await ViewSeasonPage(mp, w, isAdmin);
        });
        await dBContext.DisposeAsync();
    }

    /// <summary>
    /// Modifys a <see cref="MessageProperties"/> to be a specific week of a season displayed as a page that can be navigated to different weeks of the season.
    /// </summary>
    /// <param name="mp"></param>
    /// <param name="w"></param>
    /// <returns></returns>
    public static async Task ViewSeasonPage(MessageProperties mp, Week w, bool isAdmin)
    {
        LeagueDBContext dBContext = new();
        Week week = (Week)(await dBContext.FindAsync(typeof(Week), w.WeekId))!;
        await dBContext.Entry(week)
            .Reference(w => w.Season)
            .LoadAsync();
        await dBContext.Entry(week.Season)
            .Collection(s => s.SeasonWeeks)
            .LoadAsync();
        await dBContext.Entry(week.Season)
            .Collection(s => s.PlayoffWeeks)
            .LoadAsync();
        Embed[] embeds = [.. (await week.GetEmbed()).Select(eb => eb.Build())];
        mp.Embeds = embeds;
        ComponentBuilder buttons = new();
        if (week.WeekNumber != 1) buttons.WithButton("◀", $"{COMMANDNAME}:{week.Season.SeasonNumber}:{week.WeekNumber - 1}");
        if (week.WeekNumber != week.Season.AllWeeks.Count) buttons.WithButton("▶", $"{COMMANDNAME}:{week.Season.SeasonNumber}:{week.WeekNumber + 1}");
        mp.Components = buttons.Build();
    }
}
