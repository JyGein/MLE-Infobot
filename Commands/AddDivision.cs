using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace MLE_Infobot.Commands;

internal class AddDivision : CommandBase
{
    const string COMMANDNAME = "add-division";

    const string DIVISIONOPTIONNAME = "division";

    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        client.SlashCommandExecuted += CommandExecuted;
        await guild.CreateApplicationCommandAsync(new SlashCommandBuilder()
            .WithName(COMMANDNAME)
            .WithDescription($"Adds a new division to the next season. {Messages.REQUIRESADMIN}")
            .AddOption(DIVISIONOPTIONNAME, ApplicationCommandOptionType.String, "The name of the division to make.", isRequired: true)
            .Build());
    }

    internal async Task CommandExecuted(SocketSlashCommand slashCommand)
    {
        if (slashCommand.Data.Name != COMMANDNAME) return;
        if (!IsAdmin(slashCommand))
        {
            await slashCommand.RespondAsync(Messages.REQUIRESADMIN, ephemeral: true);
            return;
        }
        LeagueDBContext dBContext = new();
        if (!await dBContext.Seasons.AnyAsync(s => s.State == Season.SeasonState.Unpublished))
        {
            await slashCommand.RespondAsync("There isn't an unpublished season!", ephemeral: true);
            return;
        }
        await slashCommand.DeferAsync(ephemeral: true);

        Season season = await dBContext.Seasons.FirstAsync(s => s.State == Season.SeasonState.Unpublished);
        string divisionName = ((string)slashCommand.Data.Options.First(o => o.Name == DIVISIONOPTIONNAME)).Trim();
        if (season.Divisions.FirstOrDefault(d => d.DivisionName.Equals(divisionName, StringComparison.CurrentCultureIgnoreCase)) is { })
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "That division name already exists!";
            });
            return;
        }

        Division division = new() { DivisionName = divisionName, Season = season };
        season.Divisions.Add(division);
        await dBContext.SaveChangesAsync();
        await dBContext.DisposeAsync();

        Console.WriteLine($"Division {divisionName} created.");

        await slashCommand.ModifyOriginalResponseAsync((mp) =>
        {
            mp.Content = $"Division {divisionName} created.";
        });
    }
}
