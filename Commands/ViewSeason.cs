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

    const string SEASONNUMBER = "season-number";
    const string WEEKNUMBER = "week-number";

    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        client.SlashCommandExecuted += CommandExecuted;
        client.ButtonExecuted += ButtonClicked;
        await guild.CreateApplicationCommandAsync(new SlashCommandBuilder()
            .WithName(COMMANDNAME)
            .WithDescription("View a season.")
            .AddOption(SEASONNUMBER, ApplicationCommandOptionType.Integer, "The season number you want to view. Defaults to the most recent season.")
            .AddOption(WEEKNUMBER, ApplicationCommandOptionType.Integer, "The week number of the season you want to intially view.")
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
        if (slashCommand.Data.Options.FirstOrDefault(o => o.Name == SEASONNUMBER) is SocketSlashCommandDataOption seasonNumberOption)
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
        if (slashCommand.Data.Options.FirstOrDefault(o => o.Name == WEEKNUMBER) is SocketSlashCommandDataOption weekNumberOption)
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
            ViewSeasonPage(mp, week);
        });
    }

    internal async Task ButtonClicked(SocketMessageComponent messageComponent)
    {
        System.Text.RegularExpressions.Match m = ViewSeasonInteractionIDPattern().Match(messageComponent.Data.CustomId);

        messageComponent.Message.ModifyAsync((mp) =>
        {
            ViewSeasonPage(mp, week);
        });
    }

    /// <summary>
    /// Modifys a <see cref="MessageProperties"/> to be a specific week of a season displayed as a page that can be navigated to different weeks of the season.
    /// </summary>
    /// <param name="mp"></param>
    /// <param name="w"></param>
    /// <returns></returns>
    public void ViewSeasonPage(MessageProperties mp, Week w)
    {
        mp.Embed = w.GetDefaultEmbed().Build();
        ComponentBuilder buttons = new();
        if (w.WeekNumber != 1) buttons.WithButton("◀", $"{COMMANDNAME}:{w.Season.SeasonNumber}:{w.WeekNumber - 1}");
        if (w.WeekNumber != w.Season.AllWeeks.Count) buttons.WithButton("▶", $"{COMMANDNAME}:{w.Season.SeasonNumber}:{w.WeekNumber + 1}");
        mp.Components = buttons.Build();
    }
}
