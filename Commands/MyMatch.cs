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
internal class MyMatch : CommandBase
{
    const string COMMANDNAME = "my-match";

    public override async Task SubscribeCommand(DiscordSocketClient client)
    {
        client.SlashCommandExecuted += CommandExecuted;
    }
    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        await guild.CreateApplicationCommandAsync(new SlashCommandBuilder()
            .WithName(COMMANDNAME)
            .WithDescription($"Shows you your match for this week.")
            .Build());
    }

    internal async Task CommandExecuted(SocketSlashCommand slashCommand)
    {
        if (slashCommand.Data.Name != COMMANDNAME) return;
        await slashCommand.DeferAsync(ephemeral: true);

        ulong playerId = slashCommand.User.Id;
        LeagueDBContext dBContext = new();
        List<Season> Season = await dBContext.Seasons.Include(s => s.SeasonWeeks).Include(s => s.PlayoffWeeks).ToListAsync();
        if (Season.FirstOrDefault(s => s.State == MLE_Infobot.Season.SeasonState.Started) is not Season season)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "There is no current season.";
            });
            return;
        }
        if (season.AllWeeks.FirstOrDefault(w => w.State == Week.WeekState.Current) is not Week week)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "There is no current week.";
            });
            return;
        }
        List<Match> Matches = await dBContext.Entry(week).Collection(w => w.Matches).Query().Include(m => m.Games).Include(m => m.Substitutions).ToListAsync();
        if (Matches.FirstOrDefault(m => m.Games.Any(g => g.HomePlayerIDWithSub == playerId || g.AwayPlayerIDWithSub == playerId)) is not Match match)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "You are not in a match this week.";
            });
            return;
        }

        Embed[] embeds = [(await match.GetDefaultEmbed()).Build()];
        await slashCommand.ModifyOriginalResponseAsync(async (mp) =>
        {
            mp.Embeds = embeds;
        });

        await dBContext.DisposeAsync();
    }
}
