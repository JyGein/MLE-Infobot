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
internal class EditSquadSubs : CommandBase
{
    const string COMMANDNAME = "edit-squad-subs";

    const string TEAMROLEOPTIONNAME = "team-role";
    const string SQUADNUMBEROPTIONNAME = "squad-number";
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
            .WithDescription($"Edits a squad's abc ranking. {Messages.REQUIRESADMIN}")
            .AddOption(TEAMROLEOPTIONNAME, ApplicationCommandOptionType.Role, "The discord role of the squad's team.", isRequired: true)
            .AddOption(SQUADNUMBEROPTIONNAME, ApplicationCommandOptionType.Integer, "The number of the squad.", isRequired: true)
            .AddOption(SUB1OPTIONNAME, ApplicationCommandOptionType.User, "The first substitute of this squad.")
            .AddOption(SUB2OPTIONNAME, ApplicationCommandOptionType.User, "The second substitute of this squad.")
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
                    mp.Content = $"There is no unpublished or current season!";
                });
                await dBContext.DisposeAsync();
                return;
            }
            season = currentSeason;
        }

        IRole teamRole = (IRole)slashCommand.Data.Options.First(o => o.Name == TEAMROLEOPTIONNAME).Value;
        if (await dBContext.Teams.FirstOrDefaultAsync(t => t.TeamRoleID == teamRole.Id) is not Team team)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "That role is not linked to a team.";
            });
            await dBContext.DisposeAsync();
            return;
        }

        long squadNumber = (long)slashCommand.Data.Options.First(o => o.Name == SQUADNUMBEROPTIONNAME).Value;
        if ((await dBContext.Entry(season).Collection(s => s.Divisions).Query().Include(d => d.Squads).ToListAsync()).SelectMany(d => d.Squads).FirstOrDefault(s => s.TeamId == team.TeamId && s.SquadNumber == squadNumber) is not Squad squad)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "That is not a valid squad number";
            });
            await dBContext.DisposeAsync();
            return;
        }

        string warnings = "";
        List<IUser> subs = [];
        foreach (SocketSlashCommandDataOption optionData in slashCommand.Data.Options.Where(o => o.Name == SUB1OPTIONNAME || o.Name == SUB2OPTIONNAME))
        {
            subs.Add((IUser)optionData.Value);
        }

        await dBContext.Entry(squad).Collection(s => s.PlayerIDs).LoadAsync();
        await dBContext.Entry(squad).Collection(s => s.SubstituteIDs).LoadAsync();
        squad.SubstituteIDs.ForEach(async subId => dBContext.Entry(subId).State = EntityState.Deleted);
        squad.SubstituteIDs.Clear();
        foreach (IUser sub in subs)
        {
            if (squad.PlayerIDs.Any(sp => sp == sub.Id) || squad.SubstituteIDs.Any(sp => sp == sub.Id))
            {
                warnings += $"{sub.GlobalName} is on this squad an additional time.\n";
            }
            await dBContext.UpdateUserEntry(sub);
            squad.SubstituteIDs.Add((sub.Id, squad));
        }

        foreach (IUser player in subs)
        {
            if (!Program.Guild.GetUser(player.Id).Roles.Any(r => r.Id == teamRole.Id))
            {
                warnings += $"{player.GlobalName} does not have the team's role.\n";
            }
        }

        await dBContext.SaveChangesAsync();

        //Console.WriteLine($"");
        (EmbedBuilder squadEmbed, FileAttachment teamLogo) = await squad.GetDefaultEmbed(true, withABCRank: true);
        Embed[] embeds = [squadEmbed.Build()];
        List<FileAttachment> teamLogos = [teamLogo];
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
            mp.Content = "Sucessfully changed squad substitutes.";
            mp.Embeds = embeds;
            mp.Attachments = teamLogos;
        });
        await dBContext.DisposeAsync();
    }
}
