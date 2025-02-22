﻿using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MaterialSkin;
using MaterialSkin.Controls;
using ExplorerTabUtility.WinAPI;
using ExplorerTabUtility.Managers;
using ExplorerTabUtility.Helpers;
using ExplorerTabUtility.Models;

namespace ExplorerTabUtility.Forms;

public partial class MainForm : MaterialForm
{
    private readonly HookManager _hookManager;
    private readonly ProfileManager _profileManager;
    private readonly SystemTrayIcon _notifyIconManager;
    private bool _isFirstShow = true;

    public MainForm()
    {
        InitializeComponent();
        SetupMaterialSkin();

        Size = SettingsManager.FormSize;

        _profileManager = new ProfileManager(flpProfiles);
        _hookManager = new HookManager(_profileManager);
        _notifyIconManager = new SystemTrayIcon(_profileManager, _hookManager, ShowForm);

        SetupEventHandlers();
        StartHooks();

        cbThemeIssue.Checked = SettingsManager.HaveThemeIssue;
        cbHideTrayIcon.Checked = SettingsManager.IsTrayIconHidden;
        UpdateTrayIconVisibility(false);
    }

    private void SetupMaterialSkin()
    {
        var materialSkinManager = MaterialSkinManager.Instance;
        materialSkinManager.EnforceBackcolorOnAllComponents = false;
        materialSkinManager.AddFormToManage(this);
        materialSkinManager.Theme = MaterialSkinManager.Themes.DARK;
        materialSkinManager.ColorScheme = new ColorScheme(Primary.Blue800, Primary.Blue900, Primary.LightBlue600, Accent.LightBlue200, TextShade.WHITE);
    }

    private void SetupEventHandlers()
    {
        Application.ApplicationExit += OnApplicationExit;
        _hookManager.OnVisibilityToggled += ToggleFormVisibility;
    }

    private void StartHooks()
    {
        if (SettingsManager.IsWindowHookActive) _hookManager.StartWindowHook();
        if (SettingsManager.IsMouseHookActive) _hookManager.StartMouseHook();
        if (SettingsManager.IsKeyboardHookActive) _hookManager.StartKeyboardHook();
        _hookManager.SetReuseTabs(SettingsManager.ReuseTabs);
    }

    private void BtnNewProfile_Click(object _, EventArgs __) => _profileManager.AddProfile();

    private void BtnImport_Click(object _, EventArgs __)
    {
        using var ofd = new OpenFileDialog();
        ofd.FileName = Constants.HotKeyProfilesFileName;
        ofd.Filter = Constants.JsonFileFilter;

        if (ofd.ShowDialog() != DialogResult.OK) return;
        var jsonString = System.IO.File.ReadAllText(ofd.FileName);
        _profileManager.ImportProfiles(jsonString);
        _notifyIconManager.UpdateMenuItems();
        UpdateTrayIconVisibility(false);
    }

    private void BtnExport_Click(object _, EventArgs __)
    {
        using var sfd = new SaveFileDialog();
        sfd.FileName = Constants.HotKeyProfilesFileName;
        sfd.Filter = Constants.JsonFileFilter;

        if (sfd.ShowDialog() != DialogResult.OK) return;
        using var openFile = sfd.OpenFile();
        var jsonString = _profileManager.ExportProfiles();
        var bytes = System.Text.Encoding.UTF8.GetBytes(jsonString);
        openFile.Write(bytes, 0, bytes.Length);
    }

    private void BtnSave_Click(object _, EventArgs __)
    {
        _profileManager.SaveProfiles();
        _notifyIconManager.UpdateMenuItems();
        UpdateTrayIconVisibility(false);
    }

    private void CbSaveProfilesOnExit_CheckedChanged(object _, EventArgs __)
    {
        SettingsManager.SaveProfilesOnExit = cbSaveProfilesOnExit.Checked;
    }

    private void CbThemeIssue_CheckedChanged(object sender, EventArgs e)
    {
        SettingsManager.HaveThemeIssue = cbThemeIssue.Checked;
    }

    private void CbHideTrayIcon_CheckedChanged(object sender, EventArgs e) => UpdateTrayIconVisibility(true);

    private void FlpProfiles_Resize(object _, EventArgs __)
    {
        foreach (Control c in flpProfiles.Controls)
            c.Width = flpProfiles.Width - 25;
    }

    private void MainForm_Deactivate(object _, EventArgs __) => flpProfiles.Focus();

    private void OnApplicationExit(object? _, EventArgs __)
    {
        _notifyIconManager.Dispose();
        _hookManager.Dispose();
    }

    private void UpdateTrayIconVisibility(bool showAlert)
    {
        // Check for valid toggle visibility profile
        var profile = _profileManager
            .GetProfiles()
            .FirstOrDefault(p =>
                p.IsEnabled &&
                p.Action == HotKeyAction.ToggleVisibility &&
                (p.IsMouse ? SettingsManager.IsMouseHookActive : SettingsManager.IsKeyboardHookActive));

        var canToggleVisibility = profile != null;
        if (showAlert && !SettingsManager.IsTrayIconHidden)
        {
            var message = canToggleVisibility
                ? $"You can show the app again by pressing {profile!.HotKeys!.HotKeysToString(profile.IsDoubleClick)}"
                : "Cannot hide tray icon if no hotkey is configured to toggle visibility.";

            if (cbHideTrayIcon.Checked)
                MessageBox.Show(this, message, Constants.AppName);
        }


        cbHideTrayIcon.Checked = cbHideTrayIcon.Checked && canToggleVisibility;
        SettingsManager.IsTrayIconHidden = cbHideTrayIcon.Checked;
        _notifyIconManager.SetTrayIconVisibility(!cbHideTrayIcon.Checked);
    }
    private void ShowForm()
    {
        WinApi.RestoreWindowToForeground(Handle);

        if (!_isFirstShow) return;

        _isFirstShow = false;
        Task.Delay(800).ContinueWith(_ => Invoke(AddDrawerOverlayForm));
    }

    private void ToggleFormVisibility()
    {
        Invoke(() =>
        {
            if (Visible)
                Hide();
            else
                ShowForm();
        });
    }

    protected override void OnResize(EventArgs e)
    {
        if (WindowState == FormWindowState.Minimized)
        {
            Hide();

            if (cbSaveProfilesOnExit.Checked)
            {
                _profileManager.SaveProfiles();
                _notifyIconManager.UpdateMenuItems();
                UpdateTrayIconVisibility(false);
            }
        }

        base.OnResize(e);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SettingsManager.FormSize = Size;

        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();

            if (cbSaveProfilesOnExit.Checked)
            {
                _profileManager.SaveProfiles();
                _notifyIconManager.UpdateMenuItems();
                UpdateTrayIconVisibility(false);
            }
        }
        base.OnFormClosing(e);
    }

    protected override void SetVisibleCore(bool value)
    {
        if (!IsHandleCreated)
        {
            // Show form on the very first run
            if (SettingsManager.IsFirstRun)
            {
                base.SetVisibleCore(true);
                SettingsManager.IsFirstRun = false;
                return;
            }

            value = false;
            CreateHandle();
        }
        base.SetVisibleCore(value);
    }
}