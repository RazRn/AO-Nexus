using System;
using AOSharp.Core;
using AOSharp.Common.Unmanaged.Imports;
using NexusTelemetry;

static class TeamLeaderTests
{
    static int checks;
    static void Check(bool expected, string reason)
    {
        if (NexusHealthPlugin.ReadTeamLeader() != expected) throw new Exception(reason);
        checks++;
    }
    static int Main()
    {
        try
        {
            TeamViewModule_c.Instance = new IntPtr(1);
            TeamViewModule_c.InTeam = TeamViewModule_c.Leader = 1;
            Team.IsInTeam = Team.IsLeader = false;
            Check(true, "Visible team leader survives an empty engine team query");
            TeamViewModule_c.Leader = 0;
            Team.IsInTeam = Team.IsLeader = true;
            Check(false, "Leadership transfer in the Team window removes the badge");
            TeamViewModule_c.InTeam = 0;
            TeamViewModule_c.Leader = 1;
            Check(false, "A stale leader flag without a team cannot show a badge");
            TeamViewModule_c.Instance = IntPtr.Zero;
            Check(true, "Engine leader is used when no Team window exists");
            Team.IsInTeam = false;
            Check(false, "Solo engine leader flag is rejected");
            Team.IsInTeam = true;
            Team.IsLeader = false;
            Check(false, "Engine team member has no leader badge");
            var player=DynelManager.LocalPlayer;
            var monitor=new NexusHealthPlugin();
            DynelManager.Characters=new[]{
                new SimpleChar {Instance=101,Player=false,IsAttacking=true,FightingTarget=player},
                new SimpleChar {Instance=102,Player=false,IsAttacking=true,FightingTarget=player},
                new SimpleChar {Instance=103,Player=false,IsAttacking=true,FightingTarget=player},
                new SimpleChar {Instance=104,Player=false,IsAttacking=true,FightingTarget=player},
                new SimpleChar {Instance=105,Player=true,IsAttacking=true,FightingTarget=player},
                new SimpleChar {Instance=106,Player=false,Pet=true,IsAttacking=true,FightingTarget=player},
                new SimpleChar {Instance=107,Player=false,IsAttacking=false,FightingTarget=player},
                new SimpleChar {Instance=108,Player=false,IsAlive=false,IsAttacking=true,FightingTarget=player},
                new SimpleChar {Instance=109,Player=false,IsAttacking=true,FightingTarget=new SimpleChar {Instance=99}},
                new SimpleChar {Instance=110,Player=false,Owner=20,IsAttacking=true,FightingTarget=player},
                new SimpleChar {Instance=111,Player=false,Type=(int)AOSharp.Common.GameData.PetType.Attack,IsAttacking=true,FightingTarget=player},
                new SimpleChar {Instance=112,Player=false,IsAttacking=true,FightingTarget=player}
            };
            player.Pets=new[]{new Pet {Identity=DynelManager.Characters[11].Identity}};
            var aggro=monitor.ReadAggro(player);
            if(aggro.Item1!=4 || aggro.Item2.Length!=3 || aggro.Item2[0]!="Nearby 101")throw new Exception("Aggro counts unique living attackers, excludes players/pets/other targets, and bounds names");
            checks++;
            DynelManager.Characters=new SimpleChar[0];
            aggro=monitor.ReadAggro(player);
            if(aggro.Item1!=0 || aggro.Item2.Length!=0)throw new Exception("An empty observation clears old attackers");
            checks++;
            Console.WriteLine("PASS: " + checks + " team-leader source checks.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
