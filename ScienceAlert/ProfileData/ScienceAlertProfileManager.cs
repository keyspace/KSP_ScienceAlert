/******************************************************************************
                   Science Alert for Kerbal Space Program                    
 ******************************************************************************
    Copyright (C) 2014 Allen Mrazek (amrazek@hotmail.com)

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
 *****************************************************************************/
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ReeperCommon;
using ScienceAlert.ProfileData;

namespace ScienceAlert
{
    using ProfileTable = Dictionary<string, Profile>;   
    using VesselTable = Dictionary<Guid, Profile>;     

    /// <summary>
    /// The main purpose of making this a ScenarioModule is to simplify
    /// storing configs that have been "modified" per vessel, so that every
    /// vessel can have its own custom profile if it differs from its
    /// stored profile.
    /// </summary>
    [KSPScenario(ScenarioCreationOptions.AddToExistingCareerGames           |
                 ScenarioCreationOptions.AddToExistingScienceSandboxGames   |
                 ScenarioCreationOptions.AddToNewCareerGames                |
                 ScenarioCreationOptions.AddToNewScienceSandboxGames,       
                 GameScenes.FLIGHT)]
    class ScienceAlertProfileManager : ScenarioModule
    {
        private readonly string ProfileStoragePath = ConfigUtil.GetDllDirectoryPath() + "/profiles.cfg";
        ProfileTable storedProfiles;
        VesselTable vesselProfiles;

        private const string PERSISTENT_NODE_NAME = "ScienceAlert_Profiles";
        private const string STORED_NODE_NAME = "Stored_Profiles";
        public const int MAX_PROFILE_NAME_LENGTH = 28;

/******************************************************************************
 *                    Implementation Details
 ******************************************************************************/

#region intialization/deinitialization



        /// <summary>
        /// Load all saved profiles, register for events and other
        /// initialization tasks
        /// </summary>
        public override void OnAwake()
        {
            base.OnAwake();

            Log.Debug("ProfileManager.OnAwake");

            if (HighLogic.CurrentGame.config == null)
            {
                Log.Error("CurrentGame.config == null!");
                HighLogic.CurrentGame.config = new ConfigNode();
            }

            Settings.Instance.OnSave += OnSettingsSave; // this triggers saving of stored profiles

            GameEvents.onVesselChange.Add(OnVesselChange);
            GameEvents.onVesselDestroy.Add(OnVesselDestroy);
            GameEvents.onVesselCreate.Add(OnVesselCreate);
            GameEvents.onVesselWasModified.Add(OnVesselModified);
            GameEvents.onFlightReady.Add(OnFlightReady);
            GameEvents.onVesselWillDestroy.Add(OnVesselWillDestroy);
            //GameEvents.onGameStateSave.Add(OnGameSave);
            //GameEvents.onGameStateLoad.Add(OnGameLoad);
            GameEvents.onSameVesselUndock.Add(OnSameVesselUndock);
            GameEvents.onUndock.Add(OnUndock);

            Ready = false; // won't be ready until OnLoad
            Instance = this;

            LoadStoredProfiles();
        }



        /// <summary>
        /// Unregister for any events from the constructor; save 
        /// stored profiles
        /// </summary>
        private void OnDestroy()
        {
            Instance = null;

            Log.Debug("ProfileManager: OnDestroy");

            GameEvents.onVesselChange.Remove(OnVesselChange);
            GameEvents.onVesselDestroy.Remove(OnVesselDestroy);
            GameEvents.onVesselCreate.Remove(OnVesselCreate);
            GameEvents.onVesselWasModified.Remove(OnVesselModified);
            GameEvents.onFlightReady.Remove(OnFlightReady);
            GameEvents.onVesselWillDestroy.Remove(OnVesselWillDestroy);
            //GameEvents.onGameStateSave.Remove(OnGameSave);
            //GameEvents.onGameStateLoad.Remove(OnGameLoad);
            GameEvents.onSameVesselUndock.Remove(OnSameVesselUndock);
            GameEvents.onUndock.Remove(OnUndock);
          

            SaveStoredProfiles();
        }


        
        /// <summary>
        /// Load stored profiles from a ConfigNode in the ScienceAlert
        /// directory
        /// </summary>
        private void LoadStoredProfiles()
        {
            try
            {
                storedProfiles = new ProfileTable();

                if (!System.IO.File.Exists(ProfileStoragePath))
                {
                    Log.Warning("ProfileManager: Profile config not found at '{0}'", ProfileStoragePath);
                } else {
                    Log.Debug("ProfileManager: Loading profile config from '{0}'", ProfileStoragePath);

                    ConfigNode stored = ConfigNode.Load(ProfileStoragePath);

                    if (stored == null || !stored.HasNode(STORED_NODE_NAME))
                    {
                        Log.Error("ProfileManager: Failed to load config");
                    }
                    else
                    {
                        stored = stored.GetNode(STORED_NODE_NAME); // to avoid having an empty cfg, which will
                                                                          // cause KSP to hang at load

                        var profiles = stored.GetNodes("PROFILE");
                        Log.Verbose("Found {0} stored profiles to load", profiles.Length);

                        foreach (var profileNode in profiles)
                        {
                            try
                            {
                                Profile p = new Profile(profileNode);
                                p.modified = false; // by definition, stored profiles haven't been modified

                                storedProfiles.Add(p.name, p);
                                Log.Verbose("Loaded profile '{0}' successfully!", p.name);
                            } 
                            catch (Exception e) 
                            {
                                Log.Error("ProfileManager: profile '{0}' failed to parse; {1}", name, e);
                            }
                        }
                    }
                }

                // make sure there's a "default" config in there. Ideally the
                // user has created and saved over one but if not, we need
                // at least a default to give to vessels that are missing their
                // profiles
                if (DefaultProfile == null)
                    storedProfiles.Add("default", Profile.MakeDefault());

            } catch (Exception e)
            {
                Log.Error("ProfileManager: Exception loading stored profiles: {0}", e);

                // don't keep anything that might have been loaded; something's
                // gone seriously wrong but we might manage to salvage things if
                // we accept the loss of stored data and use the vessel-specific
                // profiles instead
                storedProfiles = new ProfileTable();
            }
        }



        /// <summary>
        /// Stored profiles go into a cfg ConfigNode in the ScienceAlert
        /// directory
        /// 
        /// Precondition: All profile names have been sanitized
        /// </summary>
        private void SaveStoredProfiles()
        {
            ConfigNode profiles = new ConfigNode(STORED_NODE_NAME); // note: gave it a name because an empty
                                                                     // ConfigNode will cause KSP to choke on load

            foreach (var kvp in storedProfiles)
            {
                try
                {
                    // if this happened, something broke when we were creating
                    // a profile (or potentially loading an unsanitized one)
                    if (!kvp.Key.ToLower().Equals(kvp.Value.name.ToLower()))
                        Log.Warning("ProfileManager.SavedStoredProfiles: stored key '{0}' does not match profile name '{1}'!", kvp.Key, kvp.Value.name);

                    Log.Verbose("Saving stored profile '{0}'", kvp.Key);
                    kvp.Value.OnSave(profiles.AddNode(new ConfigNode("PROFILE")));
                    Log.Verbose("Saved '{0}'", kvp.Value.name);
                } catch (Exception e)
                {
                    Log.Error("ProfileManager: Exception while saving '{0}': {1}", kvp.Key, e);
                }
            }

#if DEBUG
            Log.Debug("ProfileManager: stored profile ConfigNode: {0}", profiles.ToString());
#endif

            // note: removed because ConfigNode.Save seems to strip out
            //       the root node of the ConfigNode. That's bad because if there
            //       aren't any profiles saved (due to a fail somewhere) in the root
            //       node, the player's game will freeze at load when it encounters
            //       the empty cfg
            //if (!profiles.Save(ProfileStoragePath, "ScienceAlert stored profiles"))
                //Log.Error("ProfileManager: Error while saving stored profiles to '{0}'! Any changes this session have been lost!", ProfileStoragePath);

            System.IO.File.WriteAllText(ProfileStoragePath, profiles.ToString());
        } 



#endregion

#region GameEvents

        /// <summary>
        /// If the ship we're changing to has an unmodified profile (meaning we should use the stored profile),
        /// it might need to be updated. Just recreating it will do
        /// </summary>
        /// <param name="vessel"></param>
        private void OnVesselChange(Vessel vessel)
        {
            Log.Debug("ProfileManager.OnVesselChange: {0}", vessel.vesselName);

            if (vessel != null)
                if (vesselProfiles.ContainsKey(vessel.id))
                    if (!vesselProfiles[vessel.id].modified)
                    {
                        // it's possible the stored profile this one is based off of was
                        // modified in the meantime by the player, so bring ours up to date
                        var stored = FindStoredProfile(vesselProfiles[vessel.id].name);

                        // oops, looks like it was deleted! well we don't want to create
                        // it again next save when the user wants it gone so convert this
                        // profile from a stored to a modified vessel profile..
                        if (stored == null)
                        {
                            Log.Warning("ProfileManager.OnVesselChange: Vessel {0} refers to a missing stored profile '{1}'; converting it to vessel profile", vessel.id, vesselProfiles[vessel.id].name);

                            vesselProfiles[vessel.id].modified = true;
                        }
                        else
                        {
                            Log.Normal("ProfileManager.OnVesselChange: Bringing vessel {0} up to date on stored profile {1}", vessel.id, stored.name);
                            vesselProfiles[vessel.id] = stored.Clone();
                        }
                    }
        }



        /// <summary>
        /// Destroy old vessel profiles, if the vessel being destroyed has one
        /// </summary>
        /// <param name="vessel"></param>
        private void OnVesselDestroy(Vessel vessel)
        {
            Log.Debug("ProfileManager.OnVesselDestroy: {0}", vessel.vesselName);

            if (vesselProfiles.ContainsKey(vessel.id))
            {
                // note to self: it's not strictly necessary to delete it since unused
                // profiles won't be saved, but I can't think of a reason to keep it around
                // since we catch undock events already...
                Log.Normal("Deleting profile '{0}' since its vessel {1} was destroyed", vesselProfiles[vessel.id].name, vessel.id.ToString());
                vesselProfiles.Remove(vessel.id);
            }
        }



        /// <summary>
        /// If the new vessel was created out of a vessel that has a profile, it inherits its parent's
        /// profile
        /// </summary>
        /// <param name="newVessel"></param>
        private void OnVesselCreate(Vessel newVessel)
        {
            if (newVessel == null)
            {
                Log.Debug("ProfileManager.OnVesselCreate: new vessel is null");
                return;
            }

            try
            {
                Log.Debug("ProfileManager.OnVesselCreate: {0}", newVessel.vesselName);

                if (vesselProfiles == null)
                {
                    Log.Debug("  - ProfileManager hasn't initialized yet");
                    return; // we haven't even init yet
                }


                if (!newVessel.loaded) // there's no chance of this having come from an existing vessel, then (also it won't have a protoVessel)
                    return;

                // some other mods don't seem to set this. That's okay though, we want those vessels to have
                // default profiles anyway
                if (newVessel.protoVessel == null)
                {
                    Log.Debug("  - protoVessel is null");
                    return;
                }

                // another error trap in case a mod creates a vessel with no parts in it. I don't think I've seen this
                // happen but I didn't see a missing protoVessel exception coming either
                if (newVessel.protoVessel.protoPartSnapshots.Count == 0)
                {
                    Log.Debug("  - protoVessel part snapshot count is 0");
                    return;
                }


                if (FlightGlobals.ActiveVessel != newVessel && newVessel.vesselType != VesselType.Debris)
                {
                    Profile parentProfile = null;

                    // it's possible the new vessel is in fact packed (almost certain to be a DiscoverableObject)
                    // so we need to be careful not to access any parts if it is
                    // bugfix: newVessel.packed => newVessel.loaded. Thanks taniwha!
                    uint mid = !newVessel.loaded ? newVessel.protoVessel.protoPartSnapshots[newVessel.protoVessel.rootIndex].missionID : newVessel.rootPart.missionID;

                    Log.Debug("ProfileManager.OnVesselCreate: new vessel mission id = " + mid);

                    if (mid == FlightGlobals.ActiveVessel.rootPart.missionID)
                        if (vesselProfiles.ContainsKey(FlightGlobals.ActiveVessel.id))
                            if (vesselProfiles[FlightGlobals.ActiveVessel.id] == ActiveProfile)
                                parentProfile = ActiveProfile;





                    // if the active vessel isn't the parent then the player probably didn't
                    // cause this vessel to be created; nonetheless there may be edge cases 
                    // (collision? mods that allow eva to undock nodes?) so let's
                    // see if any vessel is our parent
                    if (parentProfile == null)
                    {
                        Log.Debug("  - active vessel isn't parent, searching all vessels");

                        var parentVessel = FlightGlobals.Vessels.SingleOrDefault(v =>
                               {
                                   if (v.rootPart != null)
                                       if (mid == v.rootPart.missionID)
                                           if (vesselProfiles.ContainsKey(v.id))
                                               return true;
                                   return false;
                               });

                        if (parentVessel != null) parentProfile = vesselProfiles[parentVessel.id];
                    }


                    if (parentProfile != null)
                    {
                        if (vesselProfiles.ContainsKey(newVessel.id))
                        {
                            Log.Error("ProfileManager.OnVesselCreate: Somehow we already have an entry for {0} called {1}; Investigate logic error", newVessel.id.ToString(), vesselProfiles[newVessel.id] != null ? vesselProfiles[newVessel.id].name : "<null vessel profile entry>");
                            return;
                        }

                        Log.Normal("New vessel created; assigning it a clone of parent's profile {0}", parentProfile.name);
                        vesselProfiles.Add(newVessel.id, parentProfile.Clone());
                    } // otherwise this is a vessel created out of the player's control, most likely an asteroid
                }
            }
            catch (Exception e)
            {
                Log.Error("ProfileManager.OnVesselCreate: Something went wrong while handling this event; {0}", e);
            }
        }



        private void OnVesselModified(Vessel vessel)
        {
            Log.Debug("ProfileManager.OnVesselModified: {0}", vessel.vesselName);   
        }



        private void OnFlightReady()
        {
            Log.Debug("ProfileManager.OnFlightReady");
        }


        private void OnVesselWillDestroy(Vessel vessel)
        {
            Log.Debug("ProfileManager.OnVesselWillDestroy: {0}", vessel.vesselName); 
        }


        private void OnSameVesselUndock(GameEvents.FromToAction<ModuleDockingNode, ModuleDockingNode> nodes)
        {
            Log.Debug("ProfileManager.OnSameVesselUndock: from {0}, to {1}", nodes.from.vessel.vesselName, nodes.to.vessel.vesselName);
        }


        private void OnUndock(EventReport report)
        {
            Log.Debug("ProfileManager.OnUndock: origin {0}, sender {1}", report.origin.name, report.sender);
        }


        /// <summary>
        /// Load vessel-specific ConfigNodes from the persistent file
        /// </summary>
        /// <param name="node"></param>
        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            if (!node.HasNode(PERSISTENT_NODE_NAME))
            {
                Log.Warning("Persistent save has no saved profiles");
                vesselProfiles = new VesselTable();
                Ready = true;
                return;
            }
            else node = node.GetNode(PERSISTENT_NODE_NAME);

            //List<string> errors = new List<string>();
            vesselProfiles = new VesselTable();

            var guidStrings = node.nodes.DistinctNames();
            Log.Verbose("ProfileManager: {0} vessel profile nodes found", guidStrings.Length);

            foreach (var strGuid in guidStrings)
            {
                Log.Debug("Loading node with name '{0}'", strGuid);

                try
                {
                    Guid guid = new Guid(strGuid);  // could throw an exception if string is malformed
                    Log.Debug("Guid created: {0}", guid.ToString());

                    // confirm a vessel with this Guid exists
                    if (!FlightGlobals.Vessels.Any(v => v.id == guid))
                    {
                        Log.Warning("Did not find a vessel that matches {0}; check destruction event code", guid.ToString());
                        continue;
                    }

                    // confirm that we don't have duplicate entries
                    if (vesselProfiles.ContainsKey(guid))
                    {
                        Log.Error("ProfileManager: Duplicate profile for vessel {0} found!", VesselIdentifier(guid), FlightGlobals.Vessels.Find(v => v.id == guid).vesselName);
                        continue;
                    }

                    // grab the node with this info
                    ConfigNode profileNode = node.GetNode(strGuid);

                    // create a profile out of the data stored in this node
                    Profile p = new Profile(profileNode);

                    // if modified is true => use the modified profile
                    // if modified is false THEN
                    //      if a stored profile of same name exists THEN 
                    //          clone the stored profile
                    //      Else
                    //          add to missing profile list
                    //          clone default profile
                    //      end
                    // end
                    if (p.modified)
                    {
                        Log.Verbose("Vessel {0} has a modified profile '{1}' stored.", VesselIdentifier(guid), p.name);

                        vesselProfiles.Add(guid, p);
                    }
                    else
                    {
                        if (HaveStoredProfile(p.name))
                        {
                            Log.Verbose("Vessel {0} has stored profile '{1}'", VesselIdentifier(guid), p.name);

                            // use the stored profile
                            vesselProfiles.Add(guid, FindStoredProfile(p.name).Clone());

                        }
                        else
                        {
                            Log.Warning("Vessel {0} refers to a stored profile '{1}' which was not found. Existing data has been converted to a vessel profile.", VesselIdentifier(guid), p.name);
                            p.modified = true;

                            vesselProfiles.Add(guid, p);

                            //// add to missing profile list and clone default
                            //errors.Add(string.Format("Stored profile '{0}' not found for {1}", p.name, VesselIdentifier(guid)));
                            //Log.Error("Could not find profile '{0}' for vessel {1}. Will use default.", p.name, VesselIdentifier(guid));

                            //// note to self: this isn't the same as Profile.MakeDefault();
                            //// that is used for a truly default, totally unmodified profile.
                            //// DefaultProfile will locate a profile called "default" instead,
                            //// which may be custom-made by the user if they overwrite the 
                            //// standard one
                            //vesselProfiles.Add(guid, DefaultProfile.Clone());

                        }
                    }
                }
                catch (Exception e)
                {
                    Log.Error("ProfileManager: Exception while loading '{0}': {1}", strGuid, e);
                }
            }

            //if (errors.Count > 0)
            //{
            //    string message = "Errors while loading profiles:\n\n";

            //    errors.ForEach(err => message += err + "\n");
            //    message += "\nVessel(s) have been assigned the \"default\" profile.";

            //    Log.Debug("Errors encountered during profile load: {0}", message);

            //    PopupDialog.SpawnPopupDialog("ScienceAlert: Profile Manager", message, "Okay", false, HighLogic.Skin);
            //}

            Ready = true;
        }


     
        /// <summary>
        /// Save vessel-specific profiles to the persistent ConfigNode
        /// </summary>
        /// <param name="node"></param>
        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);

            //Log.Verbose("ProfileManager.OnSave: {0}", node.ToString());

            if (!node.HasNode(PERSISTENT_NODE_NAME)) node.AddNode(PERSISTENT_NODE_NAME);

            node = node.GetNode(PERSISTENT_NODE_NAME);

            Log.Verbose("ProfileManager.OnSave: Saving {0} vessel profiles", vesselProfiles.Count);

            foreach (var kvp in vesselProfiles)
            {
                try
                {
                    if (!FlightGlobals.Vessels.Any(v => v.id == kvp.Key))
                    {
                        Log.Normal("ProfileManager.OnSave: Not saving profile '{0}' because vessel {1} does not exist.", kvp.Value.name, kvp.Key.ToString());
                        continue;
                    }
                    else
                    {
                        Log.Verbose("ProfileManager.OnSave: saving vessel profile '{0}'", kvp.Value.name);
                        kvp.Value.OnSave(node.AddNode(new ConfigNode(kvp.Key.ToString())));
                    }
                }
                catch (Exception e)
                {
                    Log.Error("ProfileManager.OnSave: Exception while saving profile '{0}': {1}", string.Format("{0}:{1}", kvp.Key.ToString(), kvp.Value.name), e);
                    continue;
                }
            }

            Log.Verbose("ProfileManager.OnGameSave: Finished");
        }

#endregion

#region other events

        /// <summary>
        /// Called when Settings.Save is called
        /// </summary>
        /// <param name="node"></param>
        public void OnSettingsSave()
        {
            Log.Debug("ProfileManager.OnSettingsSave");

            SaveStoredProfiles();
        }

#endregion

#region Interaction methods
        public static ScienceAlertProfileManager Instance { private set; get; }
        public bool Ready { private set; get; }

        public static Profile DefaultProfile
        {
            get
            {
                var key = Instance.storedProfiles.Keys.SingleOrDefault(k => k.ToLower().Equals("default"));

                if (string.IsNullOrEmpty(key))
                {
                    Log.Error("ProfileManager.DefaultProfile: failed to find a default profile! Creating one.");
                    key = "default";
                    Instance.storedProfiles.Add(key, Profile.MakeDefault());
                }

                return Instance.storedProfiles[key];
            }
        }



        public static Profile ActiveProfile
        {
            get
            {
                var vessel = FlightGlobals.ActiveVessel;

                if (vessel == null) 
                {
                    Log.Debug("WARN: ProfileManager.ActiveProfile: vessel is null");
                    return null;
                }
                if (!Instance.vesselProfiles.ContainsKey(vessel.id))
                {
                    Log.Normal("Vessel {0} does not have a vessel profile entry. Using default.", Instance.VesselIdentifier(vessel.id));
                    Instance.vesselProfiles.Add(vessel.id, DefaultProfile.Clone());
                }

                return Instance.vesselProfiles[vessel.id];
            }
        }




        public static bool HasActiveProfile
        {
            get
            {
                return FlightGlobals.ActiveVessel != null;
            }
        }



        public static int Count
        {
            get
            {
                if (Instance.storedProfiles != null)
                    return Instance.storedProfiles.Count;
                return 0;
            }
        }



        public static ProfileTable.KeyCollection Names
        {
            get
            {
                return Instance.storedProfiles.Keys;
            }
        }

        public static Profile GetProfileByName(string name)
        {
            var p = FindStoredProfile(name);
            if (p == null)
                Log.Error("Failed to find profile with key '{0}'", name);

            return p;
        }



        public static ProfileTable Profiles
        {
            get
            {
                return Instance.storedProfiles;
            }
        }



        /// <summary>
        /// Adds the specified profile as an unmodified, stored profile.
        ///   Important note: does NOT save to disk
        /// </summary>
        /// <param name="profile"></param>
        /// <returns></returns>
        public static void StoreActiveProfile(string name)
        {
            Profile p = ActiveProfile;

            p.name = name;
            p.modified = false;

            Profile newProfile = p.Clone();

            Log.Verbose("Adding new profile '{0}'..", p.name);

            var existing = FindStoredProfile(newProfile.name);
            if (existing != null) { Log.Warning("Overwriting existing profile"); Instance.storedProfiles.Remove(existing.name); }

            Instance.storedProfiles.Add(name, newProfile);
            Log.Verbose("Successfully added or updated profile");
        }



        public static void DeleteProfile(string name)
        {
            var p = FindStoredProfile(name);

            if (p != null)
            {
                Log.Normal("Deleting stored profile '{0}'", name);
                Instance.storedProfiles.Remove(name);
            }
            else Log.Warning("ProfileManager: Cannot delete profile '{0}' because it does not exist");
        }



        /// <summary>
        /// Renames a profile. If it's stored, the stored name itself is changed otherwise only
        /// the vessel profile is affected
        /// </summary>
        /// <param name="oldName"></param>
        /// <param name="newName"></param>
        public static void RenameProfile(string oldName, string newName)
        {
            var p = FindStoredProfile(oldName);

            if (p != null)
            {
                if (DefaultProfile.Equals(p))
                {
                    Log.Warning("User attempting to rename default profile. Renaming a clone instead.");
                    var cloned = p.Clone();

                    cloned.name = newName;
                    AssignAsActiveProfile(cloned);

                    cloned.modified = p.modified;

                    // if we're dealing with a stored profile here, we need to actually save the new clone
                    // else it won't appear for other craft
                    if (!cloned.modified)
                        StoreActiveProfile(newName);
                    
                }
                else
                {
                    Log.Normal("Renaming stored profile '{0}' to '{1}'", oldName, newName);
                    p.name = newName;
                }
            }
            else Log.Warning("ProfileManager: Cannot rename profile '{0}' because it was not found.");
        }



        /// <summary>
        /// Loads a stored profile
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public static bool LoadStoredAsActiveProfile(string name)
        {
            var p = FindStoredProfile(name);

            if (p == null)
            {
                Log.Error("ProfileManager: Cannot load '{0}' as active profile because it was not found.", name);
                return false;
            }
            else
            {
                var vessel = FlightGlobals.ActiveVessel;
                if (vessel == null)
                {
                    Log.Error("ProfileManager: Cannot load profile because vessel is null"); return false;
                }

                Profile newProfile = p.Clone();
                newProfile.modified = false; // should already be false, just making sure

                Instance.vesselProfiles[vessel.id] = newProfile;
                return true;
            }
        }



        /// <summary>
        /// Simply assigns the specified profile as the active one. This is 
        /// meant for vessel-persistent profiles, not for loading stored ones
        /// </summary>
        /// <param name="p"></param>
        /// <returns></returns>
        public static bool AssignAsActiveProfile(Profile p)
        {
            var vessel = FlightGlobals.ActiveVessel;

            if (vessel != null)
                if (p != null)
                {
                    Instance.vesselProfiles[vessel.id] = p;
                    return true;
                }

            return false;
        }

#endregion

#region internal methods

        private static Profile FindStoredProfile(string name)
        {
            var key = Instance.storedProfiles.Keys.SingleOrDefault(k => k.ToLower().Equals(name.ToLower()));

            if (string.IsNullOrEmpty(key))
                return null;
            return Instance.storedProfiles[key];
        }



        public static bool HaveStoredProfile(string name)
        {
            return FindStoredProfile(name) != null;
        }



        private string FindVesselName(Guid guid)
        {
            Vessel vessel = FlightGlobals.Vessels.SingleOrDefault(v => v.id == guid);
            if (vessel == null) return string.Format("<vessel {0} not found>", guid.ToString());
            return vessel.vesselName;
        }
      


        private string VesselIdentifier(Guid guid)
        {
            return string.Format("{0}:{1}", guid.ToString(), FindVesselName(guid));
        }
#endregion
    }
}
