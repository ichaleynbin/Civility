using System;
using System.Collections.Generic;
using UnityEngine;
using Facepunch;
using Rust;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System.Linq;
using Oxide.Core.Configuration;

// Requires: Civility

namespace Oxide.Plugins
{
    [Info( "Civility Law: Murder", "ichaleynbin", "1.2.0")]
    [Description( "Law specifics for detecting Murders.")]

    class Murder : Civility
    {

	[PluginReference] Plugin EventManager;
	[PluginReference] Civility Civility;
	Dictionary< ulong, ulong > LastAttackerOf = new Dictionary< ulong, ulong >();
	Dictionary< ulong,List<LimitedEngagement> > Engagements = new Dictionary < ulong, List<LimitedEngagement> > ();
	
	public string Crime = "Murder";
	
        
        ConfigData configData;

        class LimitedEngagement
        {
            public ulong Initiator { get; set; }
            public ulong Defender { get; set; }
            public uint startTime { get; set; }
        }
        
        class ConfigData
        {
            public VersionNumber Version { get; set; }
            public SettingsData Settings { get; set; }
        }
        
        class SettingsData
        {
            public bool AnnounceWhenLoaded {get; set; }
            public bool CastleDoctrine {get; set; }
            public float EngagementLength { get; set; }
        }
        
        public override string CivilityType()
        {
            return "Law";
        }
        
        public override string CivilityName()
        {
            return "Murder";
        }
        
        void LoadData()
        {
            try
            {
                configData = Config.ReadObject<ConfigData>();
                if (configData.Version != Version)
                {
                    Puts("Version changed, generating fresh config");
                    LoadDefaultConfig();
                }
            }
            catch
            {
                Puts("Garbage Config, generating new");
                LoadDefaultConfig();
            }
        }
        
        protected override void LoadDefaultConfig()
        {
            configData = new ConfigData
            {
                Version = Version,
                Settings = new SettingsData
                {
                    AnnounceWhenLoaded = false,
                    CastleDoctrine = true,
                    EngagementLength = 180f,
                }
            };
            Config.WriteObject(configData, true);
        }
        
        void TurnIntoMurderer(ulong murderer_id, ulong victim_id)
        {                        
            PlayerData murderer = Players[murderer_id];
            ChatToAll("Murder",murderer.name,Players[victim_id].name);
            AddCrime(murderer,Crime,victim_id);
        }
        
        
        //    [ChatCommand("lol")]
        void lol(BasePlayer player)
        {
            AddCrime(Players[player.userID],"Murder",player.userID);
        }
        
        
        
        void CheckEngagements(List<LimitedEngagement> engagements)
        {
            List<LimitedEngagement> deadEngagements = new List<LimitedEngagement>();
            uint currentTime = realTime.GetUnixTimestamp();
            foreach (LimitedEngagement engagement in engagements)
            {
                if (currentTime >= (engagement.startTime + configData.Settings.EngagementLength))
                    deadEngagements.Add(engagement);
            }
            foreach (LimitedEngagement deadEngagement in deadEngagements)
            {
                BasePlayer defender = Players[deadEngagement.Defender].playa;
                BasePlayer initiator = Players[deadEngagement.Initiator].playa;
                ChatToPlayer(defender,"LimitedEngagementOver",initiator.displayName);
                ChatToPlayer(initiator,"LimitedEngagementOver",defender.displayName);
                Puts("Engagment Ending");
                engagements.Remove(deadEngagement);
            }
        }
        
        void CheckAllEngagements()
        {
            foreach( KeyValuePair<ulong,List<LimitedEngagement>> pair in Engagements)
            {
                CheckEngagements(pair.Value);
            }
        }
        
        bool CanPVP(BasePlayer initiator, BasePlayer defender)     
        {
            if (Players[defender.userID].canBeKilled)
            {
                // Plugins have marked this defender OK to kill.
                return true;
            }
            else if (!Players[defender.userID].isAwake)
                return false;

            if (Engagements.ContainsKey(initiator.userID))
            {
                // Initiator has limited engagements. Check them for defender.
                foreach (LimitedEngagement engagement in Engagements[initiator.userID])
                {
                    if( engagement.Initiator == defender.userID)
                    {
                        // Limited Engagement present with this initiator allowed to shoot this defender.
                        return true;
                    }
                }
            }
            
            if (configData.Settings.CastleDoctrine && initiator.CanBuild() && !defender.CanBuild())
            {
                //Castle Doctrine test positive, pvp allowed
                return true;
            }
            
            return false;
        }
        
        
        
        void OnEntityTakeDamage( BaseCombatEntity entity, HitInfo info )
        {
            if (entity == null)
                return; 
            BasePlayer defender = entity.ToPlayer();
            if (defender != null)
            {
                // Prevent accidentally punishing the wrong person for other deaths.
                if( LastAttackerOf.ContainsKey(defender.userID) )
                    LastAttackerOf.Remove( defender.userID );
            }
            else
                return;
            if (info?.Initiator == null)
                return;
            BasePlayer initiator = info.Initiator.ToPlayer();
            if (initiator != null)
            {
                
                if (defender.userID != initiator.userID)
                {
                    //This damage is not self-harm, so lets do stuff.
                    PlayerData defenderData = Players[defender.userID];
                    if (!(bool)EventManager?.Call("isPlaying",defender))
                    {
                        Puts("Got 1");
                        if (!CanPVP(initiator,defender))
                        {
                            // This defender is not legally killable. Spawn or extend limited engagement for defender.
                            LimitedEngagement engagement=null;
                            if (Engagements.ContainsKey(defender.userID))
                            {
                                foreach ( LimitedEngagement currentEngagement in Engagements[defender.userID])
                                {
                                    if (currentEngagement.Initiator == initiator.userID)
                                        engagement = currentEngagement;
                                }
                            }
                            else
                            {
                                Engagements[defender.userID] = new List<LimitedEngagement> ();
                            }
                            
                            if (engagement == null)
                            {
                                engagement = new LimitedEngagement();
                                engagement.Defender = defender.userID;
                                engagement.Initiator = initiator.userID;
                                Engagements[defender.userID].Add(engagement);
                                
                                ChatToPlayer(defender,"LimitedEngagement1",initiator.displayName,configData.Settings.EngagementLength);
                                ChatToPlayer(initiator,"LimitedEngagement1",defender.displayName,configData.Settings.EngagementLength);
                            }
                            else
                            {
                                ChatToPlayer(defender,"LimitedEngagementRefresh1",initiator.displayName,configData.Settings.EngagementLength);
                                ChatToPlayer(initiator,"LimitedEngagementRefresh2",defender.displayName,configData.Settings.EngagementLength);
                            }
                            
                            engagement.startTime = realTime.GetUnixTimestamp();
                            
                            
                            // Save the initiator as potential killer for bleed out et al.
                            LastAttackerOf[defender.userID] = initiator.userID;
                        }
                        else
                        {
                            if (Engagements.ContainsKey(initiator.userID))
                            {
                                foreach (LimitedEngagement engagement in Engagements[initiator.userID])
                                {
                                    if (engagement.Initiator == defender.userID)
                                    {
                                        engagement.startTime = realTime.GetUnixTimestamp();
                                        ChatToPlayer(defender,"LimitedEngagementRefresh1",initiator.displayName,configData.Settings.EngagementLength);
                                        ChatToPlayer(initiator,"LimitedEngagementRefresh2",defender.displayName,configData.Settings.EngagementLength);
                                        
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        
        void ProcessDeath(BasePlayer victim, HitInfo info)
        {
            if (victim == null) 
            {
                // Victim is nonexistant, we don't care. Why was this function called?
                return;
            }
            
            if (victim.IsWounded())
            {
                // Victim is currently wounded, don't blame for murder-yet. 
                return;
            }

            if (info?.Initiator != null)
            {
                BasePlayer killer = info.Initiator.ToPlayer();
                
                if (killer?.userID == null)
                {
                    //Killer exists but is not a player. Ignore this death.
                    return;
                }
                if (victim.userID == killer.userID)
                {
                    //Suicide. Ignore this death.
                    return;
                }
                if (!CanPVP(killer, victim))
                {
                    // Victim was not legally killable.
                    TurnIntoMurderer(killer.userID,victim.userID);
                    return;
                }
            }
            else
            {
                //Player has died but we don't have someone to blame. Attempt to blame last attacker.
                try
                {
                    TurnIntoMurderer(LastAttackerOf[victim.userID],victim.userID);
                }
                catch { }
            }
            LastAttackerOf.Remove(victim.userID);
            
        }

        void OnPlayerDie(BasePlayer victim, HitInfo info)
        {
            timer.Once(1f, () => {ProcessDeath( victim, info);} );
        }
        
        void SaveData()
        {
            Config.WriteObject(configData,true);
        }
        
        void OnServerSave()
        {
            SaveData();
        }
        
        void Init()
        {
            LoadData();
            Messages["Murder"] = "{0} has just murdered {1}!" ;
            Messages["LimitedEngagement1"] = "You have a new Limited Engagement with {0} for {1} seconds!"; 
            Messages["LimitedEngagement2"] = "{0} has a new Limited Engagement with you for {1} seconds!"; 
            Messages["LimitedEngagementOver"] = "Your Limited Engagement with {0} has ended!";
            Messages["AnnounceMurderLoad"] = "Law: Murder plugin Version {0} by ichaleynbin has been loaded"; 
            Messages["LimitedEngagementRefresh1"] = "Your Limited Engagement with {0} has just been refreshed and will last another {1} seconds!";
            Messages["LimitedEngagementRefresh2"] = "You have just refreshed your Limited Engagement with {0}; it will last another {1} seconds!";
        }
        
        void Loaded()
        {
            
            CivilityLoad();
            timer.Repeat(10f,0,CheckAllEngagements);
            if (configData.Settings.AnnounceWhenLoaded)
            {
                timer.Once(1f, () => {ChatToAll("AnnounceMurderLoad",Version); });
                
            }
            
        }
    }
}

