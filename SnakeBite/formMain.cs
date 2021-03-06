﻿using ICSharpCode.SharpZipLib.Zip;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SnakeBite
{
    public partial class formMain : Form
    {
        private Settings objSettings = new Settings();
        private formProgress progWindow = new formProgress();

        public formMain()
        {
            InitializeComponent();
        }

        private void formMain_Load(object sender, EventArgs e)
        {
            labelVersion.Text = Application.ProductVersion;
            bool firstRun = false;
            // check if user has specified valid install path
            if (!ModManager.ValidInstallPath)
            {
                MessageBox.Show("Please locate your MGSV installation to continue. If this is your first time running SnakeBite, it is recommended that you reset your MGSV installation before continuing.", "SnakeBite", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                this.Show();
                tabControl.SelectedIndex = 1;
                buttonFindMGSV_Click(null, null);
                if (!ModManager.ValidInstallPath)
                    Application.Exit();

                firstRun = true;
            }

            // Refresh mod list
            LoadInstalledMods(true);

            textInstallPath.Text = Properties.Settings.Default.InstallPath;

            this.Show();

            // check hash for dat file, if changed, rebuild database
            if (!ModManager.CheckDatHash())
            {
                if(!firstRun)
                    MessageBox.Show("Game data modified outside of SnakeBite. SnakeBite will now attempt to recache game data",
                                    "Game data hash mismatch", MessageBoxButtons.OK, MessageBoxIcon.Error);
                buttonBuildGameDB_Click(null, null);
            }
        }

        private void LoadInstalledMods(bool resetSelection = false)
        {
            if (File.Exists("settings.xml"))
            {
                objSettings.LoadSettings();
            }
            else
            {
                objSettings.ModEntries = new List<ModEntry>();
            }

            listInstalledMods.Items.Clear();

            if (objSettings.ModEntries.Count > 0)
            {
                panelModDetails.Visible = true;
                labelNoMods.Visible = false;
                foreach (ModEntry mod in objSettings.ModEntries)
                {
                    listInstalledMods.Items.Add(mod.Name);
                }

                if (resetSelection)
                {
                    if (listInstalledMods.Items.Count > 0)
                    {
                        listInstalledMods.SelectedIndex = 0;
                    }
                    else
                    {
                        listInstalledMods.SelectedIndex = -1;
                    }
                }
            }
            else
            {
                panelModDetails.Visible = false;
                labelNoMods.Visible = true;
            }
        }

        private void buttonUninstallMod_Click(object sender, EventArgs e)
        {
            if(!(listInstalledMods.SelectedIndex >= 0)) return;
            
            // Get selected mod
            ModEntry mod = objSettings.ModEntries[listInstalledMods.SelectedIndex];
            if (!(MessageBox.Show(String.Format("{0} will be uninstalled.", mod.Name), "SnakeBite", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)) return;


            showProgressWindow(String.Format("Please wait while {0} is uninstalled...", mod.Name));

            // Remove from mod database
            objSettings.ModEntries.Remove(mod);
            objSettings.SaveSettings();

            // Uninstall mod
            ModManager.UninstallMod(mod);

            // Update installed mod list
            LoadInstalledMods(true);
            hideProgressWindow();
        }

        private void buttonInstallMod_Click(object sender, EventArgs e)
        {
            // Show open file dialog for mod file
            OpenFileDialog openModFile = new OpenFileDialog();
            openModFile.Filter = "MGSV Mod Files|*.mgsv|All Files|*.*";
            DialogResult ofdResult = openModFile.ShowDialog();
            if (ofdResult != DialogResult.OK) return;

            // extract metadata and load
            FastZip unzipper = new FastZip();
            unzipper.ExtractZip(openModFile.FileName, ".", "metadata.xml");

            ModEntry modMetadata = new ModEntry();
            modMetadata.ReadFromFile("metadata.xml");
            File.Delete("metadata.xml"); // delete temp metadata

            if(!checkConflicts.Checked)
            {
                // search installed mods for conflicts
                List<string> conflictingMods = new List<string>();

                foreach (ModEntry modEntry in objSettings.ModEntries) // iterate through installed mods
                {
                    foreach (ModQarEntry qarEntry in modMetadata.ModQarEntries) // iterate qar files from new mod
                    {
                        ModQarEntry conflicts = modEntry.ModQarEntries.FirstOrDefault(entry => entry.FilePath == qarEntry.FilePath);
                        if (conflicts != null)
                        {
                            conflictingMods.Add(modEntry.Name);
                            break;
                        }
                    }

                    foreach (ModFpkEntry fpkEntry in modMetadata.ModFpkEntries) // iterate fpk files from new mod
                    {
                        ModFpkEntry conflicts = modEntry.ModFpkEntries.FirstOrDefault(entry => entry.FpkFile == fpkEntry.FpkFile && entry.FilePath == fpkEntry.FilePath);
                        if (conflicts != null)
                        {
                            if (!conflictingMods.Contains(modEntry.Name))
                            {
                                conflictingMods.Add(modEntry.Name);
                                break;
                            }
                        }
                    }
                }

                // if the mod conflicts, display message
                if (conflictingMods.Count > 0)
                {
                    string msgboxtext = "The selected mod conflicts with these mods:\n";
                    foreach (string Conflict in conflictingMods)
                    {
                        msgboxtext += Conflict + "\n";
                    }
                    msgboxtext += "\nPlease uninstall the mods above and try again.";
                    MessageBox.Show(msgboxtext, "Installation Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                bool sysConflict = false;
                // check for system file conflicts
                foreach (ModQarEntry gameQarFile in objSettings.GameData.GameQarEntries)
                {
                    if (modMetadata.ModQarEntries.Count(entry => entry.FilePath == gameQarFile.FilePath) > 0) sysConflict = true;
                }

                foreach (ModFpkEntry gameFpkFile in objSettings.GameData.GameFpkEntries)
                {
                    if (modMetadata.ModFpkEntries.Count(entry => entry.FilePath == gameFpkFile.FilePath && entry.FpkFile == gameFpkFile.FpkFile) > 0) sysConflict = true;
                }
                if (sysConflict)
                {
                    MessageBox.Show("The selected mod conflicts with existing MGSV system files.", "SnakeBite", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            DialogResult confirmInstall = MessageBox.Show(String.Format("You are about to install {0}, continue?", modMetadata.Name), "SnakeBite", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirmInstall == DialogResult.No) return;

            showProgressWindow(String.Format("Installing {0}, please wait...", modMetadata.Name));


            // Install mod to game database
            objSettings.ModEntries.Add(modMetadata);
            objSettings.SaveSettings();

            // Install mod to 01.dat
            ModManager.InstallMod(openModFile.FileName);

            LoadInstalledMods();
            listInstalledMods.SelectedIndex = listInstalledMods.Items.Count - 1;

            hideProgressWindow();
        }

        private void listInstalledMods_SelectedIndexChanged(object sender, EventArgs e)
        {
            // populate details pane
            if (listInstalledMods.SelectedIndex >= 0)
            {
                ModEntry selectedMod = objSettings.ModEntries[listInstalledMods.SelectedIndex];
                labelModName.Text = selectedMod.Name;
                labelModVersion.Text = selectedMod.Version;
                labelModAuthor.Text = "by " + selectedMod.Author;
                labelModAuthor.Left = labelModName.Left + labelModName.Width + 4;
                labelModWebsite.Text = selectedMod.Website;
                textDescription.Text = selectedMod.Description;
            }
        }

        private void buttonFindMGSV_Click(object sender, EventArgs e)
        {
            OpenFileDialog findMGSV = new OpenFileDialog();
            findMGSV.Filter = "Metal Gear Solid V|MGSVTPP.exe";
            DialogResult findResult = findMGSV.ShowDialog();
            if (findResult != DialogResult.OK) return;

            string filePath = findMGSV.FileName.Substring(0, findMGSV.FileName.LastIndexOf("\\"));
            textInstallPath.Text = filePath;
            Properties.Settings.Default.InstallPath = filePath;
            Properties.Settings.Default.Save();
        }

        private void showProgressWindow(string Text = "Processing...")
        {
            progWindow.Owner = this;
            progWindow.StatusText.Text = Text;

            progWindow.Show(this);
            this.Enabled = false;
        }

        private void hideProgressWindow()
        {
            this.Enabled = true;
            progWindow.Hide();
        }

        private void buttonBuildGameDB_Click(object sender, EventArgs e)
        {
            // attempt to determine which files are game files and which are mod files
            if (sender != null)
            {
                DialogResult areYouSure = MessageBox.Show("SnakeBite will recache the installed game data and remove any invalid mods. This may take some time.\n\nContinue?", "SnakeBite", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (areYouSure == DialogResult.No) return;
            }

            showProgressWindow("Rebuilding game data cache...");

            objSettings.GameData = ModManager.RebuildGameData();
            objSettings.SaveSettings();

            ModManager.CleanupModSettings();

            ModManager.UpdateDatHash();

            LoadInstalledMods(true);

            hideProgressWindow();

            if(sender!=null) tabControl.SelectedIndex = 0;
        }

        private void checkConflicts_CheckedChanged(object sender, EventArgs e)
        {
            if (checkConflicts.Checked)
            {
                MessageBox.Show("Disabling conflict checking will allow SnakeBite to overwrite existing files, potentially causing unpredictable results. It is recommended you backup 01.dat before continuing!", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void labelModAuthor_Click(object sender, EventArgs e)
        {

        }

        private void buttonLaunch_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start("steam://run/287700/");
            Application.Exit();
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start(this.linkLabel1.Text);
        }

        private void label2_Click(object sender, EventArgs e)
        {

        }

        private void buttonCreateBackup_Click(object sender, EventArgs e)
        {
            if (Directory.Exists("_backup")) Directory.Delete("_backup", true);
            Directory.CreateDirectory("_backup");

            // prompt user for backup filename
            SaveFileDialog saveBackup = new SaveFileDialog();
            saveBackup.Filter = "SnakeBite Backup|*.sbb";
            DialogResult saveResult = saveBackup.ShowDialog();
            if (saveResult != DialogResult.OK) return;

            // copy current settings
            objSettings.SaveSettings();
            File.Copy("settings.xml", "_backup\\settings.xml");

            // copy current 01.dat
            File.Copy(ModManager.GameArchivePath, "_backup\\01.dat");

            // compress to backup
            FastZip zipper = new FastZip();
            zipper.CreateZip(saveBackup.FileName, "_backup", true, "(.*?)");

            Directory.Delete("_backup", true);

            MessageBox.Show("Backup complete.", "SnakeBite", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void buttonRestoreBackup_Click(object sender, EventArgs e)
        {
            if (Directory.Exists("_backup")) Directory.Delete("_backup", true);

            OpenFileDialog openBackup = new OpenFileDialog();
            openBackup.Filter = "SnakeBite Backup|*.sbb";
            DialogResult openResult = openBackup.ShowDialog();
            if (openResult != DialogResult.OK) return;

            FastZip unzipper = new FastZip();
            unzipper.ExtractZip(openBackup.FileName, "_backup","(.*?)");

            File.Copy("_backup\\01.dat", ModManager.GameArchivePath, true);
            File.Copy("_backup\\settings.xml", "settings.xml", true);

            Directory.Delete("_backup", true);

            objSettings.LoadSettings();
            LoadInstalledMods(true);

            this.tabControl.SelectedIndex = 0;

            MessageBox.Show("Backup successfully restored!","SnakeBite",MessageBoxButtons.OK,MessageBoxIcon.Information);
        }
    }
}