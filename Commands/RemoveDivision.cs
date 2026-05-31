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
internal class RemoveDivision : CommandBase
{
    const string COMMANDNAME = "remove-division";

    const string DIVISIONNAMEOPTIONNAME = "division-name";

    public override async Task SubscribeCommand(DiscordSocketClient client)
    {
        client.SlashCommandExecuted += CommandExecuted;
    }
    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        await guild.CreateApplicationCommandAsync(new SlashCommandBuilder()
            .WithName(COMMANDNAME)
            .WithDescription($"Removes an unpublished division and it's squads. {Messages.REQUIRESADMIN}")
            .AddOption(DIVISIONNAMEOPTIONNAME, ApplicationCommandOptionType.String, "The name of the division to remove.", isRequired: true)
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
                mp.Content = "There is not an unpublished season.";
            });
            await dBContext.DisposeAsync();
            return;
        }

        string oldDivisionName = ((string)slashCommand.Data.Options.First(o => o.Name == DIVISIONNAMEOPTIONNAME)).Trim();
        //throw if no division goes by the old name
        if (season.Divisions.FirstOrDefault(d => d.DivisionName.ToLower() == oldDivisionName.ToLower()) is not Division division)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "That division does not exist!";
            });
            await dBContext.DisposeAsync();
            return;
        }

        foreach(Squad squad in await dBContext.Entry(division).Collection(d => d.Squads).Query().ToListAsync())
        {
            await RemoveSquad.DeleteSquad(squad);
        }
        dBContext.Entry(division).State = EntityState.Deleted;

        await dBContext.SaveChangesAsync();

        Console.WriteLine($"{division.DivisionName} Division was deleted.");
        Embed[] embeds = [(await season.GetDivisionsEmbed()).Build()];
        await slashCommand.ModifyOriginalResponseAsync(async (mp) =>
        {
            mp.Content = $"{division.DivisionName} Division was deleted.";
            mp.Embeds = embeds;
        });
        await dBContext.DisposeAsync();
    }
}
