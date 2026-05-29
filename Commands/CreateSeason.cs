using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MLE_Infobot.Commands;

internal class CreateSeason : CommandBase
{
    const string COMMANDNAME = "create-season";
    public override async Task SubscribeCommand(DiscordSocketClient client)
    {
        client.SlashCommandExecuted += CommandExecuted;
    }
    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        await guild.CreateApplicationCommandAsync(new SlashCommandBuilder()
            .WithName(COMMANDNAME)
            .WithDescription($"Creates a new season. {Messages.REQUIRESADMIN}")
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
        LeagueDBContext dBContext = new();
        if (await dBContext.Seasons.AnyAsync(s => s.State == Season.SeasonState.Unpublished))
        {
            await slashCommand.RespondAsync("There is already an unpublished season!", ephemeral: true);
            await dBContext.DisposeAsync();
            return;
        }
        await slashCommand.DeferAsync(ephemeral: true);
         
        Season season = new() { SeasonNumber = (!await dBContext.Seasons.AnyAsync()) ? 1 : (await dBContext.Seasons.OrderBy(s => s.SeasonNumber).FirstAsync()).SeasonNumber + 1, State = Season.SeasonState.Unpublished };

        await dBContext.AddAsync(season);

        await dBContext.SaveChangesAsync();
        await dBContext.DisposeAsync();

        Console.WriteLine($"Season number {season.SeasonNumber} created.");
        await slashCommand.ModifyOriginalResponseAsync((mp) =>
        {
            mp.Content = $"Season number {season.SeasonNumber} sucessfully added to the league! Start adding squads with /add-squad.";
        });
    }
}
