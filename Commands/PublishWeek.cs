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
internal class PublishWeek : CommandBase
{
    const string COMMANDNAME = "publish-week";

    const string PUBLISHCHANNELOPTIONNAME = "publish-channel";

    public override async Task SubscribeCommand(DiscordSocketClient client)
    {
        client.SlashCommandExecuted += CommandExecuted;
    }
    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        await guild.CreateApplicationCommandAsync(new SlashCommandBuilder()
            .WithName(COMMANDNAME)
            .WithDescription($"Publish the next week. {Messages.REQUIRESADMIN}")
            .AddOption(PUBLISHCHANNELOPTIONNAME, ApplicationCommandOptionType.Channel, "The channel to publish the new week announcement into.", isRequired: true)
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

        if (await dBContext.Seasons.Include(s => s.SeasonWeeks).Include(s => s.PlayoffWeeks).FirstOrDefaultAsync(s => s.State == Season.SeasonState.Started) is not Season season)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "There is no current season.";
            });
            await dBContext.DisposeAsync();
            return;
        }

        if (season.AllWeeks.OrderBy(w => w.WeekNumber).FirstOrDefault(w => w.State == Week.WeekState.Unpublished && w.HasBeenGenerated) is not Week week)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "The next week must be generated to publish.";
            });
            await dBContext.DisposeAsync();
            return;
        }

        IGuildChannel publishChannel = (IGuildChannel)slashCommand.Data.Options.First(o => o.Name == PUBLISHCHANNELOPTIONNAME).Value;
        if (publishChannel is not IMessageChannel messageChannel)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "The channel must be a text channel.";
            });
            await dBContext.DisposeAsync();
            return;
        }

        if (season.AllWeeks.FirstOrDefault(w => w.State == Week.WeekState.Current) is Week oldCurrentWeek)
        {
            oldCurrentWeek.State = Week.WeekState.Finished;
        }
        else
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "Somehow there season has started and there is an unpublished week that has been generated but there is no current week. Please contact <@427955167233572864> to resolve."; //user id is me, JyGein :3
            });
            await dBContext.DisposeAsync();
            return;
        }

        week.State = Week.WeekState.Current;

        await dBContext.SaveChangesAsync();

        Console.WriteLine($"Published week {week.WeekNumber} in season {season.SeasonNumber}.");
        Embed[] embeds = [.. (await week.GetEmbed()).Select(eb => eb.Build())];
        await messageChannel.SendMessageAsync(text: $"Season {season.SeasonNumber} Week {week.WeekNumber} has begun!", embeds: embeds);
        await slashCommand.ModifyOriginalResponseAsync((mp) =>
        {
            mp.Content = $"Published season {season.SeasonNumber} Week {week.WeekNumber} into channel <#{publishChannel.Id}>.";
        });
        await dBContext.DisposeAsync();
    }
}
