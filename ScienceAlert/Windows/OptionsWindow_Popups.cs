﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using ReeperCommon;

namespace ScienceAlert.Windows
{
    using ProfileManager = ScienceAlertProfileManager;

    internal partial class OptionsWindow : MonoBehaviour, IDrawable
    {
        internal string editText = string.Empty;
        internal string lockName = string.Empty;
        internal bool flag = false;
        internal ProfileData.Profile editProfile;

        internal PopupDialog popup;
        internal Rect popupRect = new Rect(Screen.width / 2f - 380f / 2f, Screen.height / 2 - 200f / 2f, 380f, 200f);
        internal string badChars = "()[]?'\":#$%^&*~;\n\t\r!@,.{}/<>";

#region save popup
        void SpawnSavePopup()
        {
            editText = ProfileManager.ActiveProfile.name;
            LockControls("ScienceAlertSavePopup");
            popup = PopupDialog.SpawnPopupDialog(new MultiOptionDialog(string.Empty, DrawSaveWindow, "Choose a name for this profile"), false, HighLogic.Skin);
        }



        private void DrawSaveWindow()
        {
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            {
                GUI.SetNextControlName("ProfileName");
                editText = GUILayout.TextField(editText, GUILayout.ExpandWidth(true));

                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();

                if (AudibleButton(new GUIContent("Accept"))) SaveCurrentProfile();
                if (AudibleButton(new GUIContent("Cancel"))) DismissPopup();

                GUI.FocusControl("ProfileName");
            }
            GUILayout.EndVertical();

            if (Event.current.type == EventType.KeyUp)
            {
                if (Event.current.keyCode == KeyCode.KeypadEnter || Event.current.keyCode == KeyCode.Return)
                    SaveCurrentProfile();

                if (Event.current.keyCode == KeyCode.Escape)
                    DismissPopup();
            }
            
        }




        private void SaveCurrentProfile()
        {
            if (popup != null)
            {
                popup.Dismiss();
            }
            else editText = ProfileManager.ActiveProfile.name; // if there was no popup, SaveCurrentProfile was called directly

            if (ProfileManager.HaveStoredProfile(editText))
            {
                Log.Warning("Existing profile named '{0}' found! Asking user for overwrite permission.", editText);
                popup = PopupDialog.SpawnPopupDialog(new MultiOptionDialog("Overwrite existing profile?", string.Format("Profile '{0}' already exists!", editText), HighLogic.Skin, new DialogOption[] { new DialogOption("Overwrite", new Callback(SaveCurrentProfileOverwrite)), new DialogOption("Cancel", new Callback(DismissPopup)) }) { dialogRect = popupRect }, false, HighLogic.Skin);
            }
            else SaveCurrentProfileOverwrite(); // save to go ahead and save since no existing profile with this key exists
        }



        /// <summary>
        /// Saves the active profile as a stored profile and overwrites any
        /// previous existing version without informing the player
        /// </summary>
        private void SaveCurrentProfileOverwrite()
        {
            Log.Debug("OptionsWindow.SaveCurrentProfileOverwrite");
            ProfileManager.StoreActiveProfile(editText);
            Settings.Instance.Save();
            DismissPopup();
        }
        
#endregion

#region rename popup
        /// <summary>
        /// Renames the specified profile
        /// </summary>
        /// <param name="target"></param>
        /// <param name="clone"></param>
        private void SpawnRenamePopup(ProfileData.Profile target)
        {
            editProfile = target;
            editText = target.name;
            LockControls("ScienceAlertRenamePopup");
            //popup = PopupDialog.SpawnPopupDialog(new MultiOptionDialog(string.Empty, DrawRenameWindow, string.Format("Rename '{0}' to:", ProfileManager.ActiveProfile.name), HighLogic.Skin, new DialogOption[] { new DialogOption("Rename", new Callback(RenameTargetProfile)), new DialogOption("Cancel", new Callback(DismissPopup)) }), false, HighLogic.Skin);
            popup = PopupDialog.SpawnPopupDialog(new MultiOptionDialog(string.Empty, DrawRenameWindow, string.Format("Rename '{0}' to:", ProfileManager.ActiveProfile.name), HighLogic.Skin), false, HighLogic.Skin);
        }



        private void DrawRenameWindow()
        {
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            {
                GUI.SetNextControlName("ProfileName");

                if (Event.current.isKey)
                {
                    if (Event.current.type == EventType.KeyDown)
                        if (badChars.Contains(Event.current.character))
                            Event.current.character = '\0';
                    if (Event.current.keyCode == KeyCode.Space)
                        Event.current.character = '_';
                }

                editText = GUILayout.TextField(editText, GUILayout.ExpandWidth(true));

                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();

                if (AudibleButton(new GUIContent("Accept"))) RenameTargetProfile();
                if (AudibleButton(new GUIContent("Cancel"))) DismissPopup();

                GUILayout.EndHorizontal();

                GUI.FocusControl("ProfileName");
            }
            GUILayout.EndVertical();

            if (Event.current.isKey)
            {
                editText = editText.Replace(' ', '\0');

                if (Event.current.keyCode == KeyCode.KeypadEnter || Event.current.keyCode == KeyCode.Return)
                    RenameTargetProfile();

                if (Event.current.keyCode == KeyCode.Escape)
                    DismissPopup();
            }
        }



        /// <summary>
        /// Attempt to rename target profile. If it's a stored profile and one
        /// of that name already exists, ask user for confirmation
        /// </summary>
        private void RenameTargetProfile()
        {

            if (editProfile.modified || !ProfileManager.HaveStoredProfile(editProfile.name))
            {
                // vessel profile: no need to check stored data
                RenameTargetProfileOverwrite();
            }
            else
            {
                // check to see if this stored profile already exists
                if (ProfileManager.HaveStoredProfile(editText))
                {
                    // conflict!
                    popup.Dismiss();
                    popup = PopupDialog.SpawnPopupDialog(new MultiOptionDialog(string.Empty, string.Format("'{0}' already exists. Overwrite?", editText), HighLogic.Skin, new DialogOption[] { new DialogOption("Yes", new Callback(RenameTargetProfileOverwrite)), new DialogOption("No", new Callback(DismissPopup)) }) { dialogRect = popupRect }, false, HighLogic.Skin);
                    return;
                }
                else 
                {
                    RenameTargetProfileOverwrite(); // okay to overwrite
                }
            }
            
            DismissPopup();
        }



        /// <summary>
        /// Rename target profile and don't ask user for permission
        /// </summary>
        private void RenameTargetProfileOverwrite()
        {
            if (editProfile.modified == false && ProfileManager.HaveStoredProfile(editProfile.name))
            {
                ProfileManager.RenameProfile(editProfile.name, editText);

                // if the active profile is unmodified and links to the renamed stored 
                // profile, then rename it as well
                if (!ProfileManager.ActiveProfile.modified)
                    ProfileManager.ActiveProfile.name = editText;
            }
            else
            {
                editProfile.name = editText;
                editProfile.modified = true;
            }

            DismissPopup();
        }

#endregion

#region delete popup

        private void SpawnDeletePopup(ProfileData.Profile target)
        {
            editProfile = target;
            LockControls("ScienceAlertDeletePopup");

            popup = PopupDialog.SpawnPopupDialog(new MultiOptionDialog(string.Empty, string.Format("Are you sure you want to delete '{0}'?", target.name), HighLogic.Skin, new DialogOption("Confirm", DeleteTargetProfile), new DialogOption("Cancel", DismissPopup)), false, HighLogic.Skin);
        }



        private void DeleteTargetProfile()
        {
            DismissPopup();
            ProfileManager.DeleteProfile(editProfile.name);
        }



#endregion

        #region load popup

        private void SpawnOpenPopup(ProfileData.Profile target)
        {
            editProfile = target;
            LockControls("ScienceAlertOpenPopup");
            popup = PopupDialog.SpawnPopupDialog(new MultiOptionDialog(string.Empty, string.Format("Load '{0}'? Unsaved settings will be lost.", editProfile.name), HighLogic.Skin, new DialogOption("Confirm", LoadTargetProfile), new DialogOption("Cancel", DismissPopup)), false, HighLogic.Skin);
        }



        private void LoadTargetProfile()
        {
            DismissPopup();

            if (ProfileManager.AssignAsActiveProfile(editProfile.Clone()))
            {
                Log.Normal("Assigned new active profile: {0]", editProfile.name);
                submenu = OpenPane.None; // close panel
            }
            else Log.Error("Failed to load '{0}'", editProfile.name);
        }

        #endregion

        private void LockControls(string lockName)
        {
            this.lockName = lockName;
            InputLockManager.SetControlLock(ControlTypes.ACTIONS_ALL, lockName);
            Log.Debug("OptionsWindow_Popups: Locked controls with {0}", lockName);
        }



        private void DismissPopup()
        {
            Log.Debug("OptionsWindow.DismissPopup");
            if (popup) popup.Dismiss();
            InputLockManager.RemoveControlLock(lockName); lockName = string.Empty;
        }

        public bool HasOpenPopup { get { return popup != null; } }
    }
}
