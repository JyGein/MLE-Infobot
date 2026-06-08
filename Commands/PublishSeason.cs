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
internal class PublishSeason : CommandBase
{
    const string COMMANDNAME = "publish-season";

    const string PUBLISHCHANNELOPTIONNAME = "publish-channel";

    public override async Task SubscribeCommand(DiscordSocketClient client)
    {
        client.SlashCommandExecuted += CommandExecuted;
    }
    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        await guild.CreateApplicationCommandAsync(new SlashCommandBuilder()
            .WithName(COMMANDNAME)
            .WithDescription($"Publish the next season. {Messages.REQUIRESADMIN}")
            .AddOption(PUBLISHCHANNELOPTIONNAME, ApplicationCommandOptionType.Channel, "The channel to publish the new season announcement into.", isRequired: true)
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

        if (await dBContext.Seasons.AnyAsync(s => s.State == Season.SeasonState.Started))
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "You must finish the previous season.";
            });
            await dBContext.DisposeAsync();
            return;
        }

        if (await dBContext.Seasons.Include(s => s.SeasonWeeks).FirstOrDefaultAsync(s => s.State == Season.SeasonState.Unpublished) is not Season season)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "There is no unpublished season to publish.";
            });
            await dBContext.DisposeAsync();
            return;
        }

        if (season.SeasonWeeks.FirstOrDefault(w => w.WeekNumber == 1 && w.HasBeenGenerated) is not Week week)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "The first week must be generated to publish.";
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

        season.State = Season.SeasonState.Started;

        week.State = Week.WeekState.Current;

        await dBContext.SaveChangesAsync();

        Console.WriteLine($"Published season {season.SeasonNumber}.");
        Embed[] embeds = [.. (await week.GetEmbed()).Select(eb => eb.Build())];
        await messageChannel.SendMessageAsync(text: $"Season {season.SeasonNumber} Week {week.WeekNumber} has begun!", embeds: embeds);
        await slashCommand.ModifyOriginalResponseAsync((mp) =>
        {
            mp.Content = $"Published season {season.SeasonNumber} into channel <#{publishChannel.Id}>.";
        });
        await dBContext.DisposeAsync();
    }
}
