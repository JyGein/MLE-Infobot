using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace MLE_Infobot.Commands;

/// <summary>
/// just for copy pasting to make more comamnds
/// </summary>
internal class FinishSeason : CommandBase
{
    const string COMMANDNAME = "finish-season";

    const string PUBLISHCHANNELOPTIONNAME = "publish-channel";

    public override async Task SubscribeCommand(DiscordSocketClient client)
    {
        client.SlashCommandExecuted += CommandExecuted;
    }
    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        await guild.CreateApplicationCommandAsync(new SlashCommandBuilder()
            .WithName(COMMANDNAME)
            .WithDescription($"Sets the current season to finished. {Messages.REQUIRESADMIN}")
            .AddOption(PUBLISHCHANNELOPTIONNAME, ApplicationCommandOptionType.Channel, "The channel to publish the congrats to the champion announcement into.", isRequired: true)
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
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "There is no current season.";
            });
            await dBContext.DisposeAsync();
            return;
        }

        if (!await dBContext.Entry(season).Collection(s => s.PlayoffWeeks).Query().AnyAsync())
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "No playoffs were played.";
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

        await dBContext.Entry(season).Collection(s => s.SeasonWeeks).Query().Include(w => w.Matches).LoadAsync();
        await dBContext.Entry(season).Collection(s => s.PlayoffWeeks).Query().Include(w => w.Matches).LoadAsync();
        if (season.AllWeeks.SelectMany(w => w.Matches).Any(m => m.Winner == Match.MatchState.Undecided))
        {
            Embed[] unfinishedMatchesEmbeds = [.. season.AllWeeks.SelectMany(w => w.Matches).Where(m => m.Winner == Match.MatchState.Undecided).Select(async m => (await m.GetDefaultEmbed()).Build()).Select(t => t.Result)];
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "The following matches must be submitted to finish the season.";
                mp.Embeds = unfinishedMatchesEmbeds;
            });
            await dBContext.DisposeAsync();
            return;
        }

        if (season.PlayoffWeeks.FirstOrDefault(w => w.Matches.Count == 1) is not Week finalsWeek)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "There is no final champion.";
            });
            await dBContext.DisposeAsync();
            return;
        }

        Match finalsMatch = finalsWeek.Matches.Single();
        if (finalsMatch.Winner == Match.MatchState.Tie)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "The finals match cannot be a tie.";
            });
            await dBContext.DisposeAsync();
            return;
        }

        await dBContext.Entry(finalsMatch).Reference(m => m.HomeSquad).LoadAsync();
        await dBContext.Entry(finalsMatch).Reference(m => m.AwaySquad).LoadAsync();
        Squad winningSquad = finalsMatch.WinningSquad!;
        await dBContext.Entry(winningSquad).Reference(s => s.Team).LoadAsync();

        season.State = Season.SeasonState.Finished;

        await dBContext.SaveChangesAsync();

        string message = $"Season {season.SeasonNumber} has finished!";
        Console.WriteLine(message);
        await slashCommand.ModifyOriginalResponseAsync((mp) =>
        {
            mp.Content = message;
        });
        (EmbedBuilder squadEmbedBuilder, FileAttachment teamLogo) = await winningSquad.GetDefaultEmbed();
        Embed[] embeds = [squadEmbedBuilder.Build()];
        await messageChannel.SendFilesAsync(
            [teamLogo],
            $"Congrats to {winningSquad.Team.TeamName} Squad {winningSquad.SquadNumber} for becoming your MLE Season {season.SeasonNumber} champions!",
            embeds: embeds
            );
        await dBContext.DisposeAsync();
    }
}
