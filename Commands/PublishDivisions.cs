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
internal class PublishDivisions : CommandBase
{
    const string COMMANDNAME = "publish-divisions";

    const string PUBLISHCHANNELOPTIONNAME = "publish-channel";

    public override async Task SubscribeCommand(DiscordSocketClient client)
    {
        client.SlashCommandExecuted += CommandExecuted;
    }
    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        await guild.CreateApplicationCommandAsync(new SlashCommandBuilder()
            .WithName(COMMANDNAME)
            .WithDescription($"Publicly shows all divisions. {Messages.REQUIRESADMIN}")
            .AddOption(PUBLISHCHANNELOPTIONNAME, ApplicationCommandOptionType.Channel, "The channel to post them to.", isRequired: true)
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
        
        if (await dBContext.Seasons.FirstOrDefaultAsync(s => s.State == Season.SeasonState.Unpublished) is not Season season)
        {
            if (await dBContext.Seasons.FirstOrDefaultAsync(s => s.State == Season.SeasonState.Started) is not Season currentSeason)
            {
                await slashCommand.ModifyOriginalResponseAsync((mp) =>
                {
                    mp.Content = "There is no unpublished or current season.";
                });
                await dBContext.DisposeAsync();
                return;
            }
            season = currentSeason;
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

        foreach (Division division in await dBContext.Entry(season).Collection(s => s.Divisions).Query().ToListAsync())
        {
            Embed[] embeds = [];
            List<FileAttachment> teamLogos = [];
            if (division != null)
            {
                (List<EmbedBuilder> divisionEmbeds, List<FileAttachment> divisionTeamLogos) = await division.GetSquadsEmbeds();
                embeds = [.. embeds.Concat(divisionEmbeds.Select(eb => eb.Build()))];
                teamLogos = [.. teamLogos.Concat(divisionTeamLogos)];
            }
            else
            {
                embeds = [.. embeds.Append((await season.GetDivisionsEmbed()).Build())];
            }

            if (teamLogos.Count > 0)
            {
                await messageChannel.SendFilesAsync(teamLogos, embeds: embeds);
            }
            else
            {
                await messageChannel.SendMessageAsync(embeds: embeds);
            }
        }

        await slashCommand.ModifyOriginalResponseAsync((mp) =>
        {
            mp.Content = "Divisions Published!";
        });
        await dBContext.DisposeAsync();
    }
}
