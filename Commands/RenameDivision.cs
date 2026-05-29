using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using MLE_Infobot.Migrations;
using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace MLE_Infobot.Commands;

internal class RenameDivision : CommandBase
{
    const string COMMANDNAME = "rename-division";

    const string OLDDIVISIONOPTIONNAME = "old-division-name";
    const string NEWDIVISIONOPTIONNAME = "new-division-name";
    const string SEASONNUMBEROPTIONNAME = "season-number";

    public override async Task SubscribeCommand(DiscordSocketClient client)
    {
        client.SlashCommandExecuted += CommandExecuted;
    }
    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        await guild.CreateApplicationCommandAsync(new SlashCommandBuilder()
            .WithName(COMMANDNAME)
            .WithDescription($"Renames a division. {Messages.REQUIRESADMIN}")
            .AddOption(OLDDIVISIONOPTIONNAME, ApplicationCommandOptionType.String, "The old name of the division.", isRequired: true)
            .AddOption(NEWDIVISIONOPTIONNAME, ApplicationCommandOptionType.String, "The new name of the division.", isRequired: true)
            .AddOption(SEASONNUMBEROPTIONNAME, ApplicationCommandOptionType.Integer, "The number of season the division is in. Defaults to next season.")
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
        if (await GetSeasonOrDefault(slashCommand, dBContext) is not Season season)
        {
            await dBContext.DisposeAsync();
            return;
        }
        if (!await dBContext.Seasons.AnyAsync(s => s.State == Season.SeasonState.Unpublished))
        {
            await slashCommand.RespondAsync("There isn't an unpublished season!", ephemeral: true);
            await dBContext.DisposeAsync();
            return;
        }
        await slashCommand.DeferAsync(ephemeral: true);

        string oldDivisionName = ((string)slashCommand.Data.Options.First(o => o.Name == OLDDIVISIONOPTIONNAME)).Trim();
        //throw if no division goes by the old name
        if (season.Divisions.FirstOrDefault(d => d.DivisionName.ToLower().Equals(oldDivisionName.ToLower())) is not Division existingDivision)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "That division does not exist!";
            });
            await dBContext.DisposeAsync();
            return;
        }
        string newDivisionName = ((string)slashCommand.Data.Options.First(o => o.Name == NEWDIVISIONOPTIONNAME)).Trim();
        //throw if a division already goes by the new name
        if (season.Divisions.FirstOrDefault(d => d.DivisionName.ToLower().Equals(newDivisionName.ToLower())) is { })
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "That division name already exists!";
            });
            await dBContext.DisposeAsync();
            return;
        }

        //rename the division
        existingDivision.DivisionName = newDivisionName;
        await dBContext.SaveChangesAsync();
        await dBContext.DisposeAsync();

        Console.WriteLine($"Division {oldDivisionName} renamed to {newDivisionName}.");

        await slashCommand.ModifyOriginalResponseAsync((mp) =>
        {
            mp.Content = $"Division {oldDivisionName} renamed to {newDivisionName}.";
        });
    }

    private async static Task<Season?> GetSeasonOrDefault(SocketSlashCommand slashCommand, LeagueDBContext dBContext)
    {
        IIncludableQueryable<Season, List<Division>> Seasons = dBContext.Seasons.Include(s => s.Divisions);
        if (slashCommand.Data.Options.FirstOrDefault(o => o.Name == SEASONNUMBEROPTIONNAME) is SocketSlashCommandDataOption seasonNumberOption)
        {
            long seasonNumber = (long)seasonNumberOption.Value;
            if (await Seasons.FirstOrDefaultAsync(s => s.SeasonNumber == seasonNumber) is Season season)
            {
                return season;
            }
            else
            {
                await slashCommand.RespondAsync("That season number does not exist!", ephemeral: true);
                return null;
            }
        }
        else if (await Seasons.FirstOrDefaultAsync(s => s.State == Season.SeasonState.Unpublished) is Season season)
        {
            return season;
        }
        else
        {
            await slashCommand.RespondAsync("There isn't an unpublished season!", ephemeral: true);
            return null;
        }
    }
}
