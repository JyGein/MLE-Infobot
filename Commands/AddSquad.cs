using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        client.SlashCommandExecuted += CommandExecuted;
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
        LeagueDBContext dBContext = new();
        if (!await dBContext.Seasons.AnyAsync(s => s.State == Season.SeasonState.Unpublished))
        {
            await slashCommand.RespondAsync("There isn't an unpublished season!", ephemeral: true);
            return;
        }
        await slashCommand.DeferAsync(ephemeral: true);

        Season season = await dBContext.Seasons.FirstAsync(s => s.State == Season.SeasonState.Unpublished);
        string divisionName = (string)slashCommand.Data.Options.First(o => o.Name == DIVISIONOPTIONNAME);
        if (season.Divisions.FirstOrDefault(d => d.DivisionName.Equals(divisionName.Trim(), StringComparison.CurrentCultureIgnoreCase)) is not Division division)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "That is not a valid Division name!";
            });
            return;
        }

        IRole teamRole = (IRole)slashCommand.Data.Options.First(o => o.Name == TEAMROLEOPTIONNAME).Value;
        if (dBContext.Teams.FirstOrDefault(team => team.TeamRoleID == teamRole.Id) is not Team team)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "That role is not linked to a team!";
            });
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
            if (squad.PlayerIDs.Contains(player.Id))
            {
                warnings += $"{player.Username} is on this squad an additional time.\n";
            }
            squad.PlayerIDs.Add(player.Id);
        }
        foreach (IUser sub in subs)
        {
            if (squad.PlayerIDs.Contains(sub.Id) || squad.SubstituteIDs.Contains(sub.Id))
            {
                warnings += $"{sub.Username} is on this squad an additional time.\n";
            }
            squad.SubstituteIDs.Add(sub.Id);
        }
        foreach (IUser player in players.Concat(subs))
        {
            if (!Program.Guild.GetUser(player.Id).Roles.Any(r => r.Id == teamRole.Id))
            {
                warnings += $"{player.Username} does not have the team's role.\n";
            }
        }

        foreach (Squad anotherSquad in season.Squads)
        {
            foreach (IUser player in players)
            {
                if (anotherSquad.PlayerIDs.Contains(player.Id))
                {
                    warnings += $"{player.Username} is already on {anotherSquad.Team.TeamName} - Squad {anotherSquad.SquadNumber} as a player.\n";
                }
                if (anotherSquad.SubstituteIDs.Contains(player.Id))
                {
                    warnings += $"{player.Username} is already on {anotherSquad.Team.TeamName} - Squad {anotherSquad.SquadNumber} as a substitute.\n";
                }
            }
            foreach (IUser player in subs)
            {
                if (anotherSquad.PlayerIDs.Contains(player.Id))
                {
                    warnings += $"{player.Username} is already on {anotherSquad.Team.TeamName} - Squad {anotherSquad.SquadNumber} as a player.\n";
                }
            }
        }

        season.Squads.Add(squad);
        await season.RandomizeMatches();
        await dBContext.SaveChangesAsync();
        await dBContext.DisposeAsync();

        Console.WriteLine($"Squad number {squadNumber} created with {player1.Id}, {player2.Id}, and {player3.Id} as players and {string.Join(", ", subs.Select(s => s.Id.ToString()))} as sub(s).");
        Embed[] embeds = [(await squad.GetDefaultEmbed()).Build()];
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
        });
    }
}
