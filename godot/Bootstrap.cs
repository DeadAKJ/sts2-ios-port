using System;
using Godot;
using STS2Mobile;

public partial class Bootstrap : Control
{
    private Label _statusLabel;
    private ProgressBar _progressBar;

    public override void _Ready()
    {
        _statusLabel = GetNode<Label>("%StatusLabel");
        _progressBar = GetNode<ProgressBar>("%ProgressBar");

        Callable.From(InitializeGame).CallDeferred();
    }

    private void InitializeGame()
    {
        UpdateStatus("Applying iOS compatibility patches...", 0.2f);
        try
        {
            ModEntry.Apply();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2iOS] Error applying patches: {ex}");
        }

        UpdateStatus("Checking game assets...", 0.4f);

        // Check if assets are already mounted
        if (ResourceLoader.Exists("res://scenes/game.tscn"))
        {
            LaunchGame();
            return;
        }

        // Check user documents folder (Documents/SlayTheSpire2.pck)
        if (FileAccess.FileExists("user://SlayTheSpire2.pck"))
        {
            UpdateStatus("Loading SlayTheSpire2.pck from Documents...", 0.6f);
            if (ProjectSettings.LoadResourcePack("user://SlayTheSpire2.pck"))
            {
                LaunchGame();
                return;
            }
        }

        // Check bundled pck
        if (FileAccess.FileExists("res://SlayTheSpire2.pck"))
        {
            UpdateStatus("Loading bundled SlayTheSpire2.pck...", 0.6f);
            if (ProjectSettings.LoadResourcePack("res://SlayTheSpire2.pck"))
            {
                LaunchGame();
                return;
            }
        }

        // Missing PCK: Show instructions
        ShowMissingPckInstructions();
    }

    private void UpdateStatus(string message, float progress)
    {
        if (_statusLabel != null) _statusLabel.Text = message;
        if (_progressBar != null) _progressBar.Value = progress * 100;
    }

    private void LaunchGame()
    {
        UpdateStatus("Launching Slay the Spire 2...", 1.0f);
        GetTree().ChangeSceneToFile("res://scenes/game.tscn");
    }

    private void ShowMissingPckInstructions()
    {
        if (_statusLabel != null)
        {
            _statusLabel.Text = 
                "Game Data Not Found!\n\n" +
                "To play Slay the Spire 2 on your iPhone:\n" +
                "1. Open the 'Files' app on this iPhone (or connect to PC via iTunes/Finder).\n" +
                "2. Go to: 'On My iPhone' -> 'Slay the Spire 2'.\n" +
                "3. Copy your 'SlayTheSpire2.pck' file into that folder.\n" +
                "4. Close and re-open this app.";
        }
        if (_progressBar != null)
        {
            _progressBar.Visible = false;
        }
    }
}
