# General commands - Anyone can run them to get information about the league

**my-match**  
Privately tells user who they are playing this week.  
Throws if the user is not on a squad.

**player-match <player: discord user>**
Privately tells the user who that player is playing this week.  
Throws if player is not playing this week.

**squad-matches <team-name: discord role> <squad-number: int>**  
Privately tells the user who that squad is playing this week.  
Throws if squad does not exist.

**team-matches <team-name: discord role>**  
Privately tells the user who that team is playing this week.  
Throws if team does not exist.

**team-season [team-name: discord role]**  
Privately tells the user (their team/inputted team)'s season matchups.  
Throws if (user is not on a team/role is not connected to a team).

### FUTURE:
Commands that tell the user various game records.
Commands to show previous weeks/seasons.

# Team Captain commands - require TEAM_CAPTAIN_ROLE

-- None for now!

# Admin commands - require ADMIN_ROLE or guild administator privledges.

## Team management:
**add-team <new-team-name: discord role> <team-color: hex code> <team-logo: image link**  
Will add a team to the database which can have squads added to it.

**edit-team-name <team-name: discord role> <team-name: string**  
Edit's an existing team's name.  
Throws if role is not attached to a team.

**edit-team-color <team-name: discord role> <team-color: hex code**  
Edit's an existing team's color.  
Throws if role is not attached to a team.

**edit-team-logo <team-name: discord role> <team-logo: image link**  
Edit's an existing team's logo.  
Throws if role is not attached to a team.

**add-squad <team-name: discord role> <player1: discord user> <player2: discord user> <player3: discord user> [sub1: discord user] [sub2: discord user].**  
Adds a squad to a team with the listed players.

**edit-squad <team-name: discord role> <squad-number: int> <player1: discord user> <player2: discord user> <player3: discord user> [sub1: discord user] [sub2: discord user]**  
Changes the players on a team's squad.

**subsitute <team-name: discord role> <squad-number: int> <player: discord user> <sub: discord user**  
Substitutes the player with the sub for the week.  
Throws if the sub or player are not in that squad and team.

**remove-squad <team-name: discord role> <squad-number: int**  
Removes the squad from the team.  
Dangerous; public confirmation message.  
Throws if squad or team dont exist.  
Note: Unsure what to do if the season has already started.

**remove-team <team-name: discord role**  
Removes the team from the league.  
Dangerous; public confirmation message.  
Throws if team doesn't exist.  
Note: Unsure what to do if the season has already started.

## League management:
**create-season <number-of-weeks: int>**.  
Randomly pairs squads against other squads from different teams and shows the pairings and season number.  
Backup: if there are not enough squads for the number of weeks will pair squads against already played squads instead of squads from the same team.  
Throws if there is currently an unpublished season.

**view-season [season-number: int]**  
Shows all of the matches for the season. Defaults to current season.  
Admin: Requires admin perms to display a season not published.  
Note: Entire command will be admin-only for now because it will take a good amount of extra effort to display it nicely.  
Throws if season does not exist.

**swap-matchup <season-number: int> <week-number: int> <team1: discord role> <squad1: int> <team2: discord role> <squad2: int>**  
Swaps two squad's matchup for that season and week.  
Throws if season/week/team/squad doesn't exist.
Note: This is technically all you need to manually enter the matches from a randomized season, but can add more commands to make manual input easier.

**swap-home-away <season-number: int> <week-number: int> <team: discord role> <squad: int>**  
Swaps who is home and who is away for that squad's match for that season and week.  
Throws if season/week/team/squad doesn't exist.

**publish-season [publish-channel: discord channel]**  
Publishes the current unpublished season to be the current season and it's first week to be the current week. Displays "New season!" message in (channel where command is ran/the publish-channel).  
Dangerous; public confirmation message.

**publish-week [publish-channel: discord channel]**  
Publishes the next week of the current season. Displays "New week!" message in (channel where command is ran/the publish-channel).  
Semi-Dangerous; private confirmation message.

**submit-matches <team: discord role> <squad: int>**  
Gives an interaction prompting you to fill out the w/l for each member of the squad.  
Throws if squad doesn't exist.
