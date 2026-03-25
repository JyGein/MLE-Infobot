using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MLE_Infobot.Commands;

internal class ViewSeason : CommandBase
{
    const string COMMANDNAME = "view-season";

    const string SEASONNUMBER = "season-number";

    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        client.SlashCommandExecuted += CommandExecuted;
        await guild.CreateApplicationCommandAsync(new SlashCommandBuilder()
            .WithName(COMMANDNAME)
            .WithDescription("Creates a new season. Will fail if you are not a league admin.")
            .AddOption(SEASONNUMBER, ApplicationCommandOptionType.Integer, "The number of weeks of the main season.", isRequired: true)
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
        if (await Program.LeagueDatabase.Seasons.AnyAsync(s => s.State == Season.SeasonState.Unpublished))
        {
            await slashCommand.RespondAsync("There is already an unpublished season!", ephemeral: true);
            return;
        }
        long numberOfWeeks = (long)slashCommand.Data.Options.First(o => o.Name == NUMBEROFWEEKSOPTIONNAME).Value;
        if (numberOfWeeks < 1)
        {
            await slashCommand.RespondAsync("You need a minimum of 1 week!", ephemeral: true);
            return;
        }
        await slashCommand.DeferAsync(ephemeral: true);
         
        Season season = new() { NumberOfSeasonWeeks = numberOfWeeks, SeasonNumber = (!await Program.LeagueDatabase.Seasons.AnyAsync()) ? 1 : (await Program.LeagueDatabase.Seasons.OrderBy(s => s.SeasonNumber).FirstAsync()).SeasonNumber + 1, State = Season.SeasonState.Unpublished };

        await Program.LeagueDatabase.AddAsync(season);

        await Program.LeagueDatabase.SaveChangesAsync();

        Console.WriteLine($"Season number {season.SeasonNumber} created with {numberOfWeeks}");
        await slashCommand.ModifyOriginalResponseAsync((mp) =>
        {
            mp.Content = $"Season number {season.SeasonNumber} sucessfully added to the league! Start adding squads with /add-squad.";
        });
    }
}
