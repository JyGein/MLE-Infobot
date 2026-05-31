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
using System.Xml.Linq;

namespace MLE_Infobot.Commands;

internal class AddSquad : CommandBase
{
    const string COMMANDNAME = "add-squad";

    const string DIVISIONOPTIONNAME = "division";
    const string TEAMROLEOPTIONNAME = "team-role";
    const string PLAYER1OPTIONNAME = "player1";
    const string PLAYER2OPTIONNAME = "player2";
    const string PLAYER3OPTIONNAME = "player3";
    const string SUB1OPTIONNAME = "sub1";
    const string SUB2OPTIONNAME = "sub2";

    public override async Task SubscribeCommand(DiscordSocketClient client)
    {
        client.SlashCommandExecuted += CommandExecuted;
    }
    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        await guild.CreateApplicationCommandAsync(new SlashCommandBuilder()
            .WithName(COMMANDNAME)
            .WithDescription($"Adds a new squad to the next season. {Messages.REQUIRESADMIN}")
            .AddOption(DIVISIONOPTIONNAME, ApplicationCommandOptionType.String, "The name of the division to add the squad to.", isRequired: true)
            .AddOption(TEAMROLEOPTIONNAME, ApplicationCommandOptionType.Role, "The discord role of the squad's team.", isRequired: true)
            .AddOption(PLAYER1OPTIONNAME, ApplicationCommandOptionType.User, "The first player of this squad.", isRequired: true)
            .AddOption(PLAYER2OPTIONNAME, ApplicationCommandOptionType.User, "The second player of this squad.", isRequired: true)
            .AddOption(PLAYER3OPTIONNAME, ApplicationCommandOptionType.User, "The third player of this squad.", isRequired: true)
            .AddOption(SUB1OPTIONNAME, ApplicationCommandOptionType.User, "The first substitute of this squad.")
            .AddOption(SUB2OPTIONNAME, ApplicationCommandOptionType.User, "The second substitute of this squad.")
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
        await slashCommand.DeferAsync(ephemeral: true);
        LeagueDBContext dBContext = new();
        if (!await dBContext.Seasons.AnyAsync(s => s.State == Season.SeasonState.Unpublished))
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "There isn't an unpublished season!";
            });
            await dBContext.DisposeAsync();
            return;
        }

        Season season = await dBContext.Seasons
            .Include(s => s.Divisions)
            .ThenInclude(d => d.Squads)
            .ThenInclude(s => s.Team)
            .FirstAsync(s => s.State == Season.SeasonState.Unpublished);
        string divisionName = ((string)slashCommand.Data.Options.First(o => o.Name == DIVISIONOPTIONNAME)).Trim();
        if (season.Divisions.FirstOrDefault(d => d.DivisionName.Equals(divisionName, StringComparison.OrdinalIgnoreCase)) is not Division division)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "That is not a valid Division name!";
            });
            await dBContext.DisposeAsync();
            return;
        }

        IRole teamRole = (IRole)slashCommand.Data.Options.First(o => o.Name == TEAMROLEOPTIONNAME).Value;
        if (await dBContext.Teams.FirstOrDefaultAsync(team => team.TeamRoleID == teamRole.Id) is not Team team)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "That role is not linked to a team!";
            });
            await dBContext.DisposeAsync();
            return;
        }
        IUser player1 = (IUser)slashCommand.Data.Options.First(o => o.Name == PLAYER1OPTIONNAME).Value;
        IUser player2 = (IUser)slashCommand.Data.Options.First(o => o.Name == PLAYER2OPTIONNAME).Value;
        IUser player3 = (IUser)slashCommand.Data.Options.First(o => o.Name == PLAYER3OPTIONNAME).Value;
        List<IUser> players = [player1, player2, player3];
        List<IUser> subs = [];
        foreach (SocketSlashCommandDataOption optionData in slashCommand.Data.Options.Where(o => o.Name == SUB1OPTIONNAME || o.Name == SUB2OPTIONNAME))
        {
            subs.Add((IUser)optionData.Value);
        }
        string warnings = "";

        int squadNumber = season.Squads.Count(sq => sq.Team == team) + 1;
        Squad squad = new() { Division = division, SquadNumber = squadNumber, Team = team };
        foreach (IUser player in players)
        {
            if (squad.PlayerIDs.Any(sp => sp == player.Id))
            {
                warnings += $"{player.GlobalName} is on this squad an additional time.\n";
            }
            await dBContext.UpdateUserEntry(player);
            squad.PlayerIDs.Add((player.Id, squad));
        }
        foreach (IUser sub in subs)
        {
            if (squad.PlayerIDs.Any(sp => sp == sub.Id) || squad.SubstituteIDs.Any(sp => sp == sub.Id))
            {
                warnings += $"{sub.GlobalName} is on this squad an additional time.\n";
            }
            await dBContext.UpdateUserEntry(sub);
            squad.SubstituteIDs.Add((sub.Id, squad));
        }
        foreach (IUser player in players.Concat(subs))
        {
            if (!Program.Guild.GetUser(player.Id).Roles.Any(r => r.Id == teamRole.Id))
            {
                warnings += $"{player.GlobalName} does not have the team's role.\n";
            }
        }

        foreach (Squad anotherSquad in season.Squads)
        {
            foreach (IUser player in players)
            {
                if (anotherSquad.PlayerIDs.Any(sp => sp == player.Id))
                {
                    warnings += $"{player.GlobalName} is already on {anotherSquad.Team.TeamName} - Squad {anotherSquad.SquadNumber} as a player.\n";
                }
                if (anotherSquad.SubstituteIDs.Any(sp => sp == player.Id))
                {
                    warnings += $"{player.GlobalName} is already on {anotherSquad.Team.TeamName} - Squad {anotherSquad.SquadNumber} as a substitute.\n";
                }
            }
            foreach (IUser player in subs)
            {
                if (anotherSquad.PlayerIDs.Any(sp => sp == player.Id))
                {
                    warnings += $"{player.GlobalName} is already on {anotherSquad.Team.TeamName} - Squad {anotherSquad.SquadNumber} as a player.\n";
                }
            }
        }

        foreach (Squad divisionSquads in division.Squads)
        {
            if (divisionSquads.TeamId == squad.TeamId)
            {
                warnings += $"Squad {divisionSquads.SquadNumber} is from the same team as Squad {squad.SquadNumber} in {division.DivisionName} division.";
            }
        }

        division.Squads.Add(squad);
        await dBContext.SaveChangesAsync();
        dBContext.Entry(season).State = EntityState.Detached;
        dBContext.Entry(squad).State = EntityState.Detached;
        await dBContext.DisposeAsync();
        await season.RandomizeGuaranteedMatches();

        Console.WriteLine($"Squad number {squadNumber} created with {player1.Id}, {player2.Id}, and {player3.Id} as players and {string.Join(", ", subs.Select(s => s.Id.ToString()))} as sub(s).");
        (EmbedBuilder embedBuilder, FileAttachment teamLogo) = await squad.GetDefaultEmbed();
        Embed[] embeds = [embedBuilder.Build()];
        List<FileAttachment> fileAttachments = [teamLogo];
        if (warnings != "")
        {
            warnings = warnings.Trim();
            embeds =
            [
                .. embeds,
                new EmbedBuilder()
                    .WithColor(Discord.Color.LightOrange)
                    .WithTitle("Warnings:")
                    .WithDescription(warnings)
                    .Build(),
            ];
        }
        await slashCommand.ModifyOriginalResponseAsync((mp) =>
        {
            mp.Content = $"Squad number {squadNumber} created!";
            mp.Embeds = embeds;
            mp.Attachments = fileAttachments;
        });
    }
}
