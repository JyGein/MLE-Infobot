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
internal class EditSquadABC : CommandBase
{
    const string COMMANDNAME = "edit-squad-abc";

    const string TEAMROLEOPTIONNAME = "team-role";
    const string SQUADNUMBEROPTIONNAME = "squad-number";
    const string ABCOPTIONNAME = "abc-ranking";

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
            .AddOption(ABCOPTIONNAME, ApplicationCommandOptionType.String, "A, B, or C.", isRequired: true)
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

        Squad.ABCRanking abcRanking = Squad.ABCRanking.B;
        string abcRankingInput = ((string)slashCommand.Data.Options.First(o => o.Name == ABCOPTIONNAME).Value).Trim().ToLower();
        switch (abcRankingInput)
        {
            case "a":
                abcRanking = Squad.ABCRanking.A;
                break;
            case "b":
                abcRanking = Squad.ABCRanking.B;
                break;
            case "c":
                abcRanking = Squad.ABCRanking.C;
                break;
            default:
                await slashCommand.ModifyOriginalResponseAsync((mp) =>
                {
                    mp.Content = "That is not a valid ABC ranking.";
                });
                await dBContext.DisposeAsync();
                return;
        }

        squad.ABCRank = abcRanking;

        await dBContext.SaveChangesAsync();

        //Console.WriteLine($"");
        (EmbedBuilder squadEmbed, FileAttachment teamLogo) = await squad.GetDefaultEmbed(true, withABCRank: true);
        Embed[] embeds = [squadEmbed.Build()];
        List<FileAttachment> teamLogos = [teamLogo];
        await slashCommand.ModifyOriginalResponseAsync((mp) =>
        {
            mp.Content = "Sucessfully changed squad ranking.";
            mp.Embeds = embeds;
            mp.Attachments = teamLogos;
        });
        await dBContext.DisposeAsync();
    }
}
