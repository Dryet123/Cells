using System.Diagnostics;
using Cells.Core;

namespace Cells.UI;

// First-run install, updates, and the uninstall started from Settings > Apps.
public static class Setup
{
    public enum Outcome { Continue, Exit }

    // Called when the exe runs from anywhere but the install folder, e.g. straight from Downloads.
    public static Outcome OfferInstall()
    {
        var mine = Installer.CurrentVersion;
        var installed = Installer.InstalledVersion;
        if (installed != null && installed == mine && Installer.IsSameAsInstalled())
        {
            Process.Start(new ProcessStartInfo(Installer.InstalledExe) { UseShellExecute = true });
            return Outcome.Exit;
        }

        var where = $"Location: {Installer.InstallDir}";
        var choice = installed == null
            ? Dialog.Ask(Theme.Light, "Install Cells?",
                "It will be set up for your Windows user, no admin rights needed: added to the Start menu, started with Windows " +
                "in the tray, and listed in Settings > Apps so it can be removed like any other app.", where, "Install", "Just run it")
            : installed <= mine
                ? Dialog.Ask(Theme.Light, installed < mine ? $"Update to {Installer.Short(mine)}?" : "Replace the installed copy?",
                    $"Version {Installer.Short(installed)} is installed. Updating keeps your devices and settings.", where, "Update", "Just run it")
                : Dialog.Ask(Theme.Light, "A newer version is installed",
                    $"Version {Installer.Short(installed)} is already installed, this file is {Installer.Short(mine)}.", where, "Open installed", "Run this one");

        switch (choice)
        {
            case DialogChoice.Primary when installed != null && installed > mine:
                Process.Start(new ProcessStartInfo(Installer.InstalledExe) { UseShellExecute = true });
                return Outcome.Exit;
            case DialogChoice.Primary:
                try
                {
                    Installer.Install();
                    Process.Start(new ProcessStartInfo(Installer.InstalledExe) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                    Dialog.Ask(Theme.Light, "Couldn't install", ex.Message, null, "OK", null);
                }
                return Outcome.Exit;
            case DialogChoice.Secondary:
                return Outcome.Continue;
            default:
                return Outcome.Exit;
        }
    }

    public static void Uninstall()
    {
        if (Dialog.Ask(Theme.Light, "Uninstall Cells?", "This removes the app, its Start menu shortcut and autostart.", null, "Uninstall", "Cancel")
            != DialogChoice.Primary) return;
        bool deleteData = Dialog.Ask(Theme.Light, "Also delete settings?",
            $"Remembered devices, names and widget position are kept in {AppPaths.DataDir} in case you install it again.",
            null, "Delete", "Keep") == DialogChoice.Primary;

        try
        {
            Installer.StopInstalledCopy();
            Installer.Unregister();
            if (!deleteData)
            {
                // A later reinstall should register autostart again.
                var settings = Settings.Load();
                settings.AutostartRegistered = false;
                settings.Save();
            }
            Installer.ScheduleRemoval(deleteData);
            Dialog.Ask(Theme.Light, "Cells was removed", "", null, "OK", null);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            Dialog.Ask(Theme.Light, "Uninstall failed", ex.Message, null, "OK", null);
        }
    }
}
