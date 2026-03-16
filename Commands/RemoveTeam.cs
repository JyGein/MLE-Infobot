using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MLE_Infobot.Commands;

internal partial class RemoveTeam : CommandBase
{
    const string COMMANDNAME = "remove-team";

    const string TEAMROLEOPTIONNAME = "team-role";

    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        client.SlashCommandExecuted += CommandExecuted;
        client.ButtonExecuted += ButtonClicked;
        await guild.CreateApplicationCommandAsync(new SlashCommandBuilder()
            .WithName(COMMANDNAME)
            .WithDescription("Unlinks the team from their role. Will fail if you are not a league admin.")
            .AddOption(TEAMROLEOPTIONNAME, ApplicationCommandOptionType.Role, "The discord role of the team.", isRequired: true)
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

        IRole teamRole = (IRole)slashCommand.Data.Options.First(o => o.Name == TEAMROLEOPTIONNAME).Value;
        if (Program.LeagueDatabase.Teams.FirstOrDefault(team => team.TeamRoleID == teamRole.Id) is not Team team)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "That role is not linked to a team!";
            });
            return;
        }

        IUserMessage confirmationMessage = await slashCommand.Channel.SendMessageAsync(
            text: "Careful! This is a dangerous command!\n" +
                "If the team is not in a published season the team will just removed and the associated role will be freed.\n" +
                "Otherwise, if the team is in a publish season the team will auto-lose all of their matches and will no longer be accessable through commands in order to unlink the role from the team.\n" +
                "Are you sure you want to do this?",
            components: new ComponentBuilder()
            .WithButton("Yes", COMMANDNAME + ":yes:" + slashCommand.Id, ButtonStyle.Success)
            .WithButton("No", COMMANDNAME + ":no:" + slashCommand.Id, ButtonStyle.Danger)
            .Build()
            );
        InteractionCache[slashCommand.Id] = (slashCommand, confirmationMessage, team);
        await slashCommand.DeleteOriginalResponseAsync();
        new Task(async () => {
            await Task.Delay(TimeSpan.FromMinutes(10));
            if (InteractionCache.TryGetValue(slashCommand.Id, out _))
            {
                InteractionCache.Remove(slashCommand.Id);
                await confirmationMessage.ModifyAsync(mp => { mp.Components = null; mp.Content = confirmationMessage.Content + "\n[Timed out]"; });
            }
        }).Start();
    }

    /// <summary>
    /// Id, (Slash Command, Confirmation Message, Team to Remove)
    /// </summary>
    internal static Dictionary<ulong, (SocketSlashCommand, IUserMessage, Team)> InteractionCache = []; 

    internal async Task ButtonClicked(SocketMessageComponent messageComponent)
    {
        if (!messageComponent.Data.CustomId.Contains(COMMANDNAME)) return;
        System.Text.RegularExpressions.Match m = ComponentIdPattern().Match(messageComponent.Data.CustomId);
        string confirmationKey = m.Groups[1].Value;
        ulong interactionKey = ulong.Parse(m.Groups[2].Value);
        if (!InteractionCache.TryGetValue(interactionKey, out (SocketSlashCommand, IUserMessage, Team) interactionInfo))
        {
            await messageComponent.RespondAsync("This interaction has expired!", ephemeral: true);
            return;
        }
        SocketSlashCommand slashCommand = interactionInfo.Item1;
        IUserMessage confirmationMessage = interactionInfo.Item2;
        Team team = interactionInfo.Item3;
        if (slashCommand.User.Id != messageComponent.User.Id)
        {
            await messageComponent.RespondAsync("This is not your interaction!", ephemeral: true);
            return;
        }

        if (confirmationKey == "no")
        {
            await messageComponent.RespondAsync("Did not unlink the team.");
            await confirmationMessage.ModifyAsync(mp => { mp.Components = null; mp.Content = confirmationMessage.Content + "\n[Cancelled]"; });
            return;
        }

        await messageComponent.RespondAsync("Removing team...");

        //Im defo not using and SQL database correctly with this, it's just the best that i can right now with my knowledge, feel free to improve
        //Checks if any match in any published season has a squad from the team, if not it'll just remove the team from the database and any unpublished seasons, otherwise it'll auto-lose matches for that team in the current season and do a regular role unlink.
        bool fullyRemovedTeamFlag = false;
        bool randomizedUnpublishedSeasonMatches = false;
        if (await Program.LeagueDatabase.Seasons.AnyAsync(s => s.State != Season.SeasonState.Unpublished && s.SeasonWeeks.Cast<Week>().Concat(s.PlayoffWeeks).Any(w => w.Matches.Any(m => m.HomeSquad.Team == team || m.AwaySquad.Team == team))))
        {
            if (await Program.LeagueDatabase.Seasons.FirstOrDefaultAsync(season => season.State == Season.SeasonState.Started) is Season season)
            {
                foreach (Week week in season.SeasonWeeks.Cast<Week>().Concat(season.PlayoffWeeks))
                {
                    foreach (Match match in week.Matches.Where(m => m.HomeSquad.Team == team || m.AwaySquad.Team == team))
                    {
                        if (match.Winner == Match.MatchState.Undecided) match.Winner = match.HomeSquad.Team == team ? Match.MatchState.Away : Match.MatchState.Home;
                    }
                }
            }
            team.TeamRoleID = 0;
            team.Unlinked = true;
        }
        else
        {
            fullyRemovedTeamFlag = true;
            if (await Program.LeagueDatabase.Seasons.FirstOrDefaultAsync(season => season.State == Season.SeasonState.Unpublished) is Season season)
            {
                randomizedUnpublishedSeasonMatches = true;
                season.Squads.RemoveAll(s => s.Team == team);
                await season.RandomizeMatches();
            }
            Program.LeagueDatabase.Remove(team);
        }
        
        await Program.LeagueDatabase.SaveChangesAsync();

        InteractionCache.Remove(interactionKey);
        await messageComponent.ModifyOriginalResponseAsync(mp => { mp.Content = fullyRemovedTeamFlag ? $"Team {team.TeamName} was successfully deleted from the league." + (randomizedUnpublishedSeasonMatches ? " Randomization of the unpublished season was required and executed." : "") : $"Team {team.TeamName} was successfully unlinked from it's role."; });
        await confirmationMessage.ModifyAsync(mp => { mp.Components = null; mp.Content = confirmationMessage.Content + "\n[Confirmed]"; });
    }
}
