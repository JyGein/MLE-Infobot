using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace MLE_Infobot.Commands;

internal class ViewSeason : CommandBase
{
    const string COMMANDNAME = "view-season";

    const string SEASONNUMBEROPTIONNAME = "season-number";
    const string WEEKNUMBEROPTIONNAME = "week-number";

    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        client.SlashCommandExecuted += CommandExecuted;
        client.ButtonExecuted += ButtonClicked;
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
        LeagueDBContext dBContext = new();
        if (!dBContext.Seasons.Any())
        {
            await slashCommand.RespondAsync("There are no seasons to view.", ephemeral: true);
            return;
        }
        bool isAdmin = IsAdmin(slashCommand);
        Season season = null!;
        if (slashCommand.Data.Options.FirstOrDefault(o => o.Name == SEASONNUMBEROPTIONNAME) is SocketSlashCommandDataOption seasonNumberOption)
        {
            if (dBContext.Seasons.FirstOrDefault(s => s.SeasonNumber == (long)seasonNumberOption.Value) is not Season s || (s.State == Season.SeasonState.Unpublished && !isAdmin))
            {
                await slashCommand.RespondAsync("That season does not exist!", ephemeral: true);
                return;
            }
            season = s;
        }
        else
        {
            season = dBContext.Seasons.Where(s => s.State != Season.SeasonState.Unpublished).OrderByDescending(s => s.SeasonNumber).First();
        }
        Week week = null!;
        if (slashCommand.Data.Options.FirstOrDefault(o => o.Name == WEEKNUMBEROPTIONNAME) is SocketSlashCommandDataOption weekNumberOption)
        {
            if (season.SeasonWeeks.Cast<Week>().Concat(season.PlayoffWeeks).FirstOrDefault(w => w.WeekNumber == (long)weekNumberOption.Value) is not Week w)
            {
                await slashCommand.RespondAsync("That week does not exist!", ephemeral: true);
                return;
            }
            week = w;
        }
        else
        {
            week = season.GetCurrentOrFirstWeek();
        }
        await slashCommand.DeferAsync(ephemeral: true);

        await dBContext.DisposeAsync();

        await slashCommand.ModifyOriginalResponseAsync((mp) =>
        {
            ViewSeasonPage(mp, week, isAdmin);
        });
    }

    internal async Task ButtonClicked(SocketMessageComponent messageComponent)
    {
        if (!messageComponent.Data.CustomId.Contains(COMMANDNAME)) return; //interaction shouldn't have been for me
        System.Text.RegularExpressions.Match m = ViewSeasonInteractionIDPattern().Match(messageComponent.Data.CustomId);
        if (!m.Success) return; //interaction couldn't be parsed

        LeagueDBContext dBContext = new();
        
        if (!long.TryParse(m.Groups[1].Value, out long seasonNumber)) return; //regex somehow captured a digit that couldn't be parsed to a long
        if (await dBContext.Seasons.FirstOrDefaultAsync(s => s.SeasonNumber == seasonNumber) is not Season s) return; //the interaction had a season number that isn't there
        bool isAdmin = IsAdmin(messageComponent);
        if (!isAdmin && s.State == Season.SeasonState.Unpublished) return; //somehow a non-admin is viewing an unpublished season
        if (!long.TryParse(m.Groups[2].Value, out long weekNumber)) return; //regex somehow captured a digit that couldn't be parsed to a long
        if (s.AllWeeks.FirstOrDefault(w => w.WeekNumber == weekNumber) is not Week w) return; //the interaction had a week number that isn't there

        await dBContext.DisposeAsync();
        
        await messageComponent.Message.ModifyAsync((mp) =>
        {
            ViewSeasonPage(mp, w, isAdmin);
        });
    }

    /// <summary>
    /// Modifys a <see cref="MessageProperties"/> to be a specific week of a season displayed as a page that can be navigated to different weeks of the season.
    /// </summary>
    /// <param name="mp"></param>
    /// <param name="w"></param>
    /// <returns></returns>
    public void ViewSeasonPage(MessageProperties mp, Week w, bool isAdmin)
    {
        mp.Embed = w.GetDefaultEmbed().Build();
        ComponentBuilder buttons = new();
        if (w.WeekNumber != 1) buttons.WithButton("◀", $"{COMMANDNAME}:{w.Season.SeasonNumber}:{w.WeekNumber - 1}");
        if (w.WeekNumber != w.Season.AllWeeks.Count) buttons.WithButton("▶", $"{COMMANDNAME}:{w.Season.SeasonNumber}:{w.WeekNumber + 1}");
        mp.Components = buttons.Build();
    }
}
