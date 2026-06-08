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
internal class GenerateSeasonWeeks : CommandBase
{
    const string COMMANDNAME = "generate-season-weeks";

    public override async Task SubscribeCommand(DiscordSocketClient client)
    {
        client.SlashCommandExecuted += CommandExecuted;
    }
    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        await guild.CreateApplicationCommandAsync(new SlashCommandBuilder()
            .WithName(COMMANDNAME)
            .WithDescription($"Generates all the guaranteed weeks. {Messages.REQUIRESADMIN}")
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
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = $"There is no unpublished season!";
            });
            await dBContext.DisposeAsync();
            return;
        }

        try
        {
            await season.RandomizeGuaranteedMatches();
        }
        catch
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = $"Something went wrong generating the season!";
            });
            await dBContext.DisposeAsync();
            throw;
        }

        await dBContext.SaveChangesAsync();

        //Console.WriteLine($"");
        Week week = await dBContext.Entry(season).Collection(s => s.SeasonWeeks).Query().FirstAsync();
        await slashCommand.ModifyOriginalResponseAsync(async (mp) =>
        {
            mp.Content = "Randomized!";
            await ViewSeason.ViewSeasonPage(mp, week, true);
        });
        await dBContext.DisposeAsync();
    }
}
